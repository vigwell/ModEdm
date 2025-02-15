using Amazon.S3;
using Amazon.S3.Model;
using ImageMagick;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using static System.Net.Mime.MediaTypeNames;

namespace ModEdmZipAnalyzer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly S3Helper _s3Helper;
        private readonly FileProcessor _fileProcessor;
        private readonly int _intervalMinutes;
        private static double _isProcessing = 0;
        private readonly EventLog _eventLog;


        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _s3Helper = new S3Helper(configuration);
            _fileProcessor = new FileProcessor(logger, configuration);
            _intervalMinutes = configuration.GetValue<int>("ServiceSettings:IntervalMinutes");


            string eventLogSource = configuration.GetValue<string>("ServiceSettings:EventLogSource", "ModEdmZipAnalyzer");
            _eventLog = new EventLog("Application") { Source = eventLogSource };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (Interlocked.Exchange(ref _isProcessing, 1) == 1)
                {
                    _eventLog.WriteEntry("Previous process still running. Skipping this cycle.", EventLogEntryType.Warning);
                }
                else
                {
                    try
                    {
                        _eventLog.WriteEntry("Checking for ZIP files in S3 bucket...", EventLogEntryType.Information);
                        List<string> zipFiles = await _s3Helper.GetZipFilesAsync(_configuration.GetValue<bool>("ServiceSettings:GetOnlyNewZipFiles"));

                        if (zipFiles.Any())
                        {
                            _eventLog.WriteEntry($"Found {zipFiles.Count} ZIP files. Processing...", EventLogEntryType.Information);
                            await _fileProcessor.ProcessZipFiles(zipFiles, _s3Helper);
                        }
                        else
                        {
                            _eventLog.WriteEntry("No ZIP files found.", EventLogEntryType.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        _eventLog.WriteEntry($"Error: {ex.Message}", EventLogEntryType.Error);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isProcessing, 0);
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
            }
        }

        public override void Dispose()
        {
            _eventLog?.Dispose();
            base.Dispose();
        }
    }

    public class S3Helper
    {
        private readonly AmazonS3Client _s3Client;
        private readonly string _bucketName;

        public S3Helper(IConfiguration configuration)
        {
            _s3Client = new AmazonS3Client();
            _bucketName = configuration.GetValue<string>("AWS:BucketName");
        }

        public async Task<List<string>> GetZipFilesAsync(bool getOnlyNewZipFiles)
        {
            var request = new ListObjectsV2Request { BucketName = _bucketName };
            var response = await _s3Client.ListObjectsV2Async(request);
            var zipFiles = response.S3Objects.Where(o => o.Key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

            if (getOnlyNewZipFiles)
            {
                var jsonFiles = new HashSet<string>(
                    response.S3Objects
                        .Where(o => o.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        .Select(o => o.Key.Replace(".json", "", StringComparison.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase
                );

                zipFiles = zipFiles.Where(zip => !jsonFiles.Contains(zip.Key.Replace(".zip", "", StringComparison.OrdinalIgnoreCase))).ToList();
            }

            return zipFiles.Select(o => o.Key).ToList();
        }

        public async Task UploadFileAsync(string key, byte[] fileData)
        {
            using (var stream = new MemoryStream(fileData))
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    InputStream = stream,
                    ContentType = "application/json"
                };
                await _s3Client.PutObjectAsync(putRequest);
            }
        }

        public async Task<byte[]> DownloadFileAsync(string key)
        {
            var response = await _s3Client.GetObjectAsync(_bucketName, key);
            using (var ms = new MemoryStream())
            {
                await response.ResponseStream.CopyToAsync(ms);
                return ms.ToArray();
            }
        }
    }

    public class FileProcessor
    {
        private readonly ILogger<Worker> _logger;
        private readonly EventLog _eventLog;
        private readonly int _maxParallelTasks;
        private readonly int _maxAIMaxStringLength;
        private readonly int _pdfOcrMaxPagesPerFile;
        private readonly int _jsonAttributeFileCaptionMaxLength;
        private readonly string _ai_lambda_url;
        private readonly ApiHelper _ai_apiHelper;

        public FileProcessor(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _maxParallelTasks = configuration.GetValue<int>("ServiceSettings:MaxParallelTasks", 5); // Default to 5 if not set
            _maxAIMaxStringLength = configuration.GetValue<int>("ServiceSettings:AIMaxStringLength", 8000); // Default to 8000 if not set
            _pdfOcrMaxPagesPerFile = configuration.GetValue<int>("ServiceSettings:PdfOcrMaxPagesPerFile", 5); // Default to 5 if not set
            _jsonAttributeFileCaptionMaxLength = configuration.GetValue<int>("ServiceSettings:JsonAttributeFileCaptionMaxLength", 50); // Default to 5 if not set
            _ai_lambda_url = configuration.GetValue<string>("AWS:AiLambdaUrl");
            _ai_apiHelper = new ApiHelper(_ai_lambda_url);
            string eventLogSource = configuration.GetValue<string>("ServiceSettings:EventLogSource", "ModEdmZipAnalyzer");
            _eventLog = new EventLog("Application") { Source = eventLogSource };
        }


        public async Task ProcessZipFiles(List<string> zipFiles, S3Helper s3Helper)
        {
            using (SemaphoreSlim semaphore = new SemaphoreSlim(_maxParallelTasks)) // Limit concurrency
            {
                var zipProcessingTasks = zipFiles.Select(async zipFileName =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        _eventLog.WriteEntry($"Downloading {zipFileName}...", EventLogEntryType.Information);
                        byte[] zipBytes = await s3Helper.DownloadFileAsync(zipFileName);
                        List<MetaFileCaptionInfo> processedFiles = new List<MetaFileCaptionInfo>();

                        using (var zipStream = new MemoryStream(zipBytes))
                        using (var archive = new ZipArchive(zipStream))
                        {
                            // Process all files inside the ZIP in parallel
                            var fileTasks = archive.Entries.Select(entry => ProcessFile(entry.FullName, entry));

                            // Await all file processing tasks and store results
                            processedFiles.AddRange(await Task.WhenAll(fileTasks));
                        }

                        // Metadata creation should happen after all file processing is done
                        await CreateMetaDataFile(zipFileName, processedFiles, s3Helper);
                    }
                    catch (Exception ex)
                    {
                        _eventLog.WriteEntry($"Error processing ZIP {zipFileName}: {ex.Message}", EventLogEntryType.Error);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Wait for all ZIP processing tasks to complete
                await Task.WhenAll(zipProcessingTasks);
            }
        }


        private async Task<MetaFileCaptionInfo> ProcessFile(string fileName, ZipArchiveEntry entry)
        {
            try
            {
                string firstPageBase64String = ""; // TODO

                using (var entryStream = entry.Open())
                using (var ms = new MemoryStream())
                {
                    await entryStream.CopyToAsync(ms);
                    ms.Position = 0;

                    _eventLog.WriteEntry($"Starting OCR Analysis for {fileName}...", EventLogEntryType.Information);
                    
                    string strFileText = await GetTextByOCR(ms, fileName);

                    string strFileCaption = fileName;

                    _eventLog.WriteEntry($"Starting AI Analysis for {fileName}...", EventLogEntryType.Information);
                    strFileCaption = await GetCaptionByAI( strFileText, fileName);

                    // TODO: Add AI analysis for first page
                    //if (strFileCaption == "NOT_FOUND" && !string.IsNullOrEmpty(firstPageBase64String) )
                    //{
                    //    _eventLog.WriteEntry($"Starting AI Analysis based on first page for {fileName}...", EventLogEntryType.Information);
                    //    strFileCaption = (await _ai_apiHelper.GetCaptionByAI(firstPageBase64String, true)).Payload.Result;
                    //}



                    _eventLog.WriteEntry($"File {fileName} processed successfully.", EventLogEntryType.Information);

                    return new MetaFileCaptionInfo { fileName = fileName, fileCaption = strFileCaption };
                }
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error processing file {fileName}: {ex.Message}", EventLogEntryType.Error);
                return new MetaFileCaptionInfo { fileName = fileName, fileCaption = $"Error: {ex.Message}" };
            }
        }
    

        private async Task<string> GetTextByOCR(Stream fileStream, string fileName)
        {
            try
            {
                //_eventLog.WriteEntry("OCR processing started...", EventLogEntryType.Information);

                using (var memoryStream = new MemoryStream())
                {
                    await fileStream.CopyToAsync(memoryStream);
                    string base64String = Convert.ToBase64String(memoryStream.ToArray());

                    bool isPdf = base64String.StartsWith("JVBER");
                    string sText = "";
                    if (isPdf)
                    {
                        sText = await ExtractTextFromPdf(base64String, _pdfOcrMaxPagesPerFile);
                    }
                    else
                    {
                        string tempImagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".jpg");
                        try 
                        { 
                            byte[] byteArray = memoryStream.ToArray();
                            File.WriteAllBytes(tempImagePath, byteArray);
                            sText =  await ExtractTextFromImage(tempImagePath);
                        }
                        finally
                        {
                            if (File.Exists(tempImagePath))
                            {
                                File.Delete(tempImagePath);
                            }
                        }
        }
                    sText = await CleanExtractedText(sText);
                    return sText;
                }
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error in OCR processing: {ex.Message}", EventLogEntryType.Error);
                throw ex;
            }
        }

        private async Task<string> CleanExtractedText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string cleanedText = Regex.Replace(text, "[^\x20-\x7E\u0590-\u05FF]", "");
            cleanedText = Regex.Replace(cleanedText, "[\u200B-\u200F\u202A-\u202E]", "");
            cleanedText = Regex.Replace(cleanedText, "_{2,}", "");
            cleanedText = cleanedText.Replace("", "");
            cleanedText = Regex.Replace(cleanedText, @"\s{2,}", " ");
            cleanedText = cleanedText.Replace("__", "").Trim();
            if (cleanedText.Length < 3)
                return string.Empty;
            return cleanedText;
        }

        

        private async Task<string> ExtractTextFromPdf(string base64String, int maxPages = 5)
        {
            string extractedText = "";
            string tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".pdf");

            try
            {
                byte[] fileBytes = Convert.FromBase64String(base64String);

                // Validate if the file is a real PDF before parsing
                if (!IsValidPdf(fileBytes))
                {
                    _eventLog.WriteEntry("Skipping unsupported or corrupt PDF file.", EventLogEntryType.Warning);
                    return "Unsupported or corrupt PDF file.";
                }

                File.WriteAllBytes(tempFilePath, fileBytes);

                using (PdfDocument pdf = PdfDocument.Open(tempFilePath))
                {
                    int pageCount = Math.Min(pdf.NumberOfPages, maxPages);

                    for (int i = 1; i <= pageCount; i++)
                    {
                        var page = pdf.GetPage(i);
                        string pageText = page.Text;

                        using (MagickImage image = ConvertPdfPageToImage(tempFilePath, i))
                        {
                            ConvertToMonochrome(image);

                            byte[] byteArray = image.ToByteArray(MagickFormat.Jpeg);
                            string tempImagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".jpg");
                            File.WriteAllBytes(tempImagePath, byteArray);

                            try
                            {
                                // Extract text from image using OCR and prepend it
                                string ocrText = await ExtractTextFromImage(tempImagePath) + " ";
                                extractedText = ocrText + extractedText;
                            }
                            finally
                            {
                                if (File.Exists(tempImagePath))
                                {
                                    File.Delete(tempImagePath);
                                }
                            }
                        }

                        // Extract selectable text from PDF and append

                        extractedText += pageText + " ";
                    }
                }
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error extracting text from PDF: {ex.Message}", EventLogEntryType.Error);
                return "Error extracting text from PDF.";
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }

            return extractedText.Trim();
        }




        // Function to Convert PDF Page to Image
        private MagickImage ConvertPdfPageToImage(string pdfFilePath, int pageNumber)
        {
            MagickReadSettings settings = new MagickReadSettings
            {
                Density = new Density(300), // High resolution
                FrameIndex = (uint)pageNumber - 1,
                FrameCount = 1
            };

            return new MagickImage(pdfFilePath, settings);
        }


        private bool IsValidPdf(byte[] fileBytes)
        {
            try
            {
                // Check if the file starts with the PDF magic number "%PDF-"
                string header = Encoding.ASCII.GetString(fileBytes.Take(5).ToArray());
                return header.StartsWith("%PDF-");
            }
            catch
            {
                return false; // File is invalid or unreadable
            }
        }



        private string GetCurrentDllPath()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyLocation = assembly.Location;
            return Path.GetDirectoryName(assemblyLocation);
        }

        private async Task<string> ExtractTextFromImage(string imagePath)
        {
            try
            {
                string extractedText = "";
                string sTessPath = Path.Combine(GetCurrentDllPath(), "tessdata");

                using (var engine = new Tesseract.TesseractEngine(sTessPath, "eng+heb", Tesseract.EngineMode.Default))
                {
                    using (var image = Tesseract.Pix.LoadFromFile(imagePath))
                    {
                        using (var page = engine.Process(image))
                        {
                            extractedText = page.GetText();
                        }
                    }
                }
                return extractedText;
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error extracting text from Image: {ex.Message}", EventLogEntryType.Error);
                return string.Empty;
            }
        }



        // Convert Image to Monochrome (Black & White)
        private void ConvertToMonochrome(MagickImage image)
        {
            image.ColorType = ColorType.Grayscale;  // Convert to Grayscale
            image.Threshold(new Percentage(65));   // Apply Binarization (Adjust 65% as needed)
        }



        private async Task<string> GetCaptionByAI(string text, string fileName)
        {
            try
            {
               // _eventLog.WriteEntry("AI caption generation started...", EventLogEntryType.Information);

                // Ensure length safety
                text = text.Length > _maxAIMaxStringLength
                    ? text.Substring(0, _maxAIMaxStringLength)
                    : text;

                string aiGeneratedCaption = (await _ai_apiHelper.GetCaptionByAI(text)).Payload.Result;

                //_eventLog.WriteEntry("AI caption generation completed.", EventLogEntryType.Information);

                // Ensure length safety
                return aiGeneratedCaption.Length > _jsonAttributeFileCaptionMaxLength
                    ? aiGeneratedCaption.Substring(0, _jsonAttributeFileCaptionMaxLength)
                    : aiGeneratedCaption;
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error in AI caption generation: {ex.Message}", EventLogEntryType.Error);
                return string.Empty;
            }
        }

        private async Task CreateMetaDataFile(string zipFileName, List<MetaFileCaptionInfo> processedFiles, S3Helper s3Helper)
        {
            try
            {
                string jsonFileName = Path.ChangeExtension(zipFileName, ".json");
                MetaFileInfo metaFileInfo = new MetaFileInfo
                {
                    zipFileName = zipFileName,
                    files = processedFiles
                };

                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string jsonContent = JsonSerializer.Serialize(metaFileInfo, options);
                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);

                await s3Helper.UploadFileAsync(jsonFileName, jsonBytes);
                _eventLog.WriteEntry($"Metadata file {jsonFileName} uploaded to S3.", EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                _eventLog.WriteEntry($"Error creating metadata file: {ex.Message}", EventLogEntryType.Error);
            }
        }

    }

    public class MetaFileCaptionInfo
    {
        [JsonPropertyName("fileName")]

        public string fileName { get; set; }

        [JsonPropertyName("fileCaption")]
        public string fileCaption { get; set; }

    }

    public class MetaFileInfo
    {
        [JsonPropertyName("zipFileName")]

        public string zipFileName { get; set; }

        [JsonPropertyName("files")]
        public List<MetaFileCaptionInfo> files { get; set; }

    }
}

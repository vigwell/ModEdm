using Amazon.Lambda.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Assembly attribute for the Lambda function serializer
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ModEdmRunner;

public class Function
{
    private static readonly string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://7olooxrsnrlle72qbvjxmgurue0rwrmi.lambda-url.eu-central-1.on.aws";
    private static readonly bool getOnlyNewZipFiles = bool.TryParse(Environment.GetEnvironmentVariable("GET_ONLY_NEW_ZIP_FILES"), out bool result) ? result : false;
    private static readonly ApiHelper apiHelper = new ApiHelper(baseUrl);
    private const int MaxParallelTasks = 5; // Max parallel file processing tasks

    public async Task Handler()
    {
        try
        {
            LambdaLogger.Log("Searching for new zip files....\n");
            List<string> zipFiles = (await apiHelper.GetOnlyNewZipFilesAsync(getOnlyNewZipFiles)).Payload.Result;

            LambdaLogger.Log($"Found {zipFiles.Count} zip files to process.\n");

            foreach (var zipFileName in zipFiles)
            {
                try
                {
                    LambdaLogger.Log($"Downloading zip file: {zipFileName}\n");
                    string zipBase64String = (await apiHelper.DownloadFileAsync(zipFileName)).Payload.Result;
                    byte[] zipBytes = Convert.FromBase64String(zipBase64String);

                    LambdaLogger.Log($"Extracting contents of {zipFileName}\n");
                    using (MemoryStream zipStream = new MemoryStream(zipBytes))
                    using (ZipArchive archive = new ZipArchive(zipStream))
                    {
                        List<MetaFileCaptionInfo> allResults = new List<MetaFileCaptionInfo>();
                        SemaphoreSlim semaphore = new SemaphoreSlim(MaxParallelTasks);
                        List<Task<MetaFileCaptionInfo>> tasks = new List<Task<MetaFileCaptionInfo>>();

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            await semaphore.WaitAsync();
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    var result = await ProcessFile(entry);
                                    return result;
                                }
                                catch (Exception fileEx)
                                {
                                    LambdaLogger.Log($"Error processing file {entry.FullName}: {fileEx.Message}\n{fileEx.StackTrace}\n");
                                    return new MetaFileCaptionInfo { fileName = entry.FullName, fileCaption = $"Error: {fileEx.Message}" };
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                        }

                        allResults.AddRange(await Task.WhenAll(tasks));

                        // Collect metadata
                        var metaFileInfo = new MetaFileInfo { zipFileName = zipFileName, files = allResults };
                        JsonSerializerOptions options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };

                        string metaFileContent = JsonSerializer.Serialize(metaFileInfo, options);
                        var uploadMetaFileRequest = new UploadMetaFileRequest
                        {
                            fileName = zipFileName,
                            fileContent = metaFileContent
                        };

                        LambdaLogger.Log($"Uploading metadata for {zipFileName}\n");
                        await apiHelper.UploadMetaFileAsync(uploadMetaFileRequest);
                    }
                }
                catch (Exception zipEx)
                {
                    LambdaLogger.Log($"Error processing zip file {zipFileName}: {zipEx.Message}\n{zipEx.StackTrace}\n");
                }
            }
            LambdaLogger.Log("Processing completed successfully.\n");
        }
        catch (Exception ex)
        {
            LambdaLogger.Log($"Error processing zip files: {ex.Message}\n{ex.StackTrace}\n");
        }
    }

    private async Task<MetaFileCaptionInfo> ProcessFile(ZipArchiveEntry entry)
    {
        try
        {
            LambdaLogger.Log($"Processing file: {entry.FullName}\n");

            using (var entryStream = entry.Open())
            using (var reader = new MemoryStream())
            {
                await entryStream.CopyToAsync(reader);
                string base64Content = Convert.ToBase64String(reader.ToArray());

                return await CreateFileMetadataAsync(entry.FullName, base64Content);
            }
        }
        catch (Exception ex)
        {
            LambdaLogger.Log($"Error processing file {entry.FullName}: {ex.Message}\n{ex.StackTrace}\n");
            throw;
        }
    }

    private async Task<MetaFileCaptionInfo> CreateFileMetadataAsync(string fileName, string fileContent)
    {
        try
        {
            LambdaLogger.Log($"Creating metadata for file: {fileName}\n");
            GetFileCaptionRequest getFileCaptionRequest = new GetFileCaptionRequest { fileName = fileName, fileBuffer = fileContent };
            MetaFileCaptionInfo fileCaptionInfo = (await apiHelper.GetFileCaptionAsync(getFileCaptionRequest)).Payload;
            LambdaLogger.Log($"Metadata created for file: {fileName}\n");
            return fileCaptionInfo;
        }
        catch (Exception ex)
        {
            LambdaLogger.Log($"Error creating metadata for file {fileName}: {ex.Message}\n{ex.StackTrace}\n");
            return new MetaFileCaptionInfo { fileName = fileName, fileCaption = $"Error: {ex.Message}" };
        }
    }
}

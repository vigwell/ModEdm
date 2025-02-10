using Amazon.Lambda.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;

// Assembly attribute for the Lambda function serializer
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ModEdmRunner;

public class Function
{
    static readonly string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://7olooxrsnrlle72qbvjxmgurue0rwrmi.lambda-url.eu-central-1.on.aws";
    static readonly bool getOnlyNewZipFiles = bool.TryParse(Environment.GetEnvironmentVariable("GET_ONLY_NEW_ZIP_FILES"), out bool result) ? result : false;
    static ApiHelper apiHelper = new ApiHelper(baseUrl);
    private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(3); // Limit to 3 parallel tasks

    public async Task Handler()
    {
        try
        {
            LambdaLogger.Log("Searching for new zip files....\n");
            List<string> zipFiles = (await apiHelper.GetOnlyNewZipFilesAsync(getOnlyNewZipFiles)).Payload.Result;

            LambdaLogger.Log($"Found {zipFiles.Count} zip files to process.\n");

            foreach (var zipFileName in zipFiles)
            {
                LambdaLogger.Log($"Downloading zip file: {zipFileName}\n");
                string zipBase64String = (await apiHelper.DownloadFileAsync(zipFileName)).Payload.Result;
                byte[] zipBytes = Convert.FromBase64String(zipBase64String);

                LambdaLogger.Log($"Extracting contents of {zipFileName}\n");
                using (MemoryStream zipStream = new MemoryStream(zipBytes))
                using (ZipArchive archive = new ZipArchive(zipStream))
                {
                    List<MetaFileCaptionInfo> allResults = new List<MetaFileCaptionInfo>();
                    List<Task<MetaFileCaptionInfo>> processingTasks = new List<Task<MetaFileCaptionInfo>>();

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        await semaphore.WaitAsync(); // Ensure max 3 parallel tasks

                        var task = ProcessFile(entry)
                            .ContinueWith(t =>
                            {
                                if (t.Status == TaskStatus.RanToCompletion)
                                {
                                    lock (allResults)
                                    {
                                        allResults.Add(t.Result);
                                    }
                                }
                                semaphore.Release();
                                return t.Result; // Ensure the continuation returns the result
                            });

                        processingTasks.Add(task);

                        // Limit active tasks to 3
                        if (processingTasks.Count >= 3)
                        {
                            var completedTask = await Task.WhenAny(processingTasks);
                            processingTasks.Remove(completedTask);
                        }
                    }

                    // Ensure all remaining tasks complete
                    await Task.WhenAll(processingTasks);

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
            LambdaLogger.Log("Processing completed successfully.\n");
        }
        catch (Exception ex)
        {
            LambdaLogger.Log($"Error processing zip files: {ex.Message}\n{ex.StackTrace}\n");
        }
    }

    private async Task<MetaFileCaptionInfo> ProcessFile(ZipArchiveEntry entry)
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

    private async Task<MetaFileCaptionInfo> CreateFileMetadataAsync(string fileName, string fileContent)
    {
        LambdaLogger.Log($"Creating metadata for file: {fileName}\n");
        GetFileCaptionRequest getFileCaptionRequest = new GetFileCaptionRequest { fileName = fileName, fileBuffer = fileContent };
        MetaFileCaptionInfo fileCaptionInfo = (await apiHelper.GetFileCaptionAsync(getFileCaptionRequest)).Payload;
        LambdaLogger.Log($"Metadata created for file: {fileName}\n");
        return fileCaptionInfo;
    }
}

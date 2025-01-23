using Amazon.Lambda.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Encodings.Web;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ModEdmRunner;

public class Function
{
    static readonly string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://7olooxrsnrlle72qbvjxmgurue0rwrmi.lambda-url.eu-central-1.on.aws";
    static readonly bool getOnlyNewZipFiles = bool.TryParse(Environment.GetEnvironmentVariable("GET_ONLY_NEW_ZIP_FILES"), out bool result) ? result : false;
    static ApiHelper apiHelper = new ApiHelper(baseUrl);

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
                    List<Task<MetaFileCaptionInfo>> processingTasks = new List<Task<MetaFileCaptionInfo>>();

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        LambdaLogger.Log($"Processing file: {entry.FullName}\n");
                        using (var entryStream = entry.Open())
                        using (var reader = new MemoryStream())
                        {
                            await entryStream.CopyToAsync(reader);
                            string base64Content = Convert.ToBase64String(reader.ToArray());
                            processingTasks.Add(CreateFileMetadataAsync(entry.FullName, base64Content));
                        }
                    }

                    LambdaLogger.Log($"Waiting for all files in {zipFileName} to be processed.\n");
                    var results = await Task.WhenAll(processingTasks);

                    var metaFileInfo = new MetaFileInfo { zipFileName = zipFileName, files = new List<MetaFileCaptionInfo>(results) };

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

    private async Task<MetaFileCaptionInfo> CreateFileMetadataAsync(string fileName, string fileContent)
    {
        LambdaLogger.Log($"Creating metadata for file: {fileName}\n");
        GetFileCaptionRequest getFileCaptionRequest = new GetFileCaptionRequest { fileName = fileName, fileBuffer = fileContent };
        MetaFileCaptionInfo fileCaptionInfo = (await apiHelper.GetFileCaptionAsync(getFileCaptionRequest)).Payload;
        LambdaLogger.Log($"Metadata created for file: {fileName}\n");
        return fileCaptionInfo;
    }
}

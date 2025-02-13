using RestSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

// ApiHelper
public class ApiHelper
{
    private readonly RestClient _client;

    public ApiHelper(string baseUrl)
    {
        var options = new RestClientOptions(baseUrl) {  Timeout = TimeSpan.FromMinutes(10) };
        _client = new RestClient(options);
    }

    public async Task<GetOnlyNewZipFilesResponse> GetOnlyNewZipFilesAsync(bool getOnlyNewZipFiles)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        var apiRequest = new GetOnlyNewZipFilesRequest
        {
            GetOnlyNewZipFiles = getOnlyNewZipFiles
        };

        var jsonBody = JsonSerializer.Serialize(apiRequest);
        request.AddStringBody(jsonBody, DataFormat.Json);

        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
        {
            return JsonSerializer.Deserialize<GetOnlyNewZipFilesResponse>(response.Content);
        }
        else
        {
            throw new Exception($"API call failed: {response.ErrorMessage}");
        }
    }

    public async Task<DownloadFileResponse> DownloadFileAsync(string fileName)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        var apiRequest = new DownloadFileRequest
        {
            FileName = fileName
        };

        var jsonBody = JsonSerializer.Serialize(apiRequest);
        request.AddStringBody(jsonBody, DataFormat.Json);

        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
        {
            return JsonSerializer.Deserialize<DownloadFileResponse>(response.Content);
        }
        else
        {
            throw new Exception($"API call failed: {response.ErrorMessage}");
        }
    }

    public async Task<UploadMetaFileResponse> UploadMetaFileAsync(UploadMetaFileRequest uploadMetaFileRequest)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        var jsonBody = JsonSerializer.Serialize(uploadMetaFileRequest);
        request.AddStringBody(jsonBody, DataFormat.Json);

        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
        {
            return JsonSerializer.Deserialize<UploadMetaFileResponse>(response.Content);
        }
        else
        {
            throw new Exception($"API call failed: {response.ErrorMessage}");
        }
    }

    public async Task<GetFileCaptionResponse> GetFileCaptionAsync(GetFileCaptionRequest getFileCaptionRequest)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        var jsonBody = JsonSerializer.Serialize(getFileCaptionRequest);
        request.AddStringBody(jsonBody, DataFormat.Json);
        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
        {
            return JsonSerializer.Deserialize<GetFileCaptionResponse>(response.Content);
        }
        else
        {
            throw new Exception($"API call failed: {response.ErrorMessage}");
        }
    }
}

public class GetFileCaptionResponse
{

    // Properties
    [JsonPropertyName("payload")]
    public MetaFileCaptionInfo Payload { get; set; }

    [JsonPropertyName("success")]
    public bool success { get; set; }
}


public class ApiRequestBase
{
    [JsonPropertyName("action")]
    public string Action { get; set; }
}

public class GetOnlyNewZipFilesRequest : ApiRequestBase
{
    [JsonPropertyName("getOnlyNewZipFiles")]
    public bool GetOnlyNewZipFiles { get; set; }

    public GetOnlyNewZipFilesRequest()
    {
        this.Action = "getZipFilesToProceed";
    }
}

public class UploadMetaFileRequest : ApiRequestBase
{
    [JsonPropertyName("fileContent")]
    public string fileContent { get; set; }

    [JsonPropertyName("fileName")]
    public string fileName { get; set; }

    public UploadMetaFileRequest()
    {
        this.Action = "uploadMetaFile";
    }
}

public class UploadMetaFileResponse : ApiResponse<string>
{

}

public class DownloadFileRequest : ApiRequestBase
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; }

    public DownloadFileRequest()
    {
        this.Action = "downloadFile";
    }
}


public class ApiResponse<T>
{
    [JsonPropertyName("payload")]
    public ApiResult<T> Payload { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public class ApiResult<T>
{
    [JsonPropertyName("result")]
    public T Result { get; set; }
}

public class DownloadFileResponse : ApiResponse<string>
{
}

public class GetOnlyNewZipFilesResponse : ApiResponse<List<string>>
{
}


public class MetaFileCaptionInfo
{
    [JsonPropertyName("fileName")]

    public string fileName { get; set; }

    [JsonPropertyName("fileCaption")]
    public string fileCaption { get; set; }

}

public class GetFileCaptionRequest : ApiRequestBase
{
    // Properties
    [JsonPropertyName("fileName")]
    public string fileName { get; set; }
    [JsonPropertyName("fileBuffer")]
    public string fileBuffer { get; set; }

    public GetFileCaptionRequest()
    {
        this.Action = "getFileCaption";
    }
}


public class MetaFileInfo 
{
    [JsonPropertyName("zipFileName")]

    public string zipFileName { get; set; }

    [JsonPropertyName("files")]
    public  List<MetaFileCaptionInfo> files { get; set; }

}
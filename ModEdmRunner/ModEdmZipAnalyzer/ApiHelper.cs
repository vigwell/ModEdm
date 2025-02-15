using ModEdmZipAnalyzer;
using RestSharp;
using System.Text.Encodings.Web;
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

    public async Task<AnalyzeAIResponse> GetCaptionByAI(string content, bool isImage = false)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        object apiRequest;
    
        if (isImage)
        {
            apiRequest = new AnalyzeImageRequest
            {
                base64String = content
            };
        }
        else
        {
            apiRequest = new AnalyzeTextRequest
            {
                inputText = content
            };
        }

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var jsonBody = JsonSerializer.Serialize(apiRequest, options);
        request.AddStringBody(jsonBody, DataFormat.Json);

        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            return JsonSerializer.Deserialize<AnalyzeAIResponse>(response.Content);
        else
            throw new Exception($"API call failed: {response.ErrorMessage}");
    }
}


public class ApiRequestBase
{
    [JsonPropertyName("action")]
    public string Action { get; set; }
}



public class AnalyzeTextRequest : ApiRequestBase
{
    [JsonPropertyName("inputText")]
    public string inputText { get; set; }

    public AnalyzeTextRequest()
    {
        this.Action = "analyzeText";
    }
}

public class AnalyzeImageRequest : ApiRequestBase
{
    [JsonPropertyName("base64String")]
    public string base64String { get; set; }

    public AnalyzeImageRequest()
    {
        this.Action = "analyzeImage";
    }
}

public class AnalyzeAIResponse : ApiResponse<string>
{

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


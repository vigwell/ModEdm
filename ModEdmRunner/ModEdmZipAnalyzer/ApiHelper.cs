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

    public async Task<AnalyzeTextResponse> GetCaptionByAI(string text)
    {
        var request = new RestRequest("", Method.Post);
        request.AddHeader("Content-Type", "application/json");

        var apiRequest = new AnalyzeTextRequest
        {
            inputText = text
        };

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var jsonBody = JsonSerializer.Serialize(apiRequest, options);
        request.AddStringBody(jsonBody, DataFormat.Json);

        RestResponse response = await _client.ExecuteAsync(request);

        if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            return JsonSerializer.Deserialize<AnalyzeTextResponse>(response.Content);
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

public class AnalyzeTextResponse : ApiResponse<string>
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


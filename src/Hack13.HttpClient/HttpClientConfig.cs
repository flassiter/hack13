namespace Hack13.HttpClient;

internal sealed class HttpClientConfig
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public bool AllowPrivateNetwork { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public List<int>? SuccessStatusCodes { get; set; }
    public Dictionary<string, string>? ResponseFieldMap { get; set; }
    public string ResponseBodyKey { get; set; } = "http_response_body";
}

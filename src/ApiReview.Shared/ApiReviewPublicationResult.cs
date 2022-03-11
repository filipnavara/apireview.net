namespace ApiReviewDotNet.Data;

public sealed class ApiReviewPublicationResult
{
    public static ApiReviewPublicationResult Failed() => new(success: false, url: null);
    public static ApiReviewPublicationResult Suceess(string url) => new(success: true, url);

    private ApiReviewPublicationResult(bool success, string? url)
    {
        Success = success;
        Url = url;
    }

    public bool Success { get; }
    public string? Url { get; }
}

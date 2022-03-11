using System.Text.Json;

using ApiReviewDotNet.Data;

namespace RepoIndexer;

internal static class MyJsonSerializerOptions
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        WriteIndented = true,
        Converters =
        {
            new TimeSpanJsonConverter()
        }
    };
}

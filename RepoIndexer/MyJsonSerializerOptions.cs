using System.Text.Json;

using ApiReview.Shared;

namespace RepoIndexer
{
    internal static class MyJsonSerializerOptions
    {
        public static readonly JsonSerializerOptions Instance = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new TimeSpanJsonConverter()
            }
        };
    }
}

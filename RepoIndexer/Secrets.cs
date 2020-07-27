using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Configuration.UserSecrets;

namespace RepoIndexer
{
    internal sealed class Secrets
    {
        public string GitHubKey { get; set; }
        public string YouTubeKey { get; set; }

        public static Secrets Load()
        {
            var secretsPath = PathHelper.GetSecretsPathFromSecretsId("RepoIndexer");
            var secretsJson = File.ReadAllText(secretsPath);
            return JsonSerializer.Deserialize<Secrets>(secretsJson)!;
        }
    }
}

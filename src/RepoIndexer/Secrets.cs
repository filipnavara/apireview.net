using System.Text.Json;

using Microsoft.Extensions.Configuration.UserSecrets;

namespace RepoIndexer;

internal sealed class Secrets
{
    public Secrets(string gitHubKey, string youTubeKey)
    {
        GitHubKey = gitHubKey;
        YouTubeKey = youTubeKey;
    }

    public string GitHubKey { get; }
    public string YouTubeKey { get; }

    public static Secrets Load()
    {
        var secretsPath = PathHelper.GetSecretsPathFromSecretsId("RepoIndexer");
        var secretsJson = File.ReadAllText(secretsPath);
        return JsonSerializer.Deserialize<Secrets>(secretsJson)!;
    }
}

namespace ApiReviewDotNet.Data;

public sealed class ApiReviewer
{
    public ApiReviewer(string gitHubUserName,
                       string name,
                       string email)
    {
        GitHubUserName = gitHubUserName;
        Name = name;
        Email = email;
    }

    public string GitHubUserName { get; }
    public string Name { get; }
    public string Email { get; }
}

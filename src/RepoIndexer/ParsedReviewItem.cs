using ApiReviewDotNet.Data;

namespace RepoIndexer;

internal sealed class ParsedReviewItem
{
    public ParsedReviewItem(ApiReviewDecision decision,
                            string issueUrl,
                            string owner,
                            string repo,
                            int issueId,
                            string commentId,
                            string? videoUrl,
                            string? videoId,
                            TimeSpan timecode,
                            string descriptionMarkdown,
                            IEnumerable<string> apis)
    {
        Decision = decision;
        IssueUrl = issueUrl;
        Owner = owner;
        Repo = repo;
        IssueId = issueId;
        CommentId = commentId;
        VideoUrl = videoUrl;
        VideoId = videoId;
        Timecode = timecode;
        DescriptionMarkdown = descriptionMarkdown;
        Apis = apis.ToArray();
    }

    public ApiReviewDecision Decision { get; }
    public string IssueUrl { get; }
    public string Owner { get; }
    public string Repo { get; }
    public int IssueId { get; }
    public string CommentId { get; }
    public string? VideoUrl { get; }
    public string? VideoId { get; }
    public TimeSpan Timecode { get; }
    public string DescriptionMarkdown { get; }
    public IReadOnlyList<string> Apis { get; }
}

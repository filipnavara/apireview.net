namespace ApiReviewDotNet.Data;

public sealed class ApiReviewIssue : IComparable<ApiReviewIssue>
{
    public ApiReviewIssue(string owner,
                          string repo,
                          int id,
                          string title,
                          string author,
                          IReadOnlyList<string> assignees,
                          string? markedReadyForReviewBy,
                          IReadOnlyList<string> areaOwners,
                          DateTimeOffset createdAt,
                          string url,
                          string milestone,
                          IReadOnlyList<ApiReviewLabel> labels,
                          IReadOnlyList<ApiReviewer> reviewers)
    {
        Owner = owner;
        Repo = repo;
        Id = id;
        Title = title;
        Author = author;
        Assignees = assignees;
        MarkedReadyForReviewBy = markedReadyForReviewBy;
        AreaOwners = areaOwners;
        CreatedAt = createdAt;
        Url = url;
        Milestone = milestone;
        Labels = labels;
        Reviewers = reviewers;
    }

    public string Owner { get; }

    public string Repo { get; }

    public string RepoFull => $"{Owner}/{Repo}";

    public int Id { get; }

    public string IdFull => $"{Owner}/{Repo}#{Id}";

    public string Title { get; }

    public string Author { get; }

    public IReadOnlyList<string> Assignees { get; }

    public string? MarkedReadyForReviewBy { get; }

    public IReadOnlyList<string> AreaOwners { get; }

    public DateTimeOffset CreatedAt { get; }

    public string DetailText => $"{IdFull} {CreatedAt.FormatRelative()} by {Author}";

    public string Url { get; }

    public string Milestone { get; }

    public IReadOnlyList<ApiReviewLabel> Labels { get; }

    public bool IsBlocking => Labels is not null && Labels.Any(l => string.Equals(l.Name, ApiReviewConstants.Blocking, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ApiReviewer> Reviewers { get; }

    public int CompareTo(ApiReviewIssue? other)
    {
        if (other is null)
            return -1;

        var result = -IsBlocking.CompareTo(other.IsBlocking);
        if (result != 0)
            return result;

        result = CompareMilestone(Milestone, other.Milestone);
        if (result != 0)
            return result;

        return CreatedAt.CompareTo(other.CreatedAt);
    }

    private static int CompareMilestone(string x, string y)
    {
        // The desired sort order is:
        //
        // 1. Milestones that look like versions, sorted by version number
        // 2. Milestones that aren't versions, sorted by text (e.g. "Backlog", "Future")
        // 3. No milestone
        //
        // Why does no milestone go last? Because we don't to punish folks for triaging milestones.

        static bool IsNone(string m)
        {
            return string.IsNullOrEmpty(m) || m == ApiReviewConstants.NoMilestone;
        }

        if (IsNone(x) && IsNone(y))
            return 0;

        if (IsNone(x))
            return 1;

        if (IsNone(y))
            return -1;

        var xIsVersion = Version.TryParse(x, out var xVersion);
        var yIsVersion = Version.TryParse(y, out var yVersion);

        var result = -xIsVersion.CompareTo(yIsVersion);
        if (result != 0)
            return result;

        if (xIsVersion && yIsVersion)
            return xVersion!.CompareTo(yVersion);

        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}

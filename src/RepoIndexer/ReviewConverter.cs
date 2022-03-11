using ApiReviewDotNet.Data;

namespace RepoIndexer;

internal static class ReviewConverter
{
    public static async Task<IReadOnlyList<ApiReviewSummary>> ConvertAsync(IReadOnlyList<ParsedReviewSummary> reviews,
                                                                                YouTubeVideoCache videoCache,
                                                                                GitHubIssueCache issueCache)
    {
        var result = new List<ApiReviewSummary>();

        foreach (var review in reviews)
        {
            var videoIds = review.Items.Select(i => i.VideoId).Distinct().ToArray();
            //if (videoIds.Length > 1)
            //    Console.Error.WriteLine($"warning: review {review.Date} has multiple videos. Picking first.");

            var videoId = videoIds.FirstOrDefault();
            var video = await videoCache.LoadVideoAsync(videoId);
            var items = await ConvertItems(issueCache, review, review.Date);
            var title = ComputeTitle(items);

            if (!items.Any())
            {
                Console.WriteLine($"warning: review {review.Path} has no items. Skipping.");
                continue;
            }

            var summary = new ApiReviewSummary
            (
                repositoryGroup: string.Empty,
                video: video,
                items: items
            );

            result.Add(summary);
        }

        return result;
    }

    private static async Task<IReadOnlyList<ApiReviewItem>> ConvertItems(GitHubIssueCache issueCache, ParsedReviewSummary review, DateTimeOffset reviewDate)
    {
        var result = new List<ApiReviewItem>();

        foreach (var item in review.Items)
        {
            if (string.IsNullOrEmpty(item.Owner) ||
                string.IsNullOrEmpty(item.Repo) ||
                item.IssueId == 0)
            {
                Console.WriteLine($"warning: review {review.Path} doesn't refer to an issue. Skipping.");
                continue;
            }

            var issue = await issueCache.GetIssue(item.Owner, item.Repo, item.IssueId);

            var summaryItem = new ApiReviewItem
            (
                issue: issue,
                decision: item.Decision,
                feedbackId: item.CommentId,
                feedbackAuthor: "terrajobst", // TODO: Load from comment
                feedbackDateTime: reviewDate, // TODO: Load from comment
                feedbackMarkdown: item.DescriptionMarkdown, // TODO: Fallback to comment
                feedbackUrl: $"https://github.com/{item.Owner}/{item.Repo}/issues/{item.IssueId}#issuecomment-{item.CommentId}",
                apis: item.Apis.ToArray()
            );

            summaryItem.TimeCode = item.Timecode;

            result.Add(summaryItem);
        }

        return result.ToArray();
    }

    private static string ComputeTitle(IEnumerable<ApiReviewItem> items)
    {
        var areaLabels = new SortedSet<string>();
        var prefix = "area-";

        foreach (var item in items)
        {
            foreach (var label in item.Issue.Labels)
            {
                if (label.Name.StartsWith(prefix))
                {
                    areaLabels.Add(label.Name.Substring(prefix.Length));
                }
            }
        }

        if (areaLabels.Count == 0)
            return "GitHub Quick Reviews";

        return string.Join(", ", areaLabels);
    }
}

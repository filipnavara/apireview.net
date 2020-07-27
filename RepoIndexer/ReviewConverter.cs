using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ApiReview.Shared;

namespace RepoIndexer
{
    internal static class ReviewConverter
    {
        public static async Task<IReadOnlyList<ApiReviewSummary>> ConvertAsync(IReadOnlyList<Review> reviews,
                                                                               IReadOnlyList<ApiReviewVideo> videoCache,
                                                                               GitHubIssueCache issueCache)
        {
            var videoById = videoCache.ToDictionary(v => v.Id);
            var result = new List<ApiReviewSummary>();

            foreach (var review in reviews)
            {
                var videoIds = review.Items.Select(i => i.VideoId).Distinct().ToArray();
                if (videoIds.Length > 1)
                    Console.Error.WriteLine($"warning: review {review.Date} has multiple videos. Picking first.");

                var videoId = videoIds.FirstOrDefault();
                ApiReviewVideo? video = null;

                if (videoId != null)
                {
                    if (!videoById.TryGetValue(videoId, out video))
                    {
                        Console.Error.WriteLine($"warning: video {videoId} not found in cache.");
                    }
                }

                var summary = new ApiReviewSummary
                {
                    Id = result.Count + 1,
                    Date = review.Date,
                    Video = video
                };

                var resultItems = new List<ApiReviewItem>();

                foreach (var item in review.Items)
                {
                    if (string.IsNullOrEmpty(item.Owner) ||
                        string.IsNullOrEmpty(item.Repo) ||
                        item.IssueId == 0)
                    {
                        Console.WriteLine($"warning: review {review.Date} doesn't refer to an issue. Skipping.");
                        continue;
                    }

                    var issue = await issueCache.GetIssue(item.Owner, item.Repo, item.IssueId);

                    var summaryItem = new ApiReviewItem
                    {
                        Id = resultItems.Count + 1,
                        Issue = issue,
                        Decision = ConvertDecision(item.Status),
                        FeedbackId = item.CommentId,
                        FeedbackAuthor = "terrajobst", // TODO: Load from comment
                        FeedbackDateTime = summary.Date, // TODO: Load from comment
                        FeedbackMarkdown = item.DescriptionMarkdown, // TODO: Fallback to comment
                        FeedbackUrl = $"https://github.com/{item.Owner}/{item.Repo}/issues/{item.IssueId}#issuecomment-{item.CommentId}",
                        TimeCode = item.Timecode,
                        Apis = item.Apis.ToArray()
                    };

                    resultItems.Add(summaryItem);
                }

                summary.Items = resultItems.ToArray();

                if (summary.Items.Any())
                    result.Add(summary);
                else
                    Console.WriteLine($"warning: review {review.Date} has no items. Skipping.");

                summary.Title = ComputeTitle(summary);
            }

            return result;
        }

        private static ApiReviewDecisionKind ConvertDecision(string status)
        {
            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
                return ApiReviewDecisionKind.Approved;

            if (string.Equals(status, "Needs Work", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "NeedsWork", StringComparison.OrdinalIgnoreCase))
                return ApiReviewDecisionKind.NeedsWork;

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
                return ApiReviewDecisionKind.Rejected;

            Console.WriteLine($"warning: Unknown status '{status}'");
            return ApiReviewDecisionKind.Rejected;
        }

        private static string ComputeTitle(ApiReviewSummary summary)
        {
            var areaLabels = new SortedSet<string>();
            var prefix = "area-";

            foreach (var item in summary.Items)
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
}

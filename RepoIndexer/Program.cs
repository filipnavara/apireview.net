using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

namespace RepoIndexer
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            // TODO: Download issue details from GitHub
            // TODO: Use ApiReviewIssue, ApiReviewFeedback, and ApiReviewSummary
            // TODO: Index APIs in diffs
            // TODO: Download video information of entire playlist
            // TODO: Allow non-quick-review based Markdown files to be indexed

            var dataFolder = GetDataFolder();
            var inputFolderPath = @"P:\oss\apireviews";
            var inputIssuesPath = Path.Combine(dataFolder, "issues.json");
            var inputVideosPath = Path.Combine(dataFolder, "videos.json");
            var outputPath = Path.Combine(dataFolder, "reviews.json");

            var secrets = Secrets.Load();

            var videoCache = await YouTubeVideoCache.LoadAsync(inputVideosPath, secrets.YouTubeKey);
            var issueCache = new GitHubIssueCache(inputIssuesPath, secrets.GitHubKey);
            var reviews = ReviewIndexer.Index(inputFolderPath);

            PrintStats(reviews);

            var summaries = await ReviewConverter.ConvertAsync(reviews, videoCache, issueCache);

            var json = JsonSerializer.Serialize(summaries, MyJsonSerializerOptions.Instance);
            File.WriteAllText(outputPath, json);
        }

        private static string GetDataFolder([CallerFilePath] string? callerFileName = null)
        {
            Debug.Assert(callerFileName != null);

            var callerFolder = Path.GetDirectoryName(callerFileName);
            Debug.Assert(callerFolder != null);

            return Path.Combine(callerFolder, "Data");
        }

        private static void PrintStats(IReadOnlyList<Review> reviews)
        {
            var numberOfRepos = reviews.SelectMany(r => r.Items)
                                       .Select(i => i.Owner + "/" + i.Repo)
                                       .Distinct()
                                       .Count();

            var numberOfIssues = reviews.SelectMany(r => r.Items)
                                        .Where(i => i.IssueId > 0)
                                        .Select(i => i.Owner + "/" + i.Repo + "#" + i.IssueId)
                                        .Distinct()
                                        .Count();

            var numberOfReviews = reviews.Count;
            var numberOfReviewItems = reviews.Sum(r => r.Items.Count);
            var avgItemsPerReview = reviews.Average(r => r.Items.Count);
            var duration = reviews.Max(r => r.Date) - reviews.Min(r => r.Date);
            var durationYears = duration.TotalDays / 365;
            var workHours = numberOfReviews * 2;

            var numberOfApis = reviews.SelectMany(r => r.Items).SelectMany(i => i.Apis).ToHashSet().Count;

            Console.WriteLine();
            Console.WriteLine($"   Number of repos        : {numberOfRepos:N0}");
            Console.WriteLine($"   Number of issues       : {numberOfIssues:N0}");
            Console.WriteLine($"   Number of reviews      : {numberOfReviews:N0}");
            Console.WriteLine($"   Number of review items : {numberOfReviewItems:N0}");
            Console.WriteLine($"   Avg items per review   : {avgItemsPerReview:F1}");
            Console.WriteLine($"   Number of APIs         : {numberOfApis:N0}");
            Console.WriteLine($"   Doing reviews for      : {durationYears:F1} years");
            Console.WriteLine($"   Time spent in reviews  : {workHours} hours");
            Console.WriteLine();
        }
    }
}

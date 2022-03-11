using System.Text.RegularExpressions;

using ApiReviewDotNet.Data;

using Markdig.Parsers;
using Markdig.Syntax;

using Microsoft.CodeAnalysis.CSharp;

namespace RepoIndexer;

internal static class ReviewIndexer
{
    // language=regex
    private const string FileRegex = @"(?<year>[0-9]{4})\\(?<month>[0-9]{1,2})-(?<day>[0-9]{1,2})-quick-reviews\\README.md";

    // language=regex
    private const string StatusRegex = @"^\*\*(?<status>[^*]+)\*\* \| \[[^]]+\]\((?<issueUrl>[^)]*)\)( \| \[[^]]+\]\((?<videoUrl>[^)]*)\))?";

    // language=regex
    private const string GitHubUrlRegex = @"https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/(issues|pull)/(?<issueId>[0-9]+)(#issuecomment-(?<commentId>[0-9]+))?";

    // language=regex
    private const string YouTubeUrlRegex = @"https://www\.youtube\.com/watch\?v=(?<videoId>[^&]+)(&t=(?<hours>[0-9]+)h(?<minutes>[0-9]+)m(?<seconds>[0-9]+)s)?";

    // language=regex
    private const string YouTubeShortUrlRegex = @"https://youtu\.be/(?<videoId>[^?]+)(\?t=(?<seconds>[0-9]+)s?)?";

    public static IReadOnlyList<ParsedReviewSummary> Index(string path)
    {
        var files = Directory.GetFiles(path, "*.md", SearchOption.AllDirectories);
        var reviews = new List<ParsedReviewSummary>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(path, file);
            var fileMatch = Regex.Match(relativePath, FileRegex);
            if (!fileMatch.Success)
                continue;

            //if (file == @"P:\apireviews\2020\03-17-quick-reviews\README.md")
            //    System.Diagnostics.Debugger.Break();

            var year = int.Parse(fileMatch.Groups["year"].Value);
            var month = int.Parse(fileMatch.Groups["month"].Value);
            var day = int.Parse(fileMatch.Groups["day"].Value);

            var lines = File.ReadAllLines(file).ToList();

            // Remove 2nd level headers

            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].StartsWith("## "))
                    lines.RemoveAt(i);
            }

            var itemRanges = ParseItemRanges(lines);
            var items = ParseItems(lines, itemRanges);

            var review = new ParsedReviewSummary(file, new DateTime(year, month, day), items);
            reviews.Add(review);
        }

        return reviews.ToArray();
    }

    private static IEnumerable<(int Start, int Count)> ParseItemRanges(IReadOnlyList<string> lines)
    {
        var itemStart = -1;
        var current = 0;

        while (current < lines.Count - 1)
        {
            if (Regex.IsMatch(lines[current], StatusRegex))
            {
                if (itemStart >= 0)
                {
                    var count = current - itemStart;
                    if (count > 0)
                        yield return (itemStart, count);
                }

                itemStart = current;
            }

            current++;
        }

        if (itemStart < current)
        {
            var count = current - itemStart + 1;
            if (count > 0)
                yield return (itemStart, count);
        }
    }

    private static IReadOnlyList<ParsedReviewItem> ParseItems(IReadOnlyList<string> lines, IEnumerable<(int Start, int Count)> itemRanges)
    {
        var result = new List<ParsedReviewItem>();

        foreach (var (start, count) in itemRanges)
        {
            var itemLines = lines.Skip(start)
                                 .Take(count)
                                 .ToArray();

            var reviewItem = ParseItem(itemLines);

            if (reviewItem is not null)
                result.Add(reviewItem);
        }

        return result.ToArray();
    }

    private static ParsedReviewItem? ParseItem(IReadOnlyList<string> lines)
    {
        var header = lines[0];
        var headerInformation = ParseHeader(header);
        if (headerInformation is null)
            return null;

        var (status, issueUrl, videoUrl) = headerInformation.Value;
        var decision = ParseDecision(status);
        
        var issueInformation = ParseIssueUrl(issueUrl);
        if (issueInformation is null)
            return null;

        var (owner, repo, issueId, commentId) = issueInformation.Value;

        var videoInformation = ParseVideoUrl(videoUrl);
        var (videoId, timecode) = videoInformation is null 
                                    ? (null, TimeSpan.Zero)
                                    : videoInformation.Value;

        var descriptionMarkdown = string.Join(Environment.NewLine, lines.Skip(1)).Trim();
        var apis = ParseApis(descriptionMarkdown);

        return new ParsedReviewItem
        (
            decision: decision,
            owner: owner,
            repo: repo,
            issueId: issueId,
            commentId: commentId,
            issueUrl: issueUrl,
            videoId: videoId,
            timecode: timecode,
            videoUrl: videoUrl,
            descriptionMarkdown: descriptionMarkdown,
            apis: apis
        );
    }

    private static (string Status, string IssueUrl, string VideoUrl)? ParseHeader(string header)
    {
        var statusMatch = Regex.Match(header, StatusRegex);
        if (!statusMatch.Success)
            return null;

        var status = statusMatch.Groups["status"].Value;
        var issueUrl = statusMatch.Groups["issueUrl"].Value;
        var videoUrl = statusMatch.Groups["videoUrl"].Value;

        return (status, issueUrl, videoUrl);
    }

    private static ApiReviewDecision ParseDecision(string status)
    {
        if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            return ApiReviewDecision.Approved;

        if (string.Equals(status, "Needs Work", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "NeedsWork", StringComparison.OrdinalIgnoreCase))
            return ApiReviewDecision.NeedsWork;

        if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            return ApiReviewDecision.Rejected;

        Console.WriteLine($"warning: Unknown status '{status}'");
        return ApiReviewDecision.Rejected;
    }

    private static (string Owner, string Repo, int IssueId, string CommentId)? ParseIssueUrl(string issueUrl)
    {
        var gitHubUrlMatch = Regex.Match(issueUrl, GitHubUrlRegex);
        if (!gitHubUrlMatch.Success)
            return null;

        var owner = gitHubUrlMatch.Groups["owner"].Value;
        var repo = gitHubUrlMatch.Groups["repo"].Value;
        var issueId = int.Parse(gitHubUrlMatch.Groups["issueId"].Value);
        var commentId = gitHubUrlMatch.Groups["commentId"].Value;

        return (owner, repo, issueId, commentId);
    }

    private static (string VideoId, TimeSpan TimeCode)? ParseVideoUrl(string videoUrl)
    {
        var youTubeUrlMatch = Regex.Match(videoUrl, YouTubeUrlRegex);
        if (youTubeUrlMatch.Success)
        {
            var videoId = youTubeUrlMatch.Groups["videoId"].Value;
            var hoursText = youTubeUrlMatch.Groups["hours"].Value;
            var minutesText = youTubeUrlMatch.Groups["minutes"].Value;
            var secondsText = youTubeUrlMatch.Groups["seconds"].Value;

            var hours = string.IsNullOrEmpty(hoursText)
                            ? 0
                            : int.Parse(hoursText);
            var minutes = string.IsNullOrEmpty(minutesText)
                            ? 0
                            : int.Parse(minutesText);
            var seconds = string.IsNullOrEmpty(secondsText)
                            ? 0
                            : int.Parse(secondsText);

            var timecode = new TimeSpan(hours, minutes, seconds);
            return (videoId, timecode);
        }

        var youTubeShortUrlMatch = Regex.Match(videoUrl, YouTubeShortUrlRegex);
        if (youTubeShortUrlMatch.Success)
        {
            var videoId = youTubeShortUrlMatch.Groups["videoId"].Value;
            var secondsText = youTubeShortUrlMatch.Groups["seconds"].Value;

            var seconds = string.IsNullOrEmpty(secondsText)
                            ? 0
                            : int.Parse(secondsText);

            var timecode = TimeSpan.FromSeconds(seconds);
            return (videoId, timecode);
        }

        return null;
    }

    private static IReadOnlyList<string> ParseApis(string markdown)
    {
        var document = MarkdownParser.Parse(markdown);
        var codeBlocks = document.Descendants<FencedCodeBlock>();

        var declarations = new SortedSet<string>();

        foreach (var codeBlock in codeBlocks)
        {
            var lang = codeBlock.Info;
            if (string.IsNullOrEmpty(lang))
                continue;

            // TODO: Handle diff
            var isCSharp = string.Equals(lang, "cs", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(lang, "C#", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(lang, "CSharp", StringComparison.OrdinalIgnoreCase);

            if (isCSharp)
            {
                var lines = codeBlock.Lines.Lines.Select(l => l.ToString());
                var code = string.Join(Environment.NewLine, lines);
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var walker = new DeclarationRecordingWalker();
                walker.Visit(syntaxTree.GetRoot());
                declarations.UnionWith(walker.Declarations);
            }
        }

        return declarations.ToArray();
    }
}

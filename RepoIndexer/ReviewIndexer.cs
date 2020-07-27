using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Markdig.Parsers;
using Markdig.Syntax;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RepoIndexer
{
    internal static class ReviewIndexer
    {
        public static IReadOnlyList<Review> Index(string path)
        {
            // language=regex
            var fileRegex = @"(?<year>[0-9]{4})\\(?<month>[0-9]{1,2})-(?<day>[0-9]{1,2})-quick-reviews\\README.md";

            // language=regex
            var statusRegex = @"^\*\*(?<status>[^*]+)\*\* \| \[[^]]+\]\((?<issueUrl>[^)]*)\)( \| \[[^]]+\]\((?<videoUrl>[^)]*)\))?";

            // language=regex
            var headerRegex = @"^##? .*$";

            // language=regex
            var gitHubUrlRegex = @"https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/issues/(?<issueId>[0-9]+)(#issuecomment-(?<commentId>[0-9]+))?";

            // language=regex
            var youTubeUrlRegex = @"https://www\.youtube\.com/watch\?v=(?<videoId>[^&]+)(&t=(?<hours>[0-9]+)h(?<minutes>[0-9]+)m(?<seconds>[0-9]+)s)?";

            var files = Directory.GetFiles(path, "*.md", SearchOption.AllDirectories);

            var reviews = new List<Review>();
            var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(path, file);
                var fileMatch = Regex.Match(relativePath, fileRegex);
                if (!fileMatch.Success)
                    continue;

                var year = int.Parse(fileMatch.Groups["year"].Value);
                var month = int.Parse(fileMatch.Groups["month"].Value);
                var day = int.Parse(fileMatch.Groups["day"].Value);

                var lines = File.ReadAllLines(file).ToList();

                // Remove headers

                for (var i = lines.Count - 1; i >= 0; i--)
                {
                    if (Regex.IsMatch(lines[i], headerRegex))
                        lines.RemoveAt(i);
                }

                // Search for status headers, description Markdown is in between

                var review = new Review
                {
                    Date = new DateTime(year, month, day)
                };
                reviews.Add(review);

                var descriptionBuilder = new StringBuilder();

                void SetDescription()
                {
                    if (review.Items.Any())
                    {
                        var lastItem = review.Items.Last();
                        lastItem.DescriptionMarkdown = descriptionBuilder.ToString().Trim();
                        descriptionBuilder.Clear();

                        var document = MarkdownParser.Parse(lastItem.DescriptionMarkdown);
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
                                languages.Add("C#");
                            else
                                languages.Add(lang);

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

                        lastItem.Apis.AddRange(declarations);
                    }
                }

                foreach (var line in lines)
                {
                    var statusMatch = Regex.Match(line, statusRegex);
                    if (statusMatch.Success)
                    {
                        SetDescription();

                        var status = statusMatch.Groups["status"].Value;
                        var issueUrl = statusMatch.Groups["issueUrl"].Value;
                        var videoUrl = statusMatch.Groups["videoUrl"].Value;

                        var reviewItem = new ReviewItem
                        {
                            Status = status,
                            IssueUrl = issueUrl,
                            VideoUrl = videoUrl
                        };
                        review.Items.Add(reviewItem);

                        var gitHubUrlMatch = Regex.Match(issueUrl, gitHubUrlRegex);
                        if (gitHubUrlMatch.Success)
                        {
                            reviewItem.Owner = gitHubUrlMatch.Groups["owner"].Value;
                            reviewItem.Repo = gitHubUrlMatch.Groups["repo"].Value;
                            reviewItem.IssueId = int.Parse(gitHubUrlMatch.Groups["issueId"].Value);
                            reviewItem.CommentId = gitHubUrlMatch.Groups["commentId"].Value;
                        }

                        var youTubeUrlMatch = Regex.Match(videoUrl, youTubeUrlRegex);
                        if (youTubeUrlMatch.Success)
                        {
                            reviewItem.VideoId = youTubeUrlMatch.Groups["videoId"].Value;
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

                            reviewItem.Timecode = new TimeSpan(hours, minutes, seconds);
                        }
                    }
                    else
                    {
                        descriptionBuilder.AppendLine(line);
                    }
                }

                SetDescription();
            }

            return reviews;
        }
    }
}

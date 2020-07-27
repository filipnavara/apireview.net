using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ApiReview.Shared
{
    public static class GitHubIssueHelpers
    {
        public static string FixTitle(string title)
        {
            // Let's get rid of all the boiler plate prefixes, like:
            // [API] Add an API that does somethign
            // [Feature Request] I'd love some API
            // API: Linq expressions

            var labels = new[]
            {
                    "api",
                    "proposal",
                    "feature",
                    "request"
            };

            bool modified;

            do
            {
                modified = false;

                var match = Regex.Match(title, "^(\\[(?<prefix>[^\\]]+)\\]\\:?)|(?<prefix>(\\S+\\s+){1,3}\\S+\\:)");
                if (match.Success)
                {
                    var prefix = match.Groups["prefix"].Value;
                    if (labels.Any(l => prefix.Contains(l, StringComparison.OrdinalIgnoreCase)))
                    {
                        title = title.Substring(match.Index + match.Length).Trim();
                        modified = true;
                    }
                }
            } while (modified);

            return title;
        }

        public static string FixLabel(string labelName)
        {
            while (true)
            {
                var firstColon = labelName.IndexOf(':');
                if (firstColon < 0)
                    break;

                var secondColon = labelName.IndexOf(':', firstColon + 1);
                if (secondColon < 0)
                    break;

                var emojiStart = firstColon;
                var emojiLength = secondColon - emojiStart + 1;
                labelName = labelName.Remove(emojiStart, emojiLength);
            }

            return labelName.Trim();
        }

        public static string GetMarkdownLink(string owner, string repo, int id, string url, string title)
        {
            var fixedTitle = FixTitle(title);
            return $"[{owner}/{repo}#{id}: {fixedTitle}]({url})";
        }

        public static string GetHtmlLink(string owner, string repo, int id, string url, string title)
        {
            var fixedTitle = FixTitle(title);
            return $"<a href=\"{url}\">{owner}/{repo}#{id}: {fixedTitle}</a>";
        }
    }
}

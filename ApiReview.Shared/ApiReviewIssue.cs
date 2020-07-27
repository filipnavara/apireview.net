using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace ApiReview.Shared
{
    public sealed class ApiReviewIssue : IComparable<ApiReviewIssue>
    {
        public string Owner { get; set; }

        public string Repo { get; set; }

        public int Id { get; set; }

        [JsonIgnore]
        public string IdFull => $"{Owner}/{Repo}#{Id}";

        public string Title { get; set; }

        public string Author { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        [JsonIgnore]
        public string DetailText => $"{IdFull} {CreatedAt.FormatRelative()} by {Author}";

        public string Url { get; set; }

        public string Milestone { get; set; }

        public ApiReviewLabel[] Labels { get; set; }

        public bool IsBlocking => Labels != null && Labels.Any(l => string.Equals(l.Name, ApiReviewConstants.Blocking, StringComparison.OrdinalIgnoreCase));

        public int CompareTo(ApiReviewIssue other)
        {
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
            static bool IsNone(string m) => string.IsNullOrEmpty(m) || m == ApiReviewConstants.NoMilestone;

            if (IsNone(x) && IsNone(y))
                return 0;

            if (IsNone(x))
                return -1;

            if (IsNone(y))
                return 1;

            var xIsVersion = Version.TryParse(x, out var xVersion);
            var yIsVersion = Version.TryParse(y, out var yVersion);

            var result = -xIsVersion.CompareTo(yIsVersion);
            if (result != 0)
                return result;

            if (xIsVersion && yIsVersion)
                return xVersion.CompareTo(yVersion);

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}

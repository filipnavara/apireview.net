using System;
using System.Collections.Generic;

namespace RepoIndexer
{
    internal sealed class ReviewItem
    {
        public string Status { get; set; }

        public string IssueUrl { get; set; }
        public string Owner { get; set; }
        public string Repo { get; set; }
        public int IssueId { get; set; }
        public string CommentId { get; set; }

        public string VideoUrl { get; set; }
        public string VideoId { get; set; }
        public TimeSpan Timecode { get; set; }

        public string DescriptionMarkdown { get; set; }
        public List<string> Apis { get; } = new List<string>();
    }
}

using System;
using System.Collections.Generic;

namespace RepoIndexer
{
    internal sealed class Review
    {
        public DateTimeOffset Date { get; set; }
        public List<ReviewItem> Items { get; } = new List<ReviewItem>();
    }
}

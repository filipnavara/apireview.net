using System;
using System.Collections.Generic;

namespace ApiReview.Shared
{
    public sealed class ApiReviewItem
    {
        public int Id { get; set; }
        public ApiReviewDecisionKind Decision { get; set; }
        public ApiReviewIssue Issue { get; set; }
        public DateTimeOffset FeedbackDateTime { get; set; }
        public string FeedbackId { get; set; }
        public string FeedbackAuthor { get; set; }
        public string FeedbackUrl { get; set; }
        public string FeedbackMarkdown { get; set; }
        public TimeSpan TimeCode { get; set; }
        public IReadOnlyList<string> Apis { get; set; }
    }
}

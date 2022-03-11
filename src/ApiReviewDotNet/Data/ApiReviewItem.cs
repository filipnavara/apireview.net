﻿namespace ApiReviewDotNet.Data;

public sealed class ApiReviewItem
{
    public ApiReviewItem(ApiReviewDecision decision,
                         ApiReviewIssue issue,
                         DateTimeOffset feedbackDateTime,
                         string? feedbackId,
                         string? feedbackAuthor,
                         string feedbackUrl,
                         string? feedbackMarkdown,
                         IEnumerable<string>? apis)
    {
        Decision = decision;
        Issue = issue;
        FeedbackDateTime = feedbackDateTime;
        FeedbackId = feedbackId;
        FeedbackAuthor = feedbackAuthor;
        FeedbackUrl = feedbackUrl;
        FeedbackMarkdown = feedbackMarkdown;
        Apis = apis?.ToArray() ?? Array.Empty<string>();
    }

    public ApiReviewDecision Decision { get; }
    public ApiReviewIssue Issue { get; }
    public DateTimeOffset FeedbackDateTime { get; }
    public string? FeedbackId { get; }
    public string? FeedbackAuthor { get; }
    public string FeedbackUrl { get; }
    public string? FeedbackMarkdown { get; }
    public TimeSpan TimeCode { get; set; }
    public IReadOnlyList<string> Apis { get; }
}

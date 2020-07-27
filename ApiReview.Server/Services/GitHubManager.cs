using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Shared;

using Microsoft.Extensions.Configuration;

using Octokit;

namespace ApiReview.Server.Services
{
    public interface IGitHubManager
    {
        Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end);
        Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync();
    }

    public sealed class FakeGitHubManager : IGitHubManager
    {
        private readonly IReadOnlyList<ApiReviewIssue> _issues;
        private readonly IReadOnlyList<ApiReviewItem> _feedback;

        public FakeGitHubManager()
        {
            _issues = JsonSerializer.Deserialize<IReadOnlyList<ApiReviewIssue>>(Resources.GitHubFakeIssues);
            _feedback = JsonSerializer.Deserialize<IReadOnlyList<ApiReviewItem>>(Resources.GitHubFakeFeedback);
        }

        public Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end)
        {
            var result = _feedback.Where(f => start <= f.FeedbackDateTime && f.FeedbackDateTime <= end)
                                  .ToArray();

            return Task.FromResult<IReadOnlyList<ApiReviewItem>>(result);
        }

        public Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
        {
            return Task.FromResult(_issues);
        }
    }

    public sealed class GitHubManager : IGitHubManager
    {
        private readonly IConfiguration _configuration;
        private readonly GitHubClientFactory _clientFactory;

        public GitHubManager(IConfiguration configuration, GitHubClientFactory clientFactory)
        {
            _configuration = configuration;
            _clientFactory = clientFactory;
        }

        public Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end)
        {
            var repoList = _configuration["RepoList"];
            var repos = OrgAndRepo.ParseList(repoList).ToArray();
            return GetFeedbackAsync(repos, start, end);
        }

        private async Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(OrgAndRepo[] repos, DateTimeOffset start, DateTimeOffset end)
        {
            static bool IsApiIssue(Issue issue)
            {
                var isReadyForReview = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiReadyForReview);
                var isApproved = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiApproved);
                var needsWork = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiNeedsWork);
                return isReadyForReview || isApproved || needsWork;
            }

            static (string VideoLink, string Markdown) ParseFeedback(string body)
            {
                if (body == null)
                    return (null, null);

                const string prefix = "[Video](";
                if (body.StartsWith(prefix))
                {
                    var videoUrlEnd = body.IndexOf(")");
                    if (videoUrlEnd > 0)
                    {
                        var videoUrlStart = prefix.Length;
                        var videoUrlLength = videoUrlEnd - videoUrlStart;
                        var videoUrl = body.Substring(videoUrlStart, videoUrlLength);
                        var remainingBody = body.Substring(videoUrlEnd + 1).TrimStart();
                        return (videoUrl, remainingBody);
                    }
                }

                return (null, body);
            }

            // NOTE: Ideally, we'd use the user here, but if we do, we get a FORBIDDEN error.
            //       Not a biggie though, this API is only called by people with the api-approver
            //       role, so using the app quota seems fine.

            var github = await _clientFactory.CreateForAppAsync();
            var results = new List<ApiReviewItem>();

            foreach (var (owner, repo) in repos)
            {
                var request = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All,
                    State = ItemStateFilter.All,
                    Since = start
                };

                var issues = await github.Issue.GetAllForRepository(owner, repo, request);

                foreach (var issue in issues)
                {
                    if (!IsApiIssue(issue))
                        continue;

                    var events = await github.Issue.Events.GetAllForIssue(owner, repo, issue.Number);
                    var reviewOutcome = ApiReviewOutcome.Get(events, start, end);

                    if (reviewOutcome != null)
                    {
                        var title = GitHubIssueHelpers.FixTitle(issue.Title);
                        var feedbackDateTime = reviewOutcome.DecisionTime;

                        var decision = reviewOutcome.Decision;
                        var comments = await github.Issue.Comment.GetAllForIssue(owner, repo, issue.Number);
                        var comment = comments.Where(c => start <= c.CreatedAt && c.CreatedAt <= end)
                                              .Where(c => string.Equals(c.User.Login, reviewOutcome.DecisionMaker, StringComparison.OrdinalIgnoreCase))                                              
                                              .Select(c => (Comment: c, TimeDifference: Math.Abs((c.CreatedAt - feedbackDateTime).TotalSeconds)))
                                              .OrderBy(c => c.TimeDifference)
                                              .Select(c => c.Comment)
                                              .FirstOrDefault();

                        var feedbackId = comment?.Id.ToString();
                        var feedbackAuthor = reviewOutcome.DecisionMaker;
                        var feedbackUrl = comment?.HtmlUrl ?? issue.HtmlUrl;
                        var (videoUrl, feedbackMarkdown) = ParseFeedback(comment?.Body);

                        var apiReviewIssue = CreateIssue(owner, repo, issue);

                        var feedback = new ApiReviewItem
                        {
                            Decision = decision,
                            Issue = apiReviewIssue,
                            FeedbackId = feedbackId,
                            FeedbackAuthor = feedbackAuthor,
                            FeedbackDateTime = feedbackDateTime,
                            FeedbackUrl = feedbackUrl,
                            FeedbackMarkdown = feedbackMarkdown
                        };
                        results.Add(feedback);
                    }
                }
            }

            results.Sort((x, y) => x.FeedbackDateTime.CompareTo(y.FeedbackDateTime));
            return results;
        }

        public async Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
        {
            var repoList = _configuration["RepoList"];
            var repos = OrgAndRepo.ParseList(repoList).ToArray();

            var github = await _clientFactory.CreateForAppAsync();
            var result = new List<ApiReviewIssue>();

            foreach (var (owner, repo) in repos)
            {
                var request = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All,
                    State = ItemStateFilter.Open
                };
                request.Labels.Add(ApiReviewConstants.ApiReadyForReview);

                var issues = await github.Issue.GetAllForRepository(owner, repo, request);

                foreach (var issue in issues)
                {
                    var apiReviewIssue = CreateIssue(owner, repo, issue);
                    result.Add(apiReviewIssue);
                }
            }

            result.Sort();

            return result;
        }

        private static ApiReviewIssue CreateIssue(string owner, string repo, Issue issue)
        {
            var result = new ApiReviewIssue
            {
                Owner = owner,
                Repo = repo,
                Author = issue.User.Login,
                CreatedAt = issue.CreatedAt,
                Labels = issue.Labels.Select(l => new ApiReviewLabel { Name = l.Name, BackgroundColor = l.Color, Description = l.Description }).ToArray(),
                Milestone = issue.Milestone?.Title ?? ApiReviewConstants.NoMilestone,
                Title = GitHubIssueHelpers.FixTitle(issue.Title),
                Url = issue.HtmlUrl,
                Id = issue.Number
            };
            return result;
        }

        private sealed class ApiReviewOutcome
        {
            public ApiReviewOutcome(ApiReviewDecisionKind decision, string decisionMaker, DateTimeOffset decisionTime)
            {
                Decision = decision;
                DecisionMaker = decisionMaker;
                DecisionTime = decisionTime;
            }

            public static ApiReviewOutcome Get(IEnumerable<EventInfo> events, DateTimeOffset start, DateTimeOffset end)
            {
                var readyEvent = default(EventInfo);
                var current = default(ApiReviewOutcome);
                var rejection = default(ApiReviewOutcome);

                foreach (var e in events.Where(e => e.CreatedAt <= end)
                                        .OrderBy(e => e.CreatedAt))
                {
                    switch (e.Event.StringValue)
                    {
                        case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiReadyForReview, StringComparison.OrdinalIgnoreCase):
                            current = null;
                            readyEvent = e;
                            break;
                        case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiApproved, StringComparison.OrdinalIgnoreCase):
                            current = new ApiReviewOutcome(ApiReviewDecisionKind.Approved, e.Actor.Login, e.CreatedAt);
                            readyEvent = null;
                            break;
                        case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiNeedsWork, StringComparison.OrdinalIgnoreCase):
                            current = new ApiReviewOutcome(ApiReviewDecisionKind.NeedsWork, e.Actor.Login, e.CreatedAt);
                            readyEvent = null;
                            break;
                        case "reopened":
                            rejection = null;
                            break;
                        case "closed":
                            if (readyEvent != null)
                                rejection = new ApiReviewOutcome(ApiReviewDecisionKind.Rejected, e.Actor.Login, e.CreatedAt);
                            break;
                    }
                }

                if (rejection != null)
                    current = rejection;

                if (current != null)
                {
                    var inInterval = start <= current.DecisionTime && current.DecisionTime <= end;
                    if (!inInterval)
                        return null;
                }

                return current;
            }

            public ApiReviewDecisionKind Decision { get; }
            public string DecisionMaker { get; }
            public DateTimeOffset DecisionTime { get; }
        }
    }
}

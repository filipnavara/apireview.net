using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Shared;

using Octokit;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;
using static Octokit.GraphQL.Variable;

using Issue = Octokit.Issue;

namespace ApiReview.Server.Services
{
    public interface IGitHubManager
    {
        Task<IReadOnlyList<ApiReviewFeedback>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end);
        Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync();
    }

    public sealed class FakeGitHubManager : IGitHubManager
    {
        private readonly IReadOnlyList<ApiReviewIssue> _issues;
        private readonly IReadOnlyList<ApiReviewFeedback> _feedback;

        public FakeGitHubManager()
        {
            _issues = JsonSerializer.Deserialize<IReadOnlyList<ApiReviewIssue>>(Resources.GitHubFakeIssues);
            _feedback = JsonSerializer.Deserialize<IReadOnlyList<ApiReviewFeedback>>(Resources.GitHubFakeFeedback);
        }

        public Task<IReadOnlyList<ApiReviewFeedback>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end)
        {
            var result = _feedback.Where(f => start <= f.FeedbackDateTime && f.FeedbackDateTime <= end)
                                  .ToArray();

            return Task.FromResult<IReadOnlyList<ApiReviewFeedback>>(result);
        }

        public Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
        {
            return Task.FromResult(_issues);
        }
    }

    public sealed class GitHubManager : IGitHubManager
    {
        // TODO: Extract to config
        private const string _repoList = "dotnet/designs,dotnet/runtime,dotnet/winforms";
        private readonly GitHubClientFactory _clientFactory;

        public GitHubManager(GitHubClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public Task<IReadOnlyList<ApiReviewFeedback>> GetFeedbackAsync(DateTimeOffset start, DateTimeOffset end)
        {
            var repos = OrgAndRepo.ParseList(_repoList).ToArray();
            return GetFeedbackAsync(repos, start, end);
        }

        private async Task<IReadOnlyList<ApiReviewFeedback>> GetFeedbackAsync(OrgAndRepo[] repos, DateTimeOffset start, DateTimeOffset end)
        {
            static bool IsApiIssue(FeedbackIssue issue)
            {
                var isReadyForReview = issue.Labels.Any(l => l.Name == "api-ready-for-review");
                var isApproved = issue.Labels.Any(l => l.Name == "api-approved");
                var needsWork = issue.Labels.Any(l => l.Name == "api-needs-work");
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

            static ApiReviewIssue CreateIssue(FeedbackIssue issue)
            {
                var result = new ApiReviewIssue
                {
                    Owner = issue.Owner,
                    Repo = issue.Repo,
                    Author = issue.Author,
                    CreatedAt = issue.CreateAt,
                    Labels = issue.Labels.ToArray(),
                    //Milestone = issue.Milestone ?? "(None)",
                    Title = GitHubIssueHelpers.FixTitle(issue.Title),
                    Url = issue.Url,
                    Id = issue.Number
                };
                return result;
            }

            var filter = new IssueFilters()
            {
                Assignee = "*",
                Milestone = "*",
                Since = start,
            };
            var query = new Query()
                .Repository(Var("repo"), Var("owner"))
                .Issues(filterBy: filter)
                .AllPages()
                .Select(i => new FeedbackIssue
                {
                    Owner = i.Repository.Owner.Login,
                    Repo = i.Repository.Name,
                    Number = i.Number,
                    Title = i.Title,
                    CreateAt = i.CreatedAt,
                    Author = i.Author.Login,
                    State = i.State,
                    //Milestone = i.Milestone.Title,
                    Url = i.Url,
                    Labels = i.Labels(null, null, null, null, null)
                              .AllPages()
                              .Select(l => new ApiReviewLabel
                              {
                                  Name = l.Name,
                                  BackgroundColor = l.Color
                              }).ToList(),
                    TimelineItems = i
                        .TimelineItems(null, null, null, null, null, null, null)
                        .AllPages()
                        .Select(tl => tl == null ? null : tl.Switch<ApiTimelineItem>(when =>
                        when.IssueComment(ic => new ApiTimelineCommented
                        {
                            Id = ic.Id.Value,
                            Body = ic.Body,
                            Url = ic.Url,
                            Actor = ic.Author.Login,
                            CreatedAt = ic.CreatedAt
                        }).LabeledEvent(l => new ApiTimelineLabeled
                        {
                            LabelName = l.Label.Name,
                            Actor = l.Actor.Login,
                            CreatedAt = l.CreatedAt
                        }).ReopenedEvent(c => new ApiTimelineReopened
                        {
                            Actor = c.Actor.Login,
                            CreatedAt = c.CreatedAt
                        }).ClosedEvent(c => new ApiTimelineClosed
                        {
                            Actor = c.Actor.Login,
                            CreatedAt = c.CreatedAt
                        }))).ToList()
                }).Compile();

            var connection = await _clientFactory.CreateGraphAsync();
            var vars = new Dictionary<string, object>();

            var issues = new List<FeedbackIssue>();

            foreach (var ownerAndRepo in repos)
            {
                vars["owner"] = ownerAndRepo.OrgName;
                vars["repo"] = ownerAndRepo.RepoName;
                try
                {
                    var current = await connection.Run(query, vars);
                    issues.AddRange(current);
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            issues.ForEach(i => i.TimelineItems.RemoveAll(ti => ti == null));

            var results = new List<ApiReviewFeedback>();

            foreach (var issue in issues)
            {
                if (!IsApiIssue(issue))
                    continue;

                var reviewOutcome = ApiReviewOutcome.Get(issue.TimelineItems, start, end);
                if (reviewOutcome != null)
                {
                    var title = GitHubIssueHelpers.FixTitle(issue.Title);
                    var decision = reviewOutcome.Decision;
                    var feedbackDateTime = reviewOutcome.DecisionTime;
                    var comments = issue.TimelineItems.OfType<ApiTimelineCommented>();
                    var comment = comments.Where(c => start <= c.CreatedAt && c.CreatedAt <= end)
                                          .Where(c => string.Equals(c.Actor, reviewOutcome.DecisionMaker, StringComparison.OrdinalIgnoreCase))
                                          .Select(c => (Comment: c, TimeDifference: Math.Abs((c.CreatedAt - feedbackDateTime).TotalSeconds))).OrderBy(c => c.TimeDifference)
                                          .Select(c => c.Comment)
                                          .FirstOrDefault();

                    var feedbackId = comment?.Id;
                    var feedbackUrl = comment?.Url ?? issue.Url;
                    var (videoUrl, feedbackMarkdown) = ParseFeedback(comment?.Body);

                    var apiReviewIssue = CreateIssue(issue);

                    var feedback = new ApiReviewFeedback
                    {
                        Issue = apiReviewIssue,
                        Decision = decision,
                        FeedbackId = feedbackId,
                        FeedbackDateTime = feedbackDateTime,
                        FeedbackUrl = feedbackUrl,
                        FeedbackMarkdown = feedbackMarkdown,
                        VideoUrl = videoUrl
                    };
                    results.Add(feedback);
                }
            }

            results.Sort((x, y) => x.FeedbackDateTime.CompareTo(y.FeedbackDateTime));
            return results;
        }

        public async Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
        {
            var repos = OrgAndRepo.ParseList(_repoList).ToArray();

            var github = await _clientFactory.CreateAsync();
            var result = new List<ApiReviewIssue>();

            foreach (var (owner, repo) in repos)
            {
                var request = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All,
                    State = ItemStateFilter.Open
                };
                request.Labels.Add("api-ready-for-review");

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
                Milestone = issue.Milestone?.Title ?? "(None)",
                Title = GitHubIssueHelpers.FixTitle(issue.Title),
                Url = issue.HtmlUrl,
                Id = issue.Number
            };
            return result;
        }

        private sealed class ApiReviewOutcome
        {
            public ApiReviewOutcome(ApiReviewDecision decision, string decisionMaker, DateTimeOffset decisionTime)
            {
                Decision = decision;
                DecisionMaker = decisionMaker;
                DecisionTime = decisionTime;
            }

            public static ApiReviewOutcome Get(IEnumerable<ApiTimelineItem> items, DateTimeOffset start, DateTimeOffset end)
            {
                var readyEvent = default(ApiTimelineLabeled);
                var current = default(ApiReviewOutcome);
                var rejection = default(ApiReviewOutcome);

                foreach (var e in items.Where(e => e.CreatedAt <= end)
                                       .OrderBy(e => e.CreatedAt))
                {
                    switch (e)
                    {
                        case ApiTimelineLabeled rl when string.Equals(rl.LabelName, "api-ready-for-review", StringComparison.OrdinalIgnoreCase):
                            current = null;
                            readyEvent = rl;
                            break;
                        case ApiTimelineLabeled al when string.Equals(al.LabelName, "api-approved", StringComparison.OrdinalIgnoreCase):
                            current = new ApiReviewOutcome(ApiReviewDecision.Approved, e.Actor, e.CreatedAt);
                            readyEvent = null;
                            break;
                        case ApiTimelineLabeled wl when string.Equals(wl.LabelName, "api-needs-work", StringComparison.OrdinalIgnoreCase):
                            if (readyEvent != null)
                            {
                                current = new ApiReviewOutcome(ApiReviewDecision.NeedsWork, e.Actor, e.CreatedAt);
                                readyEvent = null;
                            }
                            break;
                        case ApiTimelineReopened _:
                            rejection = null;
                            break;
                        case ApiTimelineClosed _:
                            if (readyEvent != null)
                                rejection = new ApiReviewOutcome(ApiReviewDecision.Rejected, e.Actor, e.CreatedAt);
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

            public ApiReviewDecision Decision { get; }
            public string DecisionMaker { get; }
            public DateTimeOffset DecisionTime { get; }
        }

        private sealed class FeedbackIssue
        {
            public string Owner { get; set; }
            public string Repo { get; set; }
            public int Number { get; set; }
            public DateTimeOffset CreateAt { get; set; }
            public string Author { get; set; }
            public string Title { get; set; }
            public IssueState State { get; set; }
            public string Milestone { get; set; }
            public string Url { get; set; }
            public List<ApiReviewLabel> Labels { get; set; }
            public List<ApiTimelineItem> TimelineItems { get; set; }

            public override string ToString()
            {
                return $"{Owner}/{Repo}#{Number}: {Title}";
            }
        }

        private abstract class ApiTimelineItem
        {
            public string Actor { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
        }

        private sealed class ApiTimelineCommented : ApiTimelineItem
        {
            public string Id { get; set; }
            public string Body { get; set; }
            public string Url { get; set; }
        }

        private sealed class ApiTimelineLabeled : ApiTimelineItem
        {
            public string LabelName { get; set; }
        }

        private sealed class ApiTimelineReopened : ApiTimelineItem
        {
        }

        private sealed class ApiTimelineClosed : ApiTimelineItem
        {
        }
    }
}

using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services.Ospo;

using Octokit;

namespace ApiReviewDotNet.Services.GitHub;

public sealed class GitHubManager : IGitHubManager
{
    private readonly RepositoryGroupService _repositoryGroupService;
    private readonly GitHubClientFactory _clientFactory;
    private readonly AreaOwnerService _areaOwnerService;
    private readonly OspoService _ospoService;

    public GitHubManager(RepositoryGroupService repositoryGroupService, GitHubClientFactory clientFactory, AreaOwnerService areaOwnerService, OspoService ospoService)
    {
        _repositoryGroupService = repositoryGroupService;
        _clientFactory = clientFactory;
        _areaOwnerService = areaOwnerService;
        _ospoService = ospoService;
    }

    public async Task<IReadOnlyList<ApiReviewItem>> GetFeedbackAsync(IReadOnlyCollection<OrgAndRepo> repos, DateTimeOffset start, DateTimeOffset end)
    {
        static bool MightBeAnApiIssue(Issue issue)
        {
            var isClosed = issue.State.Value == ItemState.Closed;
            var isReadyForReview = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiReadyForReview);
            var isApproved = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiApproved);
            var needsWork = issue.Labels.Any(l => l.Name == ApiReviewConstants.ApiNeedsWork);
            return isClosed || isReadyForReview || isApproved || needsWork;
        }

        static (string? VideoLink, string? Markdown) ParseFeedback(string? body)
        {
            if (body is null)
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
                if (!MightBeAnApiIssue(issue))
                    continue;

                var events = await github.Issue.Events.GetAllForIssue(owner, repo, issue.Number);
                var reviewOutcome = ApiReviewOutcome.Get(events, start, end);

                if (reviewOutcome is not null)
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
                    var (_, feedbackMarkdown) = ParseFeedback(comment?.Body);

                    var apiReviewIssue = CreateIssue(owner, repo, issue, events, end);

                    var feedback = new ApiReviewItem(
                        decision: decision,
                        issue: apiReviewIssue,
                        feedbackId: feedbackId,
                        feedbackAuthor: feedbackAuthor,
                        feedbackDateTime: feedbackDateTime,
                        feedbackUrl: feedbackUrl,
                        feedbackMarkdown: feedbackMarkdown,
                        apis: null
                    );
                    results.Add(feedback);
                }
            }
        }

        results.Sort((x, y) => x.FeedbackDateTime.CompareTo(y.FeedbackDateTime));
        return results;
    }

    public async Task<IReadOnlyList<ApiReviewIssue>> GetIssuesAsync()
    {
        var repos = _repositoryGroupService.Repositories;

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
                var events = await github.Issue.Events.GetAllForIssue(owner, repo, issue.Number);
                var apiReviewIssue = CreateIssue(owner, repo, issue, events, DateTime.Now);

                result.Add(apiReviewIssue);
            }
        }

        result.Sort();

        return result;
    }

    private ApiReviewer[] GetReviewers(string author,
                                       IReadOnlyList<string> assignees,
                                       string? markedReadyForReviewBy,
                                       IReadOnlyList<string> areaOwners)
    {
        var linkSet = _ospoService.LinkSet;
        var result = new List<ApiReviewer>();

        Add(result, linkSet, author);
        foreach (var assignee in assignees ?? Array.Empty<string>())
            Add(result, linkSet, assignee);
        Add(result, linkSet, markedReadyForReviewBy);
        foreach (var areaOwner in areaOwners ?? Array.Empty<string>())
            Add(result, linkSet, areaOwner);

        return result.ToArray();

        static void Add(List<ApiReviewer> target, OspoLinkSet linkSet, string? userName)
        {
            if (userName is null)
                return;

            if (target.Any(r => string.Equals(r.GitHubUserName, userName, StringComparison.OrdinalIgnoreCase)))
                return;

            if (linkSet.LinkByLogin.TryGetValue(userName, out var link))
            {
                var reviewer = new ApiReviewer(
                    gitHubUserName: userName,
                    name: link.MicrosoftInfo.PreferredName,
                    email: link.MicrosoftInfo.EmailAddress
                );
                target.Add(reviewer);
            }
        }
    }

    private ApiReviewIssue CreateIssue(string owner, string repo, Issue issue, IEnumerable<EventInfo> events, DateTimeOffset end)
    {
        var readyEvent = ApiReadyEvent.Get(events, issue.CreatedAt, end);

        var title = GitHubIssueHelpers.FixTitle(issue.Title);
        var author = issue.User.Login;
        var assignees = issue.Assignees.Select(a => a.Login).ToArray();
        var markedReadyForReviewBy = readyEvent?.DecisionMaker;
        var areaOwners = GetAreaOwners(issue.Labels.Select(l => l.Name));
        var milestone = issue.Milestone?.Title ?? ApiReviewConstants.NoMilestone;
        var labels = issue.Labels.Select(l => new ApiReviewLabel(l.Name, l.Color, l.Description)).ToArray();
        var reviewers = GetReviewers(author, assignees, markedReadyForReviewBy, areaOwners);

        var result = new ApiReviewIssue(
            owner,
            repo,
            issue.Number,
            title,
            author,
            assignees,
            markedReadyForReviewBy,
            areaOwners,
            issue.CreatedAt,
            issue.HtmlUrl,
            milestone,
            labels,
            reviewers
        );

        return result;
    }

    private string[] GetAreaOwners(IEnumerable<string> labels)
    {
        var result = new List<string>();

        foreach (var label in labels)
        {
            var owners = _areaOwnerService.GetOwners(label);
            result.AddRange(owners);
        }

        return result.ToArray();
    }

    private sealed class ApiReadyEvent
    {
        public ApiReadyEvent(string decisionMaker, DateTimeOffset createdAt)
        {
            DecisionMaker = decisionMaker;
            CreatedAt = createdAt;
        }

        public string DecisionMaker { get; }
        public DateTimeOffset CreatedAt { get; }

        public static ApiReadyEvent? Get(IEnumerable<EventInfo> events, DateTimeOffset start, DateTimeOffset end)
        {
            var readyEvent = default(EventInfo);

            foreach (var e in events.Where(e => e.CreatedAt <= end)
                                    .OrderByDescending(e => e.CreatedAt))
                switch (e.Event.StringValue)
                {
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiReadyForReview, StringComparison.OrdinalIgnoreCase):
                        readyEvent = e;
                        break;
                }

            return readyEvent is null
                    ? null
                    : new ApiReadyEvent(readyEvent.Actor.Login, readyEvent.CreatedAt);
        }
    }

    private sealed class ApiReviewOutcome
    {
        public ApiReviewOutcome(ApiReviewDecision decision, string decisionMaker, DateTimeOffset decisionTime)
        {
            Decision = decision;
            DecisionMaker = decisionMaker;
            DecisionTime = decisionTime;
        }

        public static ApiReviewOutcome? Get(IEnumerable<EventInfo> events, DateTimeOffset start, DateTimeOffset end)
        {
            var readyEvent = default(EventInfo);
            var current = default(ApiReviewOutcome);
            var rejection = default(ApiReviewOutcome);

            foreach (var e in events.Where(e => e.CreatedAt <= end)
                                    .OrderBy(e => e.CreatedAt))
                switch (e.Event.StringValue)
                {
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiReadyForReview, StringComparison.OrdinalIgnoreCase):
                        current = null;
                        readyEvent = e;
                        break;
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiApproved, StringComparison.OrdinalIgnoreCase):
                        current = new ApiReviewOutcome(ApiReviewDecision.Approved, e.Actor.Login, e.CreatedAt);
                        readyEvent = null;
                        break;
                    case "labeled" when string.Equals(e.Label.Name, ApiReviewConstants.ApiNeedsWork, StringComparison.OrdinalIgnoreCase):
                        current = new ApiReviewOutcome(ApiReviewDecision.NeedsWork, e.Actor.Login, e.CreatedAt);
                        readyEvent = null;
                        break;
                    case "reopened":
                        rejection = null;
                        break;
                    case "closed":
                        if (readyEvent is not null)
                            rejection = new ApiReviewOutcome(ApiReviewDecision.Rejected, e.Actor.Login, e.CreatedAt);
                        break;
                }

            if (rejection is not null)
                current = rejection;

            if (current is not null)
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
}

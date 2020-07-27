using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Shared;

using Octokit;

namespace RepoIndexer
{
    internal sealed class GitHubIssueCache
    {
        private readonly string _path;
        private readonly string _gitHubKey;
        private Dictionary<(string Owner, string Repo, int Id), ApiReviewIssue>? _cache;
        private GitHubClient? _gitHubClient;

        public GitHubIssueCache(string path, string gitHubKey)
        {
            _path = path;
            _gitHubKey = gitHubKey;
        }

        public async Task<ApiReviewIssue> GetIssue(string owner, string repo, int number)
        {
            if (_cache == null)
                _cache = await LoadCache(_path);

            var key = (owner, repo, number);

            if (!_cache.TryGetValue(key, out var issue))
            {
                if (_gitHubClient == null)
                {
                    var productInformation = new ProductHeaderValue("RepoIndexer");
                    _gitHubClient = new GitHubClient(productInformation)
                    {
                        Credentials = new Credentials(_gitHubKey)
                    };
                }

                Console.WriteLine($"Loading issue {key.owner}/{key.repo}#{key.number}");

                var githubIssue = await _gitHubClient.Issue.Get(owner, repo, number);
                issue = CreateIssue(owner, repo, number, githubIssue);

                _cache.Add(key, issue);

                var issues = _cache.Values.OrderBy(r => r.Owner)
                                          .ThenBy(r => r.Repo)
                                          .ThenBy(r => r.Id);
                using var stream = File.Create(_path);
                await JsonSerializer.SerializeAsync(stream, issues, MyJsonSerializerOptions.Instance);
            }

            return issue;
        }

        private static async Task<Dictionary<(string Owner, string Repo, int Id), ApiReviewIssue>> LoadCache(string path)
        {
            var cache = new Dictionary<(string Owner, string Repo, int Id), ApiReviewIssue>();

            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var issues = await JsonSerializer.DeserializeAsync<IReadOnlyList<ApiReviewIssue>>(stream);

                foreach (var issue in issues)
                {
                    var key = (issue.Owner, issue.Repo, issue.Id);
                    cache.Add(key, issue);
                }
            }

            return cache;
        }

        private static ApiReviewIssue CreateIssue(string owner, string repo, int number, Issue issue)
        {
            var result = new ApiReviewIssue
            {
                Owner = owner,
                Repo = repo,
                Id = number,
                Author = issue.User.Login,
                CreatedAt = issue.CreatedAt,
                Labels = issue.Labels.Select(l => new ApiReviewLabel { Name = l.Name, BackgroundColor = l.Color, Description = l.Description }).ToArray(),
                Milestone = issue.Milestone?.Title ?? ApiReviewConstants.NoMilestone,
                Title = GitHubIssueHelpers.FixTitle(issue.Title),
                Url = issue.HtmlUrl,
            };
            return result;
        }
    }
}

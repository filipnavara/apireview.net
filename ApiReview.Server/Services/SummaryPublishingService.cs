using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

using ApiReview.Shared;
using Markdig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Octokit;

namespace ApiReview.Server.Services
{
    public sealed class SummaryPublishingService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly GitHubClientFactory _clientFactory;
        private readonly YouTubeServiceFactory _youTubeServiceFactory;

        public SummaryPublishingService(IWebHostEnvironment env,
                                        IConfiguration configuration,
                                        GitHubClientFactory clientFactory,
                                        YouTubeServiceFactory youTubeServiceFactory)
        {
            _env = env;
            _configuration = configuration;
            _clientFactory = clientFactory;
            _youTubeServiceFactory = youTubeServiceFactory;
        }

        public async Task<ApiReviewPublicationResult> PublishAsync(ApiReviewSummary summary)
        {
            if (!summary.Items.Any())
                return ApiReviewPublicationResult.Failed();

            if (_env.IsDevelopment())
            {
                await UpdateCommentsDevAsync(summary);
            }
            else
            {
                // Apparently, we can't easily modify video descriptions in the cloud.
                // If someone has a fix for that, I'd be massively thankful.
                //
                // await UpdateVideoDescriptionAsync(summary);
                await UpdateCommentsAsync(summary);
            }

            var url = await CommitAsync(summary);
            await SendEmailAsync(summary);
            return ApiReviewPublicationResult.Suceess(url);
        }

        private async Task SendEmailAsync(ApiReviewSummary summary)
        {
            var from = _configuration["MailFrom"];
            var to = _configuration["MailTo"];
            var userName = _configuration["MailUserName"];
            var password = _configuration["MailPassword"];
            var host = _configuration["MailHost"];
            var port = Convert.ToInt32(_configuration["MailPort"]);

            var date = summary.Items.First().FeedbackDateTime.Date;
            var subject = $"API Review Notes {date:d}";
            var markdown = GetMarkdown(summary);
            var body = Markdown.ToHtml(markdown);

            var msg = new MailMessage
            {
                From = new MailAddress(from),
                To = {
                    new MailAddress(to)
                },
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            var client = new SmtpClient
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(userName, password),
                Port = port,
                Host = host,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = true
            };

            try
            {
                await client.SendMailAsync(msg);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task UpdateVideoDescriptionAsync(ApiReviewSummary summary)
        {
            if (summary.Video == null)
                return;

            using var descriptionBuilder = new StringWriter();
            foreach (var item in summary.Items)
            {
                var tc = item.TimeCode;
                descriptionBuilder.WriteLine($"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00} - {item.Decision}: {item.Issue.Title} {item.FeedbackUrl}");
            }

            var description = descriptionBuilder.ToString()
                                                .Replace("<", "(")
                                                .Replace(">", ")");

            var service = _youTubeServiceFactory.Create();

            var listRequest = service.Videos.List("snippet");
            listRequest.Id = summary.Video.Id;
            var listResponse = await listRequest.ExecuteAsync();

            var video = listResponse.Items[0];
            video.Snippet.Description = description;

            var updateRequest = service.Videos.Update(video, "snippet");
            await updateRequest.ExecuteAsync();
        }

        private async Task UpdateCommentsAsync(ApiReviewSummary summary)
        {
            var github = await _clientFactory.CreateForAppAsync();

            foreach (var item in summary.Items)
            {
                var videoUrl = summary.GetVideoUrl(item.TimeCode);

                if (item.FeedbackId != null && videoUrl != null)
                {
                    var updatedMarkdown = $"[Video]({videoUrl})\n\n{item.FeedbackMarkdown}";
                    var commentId = Convert.ToInt32(item.FeedbackId);
                    await github.Issue.Comment.Update(item.Issue.Owner, item.Issue.Repo, commentId, updatedMarkdown);
                }
            }
        }

        private async Task UpdateCommentsDevAsync(ApiReviewSummary summary)
        {
            var testRepo = _configuration["RepoList"];
            var (owner, repo) = OrgAndRepo.Parse(testRepo);

            if (!summary.Items.All(i => i.Issue.Owner == owner &&
                                        i.Issue.Repo == repo))
                return;

            var github = await _clientFactory.CreateForAppAsync();

            foreach (var item in summary.Items)
            {
                if (item.FeedbackId != null)
                {
                    var status = item.Decision.ToString();
                    var updatedMarkdown = $"[Video]({status})\n\n{item.FeedbackMarkdown}";
                    var commentId = Convert.ToInt32(item.FeedbackId);
                    await github.Issue.Comment.Update(item.Issue.Owner, item.Issue.Repo, commentId, updatedMarkdown);
                }
            }
        }

        private async Task<string> CommitAsync(ApiReviewSummary summary)
        {
            var ownerRepoString = _configuration["ApiReviewsRepo"];
            var (owner, repo) = OrgAndRepo.Parse(ownerRepoString);
            var branch = ApiReviewConstants.ApiReviewsBranch;
            var head = $"heads/{branch}";
            var date = summary.Items.FirstOrDefault().FeedbackDateTime.DateTime;
            var markdown = $"# Quick Reviews {date:d}\n\n{GetMarkdown(summary)}";
            var path = $"{date.Year}/{date.Month:00}-{date.Day:00}-quick-reviews/README.md";
            var commitMessage = $"Add quick review notes for {date:d}";

            var github = await _clientFactory.CreateForAppAsync();
            var masterReference = await github.Git.Reference.Get(owner, repo, head);
            var latestCommit = await github.Git.Commit.Get(owner, repo, masterReference.Object.Sha);

            var recursiveTreeResponse = await github.Git.Tree.GetRecursive(owner, repo, latestCommit.Tree.Sha);
            var file = recursiveTreeResponse.Tree.SingleOrDefault(t => t.Path == path);

            if (file == null)
            {
                var newTreeItem = new NewTreeItem
                {
                    Mode = "100644",
                    Path = path,
                    Content = markdown
                };

                var newTree = new NewTree
                {
                    BaseTree = latestCommit.Tree.Sha
                };
                newTree.Tree.Add(newTreeItem);

                var newTreeResponse = await github.Git.Tree.Create(owner, repo, newTree);
                var newCommit = new NewCommit(commitMessage, newTreeResponse.Sha, latestCommit.Sha);
                var newCommitResponse = await github.Git.Commit.Create(owner, repo, newCommit);

                var newReference = new ReferenceUpdate(newCommitResponse.Sha);
                var newReferenceResponse = await github.Git.Reference.Update(owner, repo, head, newReference);
            }

            var url = $"https://github.com/{owner}/{repo}/blob/{branch}/{path}";
            return url;
        }

        private static string GetMarkdown(ApiReviewSummary summary)
        {
            var noteWriter = new StringWriter();

            foreach (var item in summary.Items)
            {
                noteWriter.WriteLine($"## {item.Issue.Title}");
                noteWriter.WriteLine();
                noteWriter.Write($"**{item.Decision}** | [#{item.Issue.Repo}/{item.Issue.Id}]({item.FeedbackUrl})");

                var videoUrl = summary.GetVideoUrl(item.TimeCode);
                if (videoUrl != null)
                    noteWriter.Write($" | [Video]({videoUrl})");

                noteWriter.WriteLine();
                noteWriter.WriteLine();

                if (item.FeedbackMarkdown != null)
                {
                    noteWriter.Write(item.FeedbackMarkdown);
                    noteWriter.WriteLine();
                }
            }

            return noteWriter.ToString();
        }
    }
}

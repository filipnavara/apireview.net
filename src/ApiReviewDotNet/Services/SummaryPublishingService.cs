﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

using ApiReviewDotNet.Data;
using ApiReviewDotNet.Services.GitHub;
using ApiReviewDotNet.Services.YouTube;

using Markdig;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Octokit;

namespace ApiReviewDotNet.Services
{
    public sealed class SummaryPublishingService
    {
        private readonly ILogger<SummaryPublishingService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IOptions<MailOptions> _mailOptions;
        private readonly RepositoryGroupService _repositoryGroupService;
        private readonly IConfiguration _configuration;
        private readonly GitHubClientFactory _clientFactory;
        private readonly YouTubeServiceFactory _youTubeServiceFactory;

        public SummaryPublishingService(ILogger<SummaryPublishingService> logger,
                                        IWebHostEnvironment env,
                                        IOptions<MailOptions> mailOptions,
                                        RepositoryGroupService repositoryGroupService,
                                        IConfiguration configuration,
                                        GitHubClientFactory clientFactory,
                                        YouTubeServiceFactory youTubeServiceFactory)
        {
            _logger = logger;
            _env = env;
            _mailOptions = mailOptions;
            _repositoryGroupService = repositoryGroupService;
            _configuration = configuration;
            _clientFactory = clientFactory;
            _youTubeServiceFactory = youTubeServiceFactory;
        }

        public async Task<ApiReviewPublicationResult> PublishAsync(ApiReviewSummary summary)
        {
            if (!summary.Items.Any())
                return ApiReviewPublicationResult.Failed();

            var group = _repositoryGroupService.Get(summary.RepositoryGroup);
            if (group is null)
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

            var url = await CommitAsync(group, summary);
            await SendEmailAsync(group, summary);
            return ApiReviewPublicationResult.Suceess(url);
        }

        private async Task SendEmailAsync(RepositoryGroup group, ApiReviewSummary summary)
        {
            if (group.MailingList is null)
                return;

            var mailOptions = _mailOptions.Value;
            var date = summary.Items.First().Feedback.FeedbackDateTime.Date;
            var subject = $"API Review Notes {date:d}";
            var markdown = GetMarkdown(summary);
            var body = Markdown.ToHtml(markdown);

            var msg = new MailMessage
            {
                From = new MailAddress(mailOptions.From),
                To = {
                    new MailAddress(group.MailingList)
                },
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            var client = new SmtpClient
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(mailOptions.UserName, mailOptions.Password),
                Port = mailOptions.Port,
                Host = mailOptions.Host,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = true
            };

            try
            {
                await client.SendMailAsync(msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email: {Message}", ex.Message);
            }
        }

        private async Task UpdateVideoDescriptionAsync(ApiReviewSummary summary)
        {
            if (summary.Video == null)
                return;

            using var descriptionBuilder = new StringWriter();
            foreach (var item in summary.Items)
            {
                var tc = item.VideoTimeCode;
                descriptionBuilder.WriteLine($"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00} - {item.Feedback.Decision}: {item.Feedback.Issue.Title} {item.Feedback.FeedbackUrl}");
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
                var feedback = item.Feedback;

                if (feedback.VideoUrl == null && feedback.FeedbackId != null && item.VideoTimeCodeUrl != null)
                {
                    var updatedMarkdown = $"[Video]({item.VideoTimeCodeUrl})\n\n{feedback.FeedbackMarkdown}";
                    var commentId = Convert.ToInt32(feedback.FeedbackId);
                    await github.Issue.Comment.Update(feedback.Issue.Owner, feedback.Issue.Repo, commentId, updatedMarkdown);
                }
            }
        }

        private async Task UpdateCommentsDevAsync(ApiReviewSummary summary)
        {
            var (owner, repo) = _repositoryGroupService.Repositories.First();

            if (!summary.Items.All(i => i.Feedback.Issue.Owner == owner &&
                                        i.Feedback.Issue.Repo == repo))
                return;

            var github = await _clientFactory.CreateForAppAsync();

            foreach (var item in summary.Items)
            {
                var feedback = item.Feedback;

                if (feedback.FeedbackId != null)
                {
                    var status = feedback.Decision.ToString();
                    var updatedMarkdown = $"[Video]({status})\n\n{feedback.FeedbackMarkdown}";
                    var commentId = Convert.ToInt32(feedback.FeedbackId);
                    await github.Issue.Comment.Update(feedback.Issue.Owner, feedback.Issue.Repo, commentId, updatedMarkdown);
                }
            }
        }

        private async Task<string> CommitAsync(RepositoryGroup group, ApiReviewSummary summary)
        {
            var ownerRepoString = _configuration["ApiReviewsRepo"];
            var (owner, repo) = OrgAndRepo.Parse(ownerRepoString);
            var branch = ApiReviewConstants.ApiReviewsBranch;
            var head = $"heads/{branch}";
            var date = summary.Items.FirstOrDefault().Feedback.FeedbackDateTime.DateTime;
            var markdown = $"# API Review {date:d}\n\n{GetMarkdown(summary)}";
            var path = $"{date.Year}/{date.Month:00}-{date.Day:00}-{group.NotesSuffix}/README.md";
            var commitMessage = $"Add review notes for {date:d}";

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
                var feedback = item.Feedback;

                noteWriter.WriteLine($"## {feedback.Issue.Title}");
                noteWriter.WriteLine();
                noteWriter.Write($"**{feedback.Decision}** | [#{feedback.Issue.Repo}/{feedback.Issue.Id}]({feedback.FeedbackUrl})");

                if (item.VideoTimeCodeUrl != null)
                    noteWriter.Write($" | [Video]({item.VideoTimeCodeUrl})");

                noteWriter.WriteLine();
                noteWriter.WriteLine();

                if (feedback.FeedbackMarkdown != null)
                {
                    noteWriter.Write(feedback.FeedbackMarkdown);
                    noteWriter.WriteLine();
                }
            }

            return noteWriter.ToString();
        }
    }
}

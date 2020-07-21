﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Client.Services;
using ApiReview.Shared;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ApiReview.Client.Controllers
{
    [ApiController]
    [Route("github-webhook")]
    [AllowAnonymous]
    public sealed class GitHubWebHookController : Controller
    {
        private static readonly HashSet<string> _relevantActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "opened",
            "edited",
            "deleted",
            "closed",
            "reopened",
            "assigned",
            "unassigned",
            "labeled",
            "unlabeled",
            "transferred",
            "milestoned",
            "demilestoned"
        };

        private static readonly HashSet<string> _relevantLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ApiReviewConstants.ApiReadyForReview,
            ApiReviewConstants.ApiApproved,
            ApiReviewConstants.ApiNeedsWork
        };

        private readonly ILogger<GitHubWebHookController> _logger;
        private readonly IssueChangedNotificationService _notificationService;

        public GitHubWebHookController(ILogger<GitHubWebHookController> logger,
                                       IssueChangedNotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
        }

        [HttpPost]
        public async Task<IActionResult> Post()
        {
            if (Request.ContentType != MediaTypeNames.Application.Json)
                return BadRequest();

            // Get payload
            string payload;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                payload = await reader.ReadToEndAsync();

            WebHookPayload typedPayload;

            try
            {
                typedPayload = JsonSerializer.Deserialize<WebHookPayload>(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't deserialize GitHub web hook: {error}", ex.Message);
                return BadRequest();
            }

            var isRelevant = IsRelevant(typedPayload);
            var payloadResult = new { IsRelevant = isRelevant, Payload = payload };
            _logger.LogInformation("Processed GitHub web hook: {payloadResult}", payloadResult);

            if (isRelevant)
                _notificationService.Notify();

            return Ok();
        }

        private static bool IsRelevant(WebHookPayload payload)
        {
            if (payload.action == null || !_relevantActions.Contains(payload.action))
                return false;

            return IsRelevant(payload.label) ||
                   IsRelevant(payload.issue) ||
                   IsRelevant(payload.pull_request);                   
        }

        private static bool IsRelevant(LabelledEntity payload)
        {
            if (payload == null || payload.labels == null)
                return false;

            return IsRelevant(payload.labels);
        }

        private static bool IsRelevant(Label[] payload)
        {
            if (payload == null || payload.Length == 0)
                return false;

            foreach (var label in payload)
                if (IsRelevant(label))
                    return true;

            return false;
        }

        private static bool IsRelevant(Label payload)
        {
            if (payload == null || payload.name == null)
                return false;

            return _relevantLabels.Contains(payload.name);
        }

        private class WebHookPayload
        {
            public string action { get; set; }
            public LabelledEntity issue { get; set; }
            public LabelledEntity pull_request { get; set; }
            public Label label { get; set; }
        }

        private class LabelledEntity
        {
            public Label[] labels { get; set; }
        }

        private class Label
        {
            public string name { get; set; }
        }
    }
}

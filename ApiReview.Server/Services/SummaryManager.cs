using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ApiReview.Shared;

namespace ApiReview.Server.Services
{
    public class SummaryManager
    {
        private readonly IYouTubeManager _youTubeManager;
        private readonly IGitHubManager _gitHubManager;

        public SummaryManager(IYouTubeManager youTubeManager, IGitHubManager gitHubManager)
        {
            _youTubeManager = youTubeManager;
            _gitHubManager = gitHubManager;
        }

        public async Task<ApiReviewSummary> GetSummaryAsync(DateTimeOffset start, DateTimeOffset end)
        {
            var items = await _gitHubManager.GetFeedbackAsync(start, end);
            return CreateSummary(null, items);
        }

        public async Task<ApiReviewSummary> GetSummaryAsync(string videoId)
        {
            var video = await _youTubeManager.GetVideoAsync(videoId);
            if (video == null)
                return null;

            var start = video.StartDateTime;
            var end = video.EndDateTime;
            var items = await _gitHubManager.GetFeedbackAsync(start, end);
            return CreateSummary(video, items);
        }

        private static ApiReviewSummary CreateSummary(ApiReviewVideo video, IReadOnlyList<ApiReviewItem> items)
        {
            if (items.Count == 0)
            {
                return new ApiReviewSummary
                {
                    Video = video,
                    Items = Array.Empty<ApiReviewItem>()
                };
            }
            else
            {
                var reviewStart = video == null
                                    ? items.OrderBy(i => i.FeedbackDateTime).Select(i => i.FeedbackDateTime).First()
                                    : video.StartDateTime;

                var reviewEnd = video == null
                                    ? items.OrderBy(i => i.FeedbackDateTime).Select(i => i.FeedbackDateTime).Last()
                                    : video.EndDateTime.AddMinutes(15);

                for (var i = 0; i < items.Count; i++)
                {
                    var current = items[i];

                    if (video != null)
                    {
                        var wasDuringReview = reviewStart <= current.FeedbackDateTime && current.FeedbackDateTime <= reviewEnd;
                        if (!wasDuringReview)
                            continue;
                    }

                    var previous = i == 0 ? null : items[i - 1];

                    TimeSpan timeCode;

                    if (previous == null || video == null)
                    {
                        timeCode = TimeSpan.Zero;
                    }
                    else
                    {
                        timeCode = (previous.FeedbackDateTime - video.StartDateTime).Add(TimeSpan.FromSeconds(10));
                        var videoDuration = video.EndDateTime - video.StartDateTime;
                        if (timeCode >= videoDuration)
                            timeCode = items[i - 1].TimeCode;
                    }

                    current.TimeCode = timeCode;
                }

                return new ApiReviewSummary
                {
                    Video = video,
                    Items = items
                };
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace ApiReview.Shared
{
    public sealed class ApiReviewSummary
    {
        public ApiReviewVideo Video { get; set; }
        public IReadOnlyList<ApiReviewItem> Items { get; set; }

        public string GetVideoUrl(TimeSpan timeCode)
        {
            if (Video == null)
                return null;

            var timeCodeText = $"{timeCode.Hours}h{timeCode.Minutes}m{timeCode.Seconds}s";
            return $"https://www.youtube.com/watch?v={Video.Id}&t={timeCodeText}";
        }
    }
}

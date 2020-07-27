using System.Collections.Generic;

using ApiReview.Shared;

namespace ApiReview.Client.Services
{
    public sealed class PagedReviews
    {
        public IReadOnlyList<ApiReviewSummary> Items { get; set; }
        public int PageIndex { get; set; }
        public int PageCount { get; set; }
        public int TotalItemCount { get; set; }
    }
}

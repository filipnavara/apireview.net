using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

using ApiReview.Client.Services;
using ApiReview.Shared;

namespace ApiReview.Client.Pages
{
    [Authorize]
    public partial class ReviewDetails
    {
        [Inject]
        public ReviewService ReviewService { get; set; }

        [Parameter]
        public int ReviewId { get; set; }

        private ApiReviewSummary _summary;

        protected override async Task OnParametersSetAsync()
        {
            _summary = await ReviewService.GetById(ReviewId);
        }
    }
}

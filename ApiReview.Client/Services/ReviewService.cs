using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ApiReview.Shared;

namespace ApiReview.Client.Services
{
    internal sealed class Review
    {
        public int Id { get; set; }
        public DateTimeOffset Date { get; set; }
        public bool IsCompleted { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public IReadOnlyList<ApiReviewIssue> ScheduledIssues { get; set; }
        public ApiReviewSummary Summary { get; set; }
    }

    internal sealed class ReviewService
    {
        private readonly IssueService _issueService;
        private readonly NotesService _notesService;
        private List<Review> _reviews;

        public ReviewService(IssueService issueService, NotesService notesService)
        {
            _issueService = issueService;
            _notesService = notesService;
        }

        private async Task EnsureReviewsLoadedAsync()
        {
            if (_reviews != null)
                return;

            var issues = await _issueService.GetAsync();
            var start = new DateTimeOffset(new DateTime(2020, 6, 25));
            var end = start.AddHours(23).AddMinutes(59);
            var summary = await _notesService.IssuesForRange(start, end);

            var reviews = new[]
            {
                new Review
                {
                    Id = 1,
                    IsCompleted = true,
                    Date = start.AddHours(10),
                    Title = "GitHub Quick Reviews",
                    Description = "We're looking at some issues in our backlog.",
                    ScheduledIssues = issues.Take(3).ToArray(),
                    Summary = summary
                },
                new Review
                {
                    Id = 2,
                    Date = DateTime.Now.Date.AddDays(1).AddHours(10),
                    Title = "GitHub Quick Reviews",
                    Description = "We're looking at some issues in our backlog.",
                    ScheduledIssues = issues.Take(3).ToArray(),
                },
                new Review
                {
                    Id = 3,
                    Date = DateTime.Now.Date.AddDays(3).AddHours(10),
                    Title = "GitHub Quick Reviews",
                    Description = "We're looking at some issues in our backlog.",
                    ScheduledIssues = issues.Skip(3).Take(4).ToArray(),
                },
                new Review
                {
                    Id = 4,
                    Date = DateTime.Now.Date.AddDays(4).AddHours(10),
                    Title = "GitHub Quick Reviews",
                    Description = "We're looking at some issues in our backlog.",
                    ScheduledIssues = issues.Skip(7).Take(2).ToArray(),
                }
            };

            _reviews = reviews.ToList();
        }

        public async Task<IReadOnlyList<Review>> GetAsync()
        {
            await EnsureReviewsLoadedAsync();
            return _reviews;
        }

        public async Task<Review> GetByIdAsync(int id)
        {
            await EnsureReviewsLoadedAsync();
            return _reviews.SingleOrDefault(r => r.Id == id);
        }

        public async Task<Review> CreateAsync(Review review)
        {
            await EnsureReviewsLoadedAsync();
            review.Id = _reviews.Select(r => r.Id).DefaultIfEmpty().Max() + 1;
            _reviews.Add(review);
            return review;
        }

        public async Task<Review> DeleteAsync(int id)
        {
            var review = await GetByIdAsync(id);
            if (review != null)
                _reviews.Remove(review);

            return review;
        }
    }
}

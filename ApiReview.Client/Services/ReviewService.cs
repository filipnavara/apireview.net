using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Shared;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiReview.Client.Services
{
    public sealed class ReviewService
    {
        private readonly IReadOnlyList<ApiReviewSummary> _summaries;

        public ReviewService(IOptions<JsonOptions> jsonOptions)
        {
            var path = @"P:\ApiReviewWeb\RepoIndexer\Data\reviews.json";
            var json = File.ReadAllText(path);
            var options = jsonOptions.Value.JsonSerializerOptions;
            _summaries = JsonSerializer.Deserialize<IReadOnlyList<ApiReviewSummary>>(json, options);
        }

        public Task<ApiReviewSummary> GetById(int id)
        {
            var result = _summaries.FirstOrDefault(s => s.Id == id);
            return Task.FromResult(result);
        }

        public Task<PagedReviews> GetRecentAsync(int pageIndex = 0)
        {
            var items = _summaries.OrderByDescending(s => s.Date).ToArray();
            var result = Page(items, pageIndex);
            return Task.FromResult(result);
        }

        public Task<PagedReviews> SearchAsync(string filter, int pageIndex = 0)
        {
            var item = _summaries.OrderByDescending(s => s.Date)
                                 .Where(s => FilterApplies(s, filter))
                                 .ToArray();
            var result = Page(item, pageIndex);
            return Task.FromResult(result);
        }

        private static bool FilterApplies(ApiReviewSummary summary, string filter)
        {
            if (summary.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var item in summary.Items)
            {
                if (item.Issue.IdFull.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (item.Issue.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    return true;

                foreach (var api in item.Apis)
                {
                    if (api.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static PagedReviews Page(IReadOnlyList<ApiReviewSummary> items, int pageIndex)
        {
            const int pageSize = 25;
            var pageCount = items.Count / pageSize;
            if (pageSize * pageCount < items.Count)
                pageCount++;

            if (pageIndex < 0)
                pageIndex = 0;
            if (pageIndex >= pageCount)
                pageIndex = pageCount - 1;

            var skip = pageIndex * pageSize;
            var pageItems = items.Skip(skip)
                                 .Take(pageSize)
                                 .ToArray();
            var result = new PagedReviews
            {
                Items = pageItems,
                PageIndex = pageIndex,
                PageCount = pageCount,
                TotalItemCount = items.Count
            };
            return result;
        }
    }
}

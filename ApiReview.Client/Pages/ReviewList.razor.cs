using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

using ApiReview.Client.Services;
using Microsoft.AspNetCore.WebUtilities;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components.Routing;
using Octokit.GraphQL.Model;
using System.Web;

namespace ApiReview.Client.Pages
{
    [Authorize]
    public partial class ReviewList : IDisposable
    {
        [Inject]
        public ReviewService ReviewService { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        public string Filter
        {
            get => _filter;
            set
            {
                _filter = value;
                var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
                var uriBuilder = new UriBuilder(uri);
                if (string.IsNullOrEmpty(_filter))
                    uriBuilder.Query = "";
                else
                    uriBuilder.Query = "?q=" + HttpUtility.UrlEncode(_filter);
                NavigationManager.NavigateTo(uriBuilder.ToString());
            }
        }

        private PagedReviews _currentPage;
        private string _filter;

        protected override async Task OnInitializedAsync()
        {
            await LoadPage();
            NavigationManager.LocationChanged += NavigationManager_LocationChanged;
        }

        private async Task LoadPage()
        {
            var pageIndex = 0;
            var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);

            var queryParameters = QueryHelpers.ParseQuery(uri.Query);

            if (queryParameters.TryGetValue("page", out var pageIndexText))
                pageIndex = Convert.ToInt32(pageIndexText) - 1;

            if (queryParameters.TryGetValue("q", out var filter))
                _filter = filter.ToString();
            else
                _filter = null;

            if (string.IsNullOrEmpty(_filter))
                _currentPage = await ReviewService.GetRecentAsync(pageIndex);
            else
                _currentPage = await ReviewService.SearchAsync(_filter, pageIndex);
        }

        public void Dispose()
        {
            NavigationManager.LocationChanged -= NavigationManager_LocationChanged;
        }

        private async void NavigationManager_LocationChanged(object sender, LocationChangedEventArgs e)
        {
            await LoadPage();
            StateHasChanged();
        }

        private string GetUriForPage(int pageIndex)
        {
            var pageNumber = pageIndex + 1;
            var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
            var queryParameters = HttpUtility.ParseQueryString(uri.Query);
            if (pageIndex == 0)
                queryParameters.Remove("page");
            else
                queryParameters.Set("page", pageNumber.ToString());

            var uriBuilder = new UriBuilder(uri);
            uriBuilder.Query = queryParameters.ToString();
            return uriBuilder.ToString();
        }
    }
}

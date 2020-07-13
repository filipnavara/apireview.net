using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

using Octokit;
using GraphConnection = Octokit.GraphQL.Connection;
using GraphProductHeaderValue = Octokit.GraphQL.ProductHeaderValue;

namespace ApiReview.Server.Services
{
    public sealed class GitHubClientFactory
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GitHubClientFactory(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<GitHubClient> CreateAsync()
        {
            var accessToken = await _httpContextAccessor.HttpContext.GetTokenAsync("access_token");
            // TODO: Extract to config
            var productInformation = new ProductHeaderValue("apireviews.azurewebsites.net");
            var client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(accessToken)
            };
            return client;
        }

        public async Task<GraphConnection> CreateGraphAsync()
        {
            var accessToken = await _httpContextAccessor.HttpContext.GetTokenAsync("access_token");
            // TODO: Extract to config
            var productInformation = new GraphProductHeaderValue("apireviews.azurewebsites.net");
            var client = new GraphConnection(productInformation, accessToken);
            return client;
        }
    }
}

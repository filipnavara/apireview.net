using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components.Authorization;

using ApiReview.Shared;
using Microsoft.Net.Http.Headers;

namespace ApiReview.Client.Services
{
    public class BackendHttpClientFactory
    {
        private readonly string _url;

        public BackendHttpClientFactory(IConfiguration configuration,
                                        IOptions<JsonOptions> jsonOptions)
        {
            _url = configuration["apireview-api-url"];
            JsonOptions = jsonOptions.Value.JsonSerializerOptions;
        }

        public JsonSerializerOptions JsonOptions { get; }

        public virtual Task<HttpClient> CreateAsync()
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(_url),
            };

            return Task.FromResult(client);
        }
    }

    public sealed class AuthenticatedBackendHttpClientFactory : BackendHttpClientFactory
    {
        private readonly AuthenticationStateProvider _provider;

        public AuthenticatedBackendHttpClientFactory(IConfiguration configuration,
                                                     IOptions<JsonOptions> jsonOptions,
                                                     AuthenticationStateProvider provider)
            : base(configuration, jsonOptions)
        {
            _provider = provider;
        }

        public override async Task<HttpClient> CreateAsync()
        {
            var client = await base.CreateAsync();
            var state = await _provider.GetAuthenticationStateAsync();
            var token = state.User.FindFirst(ApiReviewConstants.TokenClaim)?.Value;

            if (token != null)
                client.DefaultRequestHeaders.Add(HeaderNames.Authorization, "Bearer " + token);

            return client;
        }
    }
}

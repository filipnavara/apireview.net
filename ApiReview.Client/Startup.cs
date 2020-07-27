using System;
using System.Security.Claims;

using ApiReview.Client.Services;
using ApiReview.Shared;

using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace ApiReview.Client
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            Env = env;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Env { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new TimeSpanJsonConverter());
            });
            services.AddApplicationInsightsTelemetry(Configuration["APPINSIGHTS_INSTRUMENTATIONKEY"]);
            services.AddServerSideBlazor();
            services.AddControllers();
            services.AddHostedService<IssueCacheWarmUp>();
            services.AddSingleton<IssueService>();
            services.AddSingleton<ReviewService>();
            services.AddSingleton<BackendHttpClientFactory>();
            services.AddScoped<AuthenticatedBackendHttpClientFactory>();
            services.AddScoped<NotesService>();
            services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    })
                    .AddCookie(options =>
                    {
                        options.LoginPath = "/signin";
                        options.LogoutPath = "/signout";
                    })
                    .AddGitHub(options =>
                    {
                        options.ClientId = Configuration["GitHubClientId"];
                        options.ClientSecret = Configuration["GitHubClientSecret"];
                        options.ClaimActions.MapJsonKey(ApiReviewConstants.GitHubAvatarUrl, ApiReviewConstants.GitHubAvatarUrl);
                        options.Events.OnCreatingTicket = async context =>
                        {
                            var accessToken = context.AccessToken;
                            var orgName = ApiReviewConstants.ApiApproverOrgName;
                            var teamName = ApiReviewConstants.ApiApproverTeamSlug;
                            var userName = context.Identity.Name;
                            var isMember = await GitHubAuthHelpers.IsMemberOfTeamAsync(accessToken, orgName, teamName, userName);
                            if (isMember)
                                context.Identity.AddClaim(new Claim(context.Identity.RoleClaimType, ApiReviewConstants.ApiApproverRole));

                            context.Identity.AddClaim(new Claim(ApiReviewConstants.TokenClaim, accessToken));
                        };
                    });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Redirect to apireview.net

            app.Use(async (context, next) =>
            {
                const string oldHost = "apireviews.azurewebsites.net";
                const string newHost = "apireview.net";
                var url = context.Request.GetUri();                
                if (url.Host.Equals(oldHost, StringComparison.OrdinalIgnoreCase))
                {
                    var response = context.Response;
                    response.StatusCode = StatusCodes.Status301MovedPermanently;

                    var newUrl = new UriBuilder(url)
                    {
                        Host = newHost
                    }.ToString();

                    response.Headers[HeaderNames.Location] = newUrl;
                    return;
                }

                await next();
            });

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapDefaultControllerRoute();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}

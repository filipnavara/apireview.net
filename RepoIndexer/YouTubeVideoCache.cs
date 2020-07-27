using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using ApiReview.Shared;

using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace RepoIndexer
{
    internal static class YouTubeVideoCache
    {
        public static async Task<IReadOnlyList<ApiReviewVideo>> LoadAsync(string path, string youTubeKey)
        {
            if (!File.Exists(path))
                await DownloadYouTubeVideosAsync(path, youTubeKey);

            return await LoadVideosAsync(path);
        }

        private static async Task DownloadYouTubeVideosAsync(string outputPath, string youTubeKey)
        {
            var initializer = new BaseClientService.Initializer
            {
                ApiKey = youTubeKey
            };

            var service = new YouTubeService(initializer);

            var result = new List<Video>();
            var nextPageToken = "";

            var listPlaylistRequest = service.PlaylistItems.List("snippet,contentDetails");
            listPlaylistRequest.PlaylistId = "PL1rZQsJPBU2S49OQPjupSJF-qeIEz9_ju";
            listPlaylistRequest.MaxResults = 100;

            while (nextPageToken != null)
            {
                listPlaylistRequest.PageToken = nextPageToken;
                var response = await listPlaylistRequest.ExecuteAsync();

                var ids = response.Items.Select(i => i.ContentDetails.VideoId);
                var idString = string.Join(",", ids);

                var videoRequest = service.Videos.List("snippet,liveStreamingDetails");
                videoRequest.Id = idString;
                var videoResponse = await videoRequest.ExecuteAsync();
                result.AddRange(videoResponse.Items);

                nextPageToken = response.NextPageToken;
            }

            var videos = result.Where(v => v.LiveStreamingDetails != null &&
                                           v.LiveStreamingDetails.ActualStartTime != null &&
                                           v.LiveStreamingDetails.ActualEndTime != null)
                               .Select(CreateVideo)
                               .ToList();

            AddHardcodedItems(videos);

            videos = videos.OrderBy(v => v.StartDateTime).ToList();

            using (var stream = File.Create(outputPath))
                await JsonSerializer.SerializeAsync(stream, videos, MyJsonSerializerOptions.Instance);
        }

        private static void AddHardcodedItems(List<ApiReviewVideo> videos)
        {
            DateTimeOffset start;
            TimeSpan duration;
            DateTimeOffset end;

            start = new DateTime(2020, 1, 7, 10, 4, 0);
            duration = new TimeSpan(1, 57, 31);
            end = start + duration;
            videos.Add(new ApiReviewVideo("lSB-ACeetRo", start, end, ".NET Design Reviews: GitHub Quick Reviews", "https://i.ytimg.com/vi/lSB-ACeetRo/mqdefault.jpg"));

            start = new DateTime(2020, 1, 14, 10, 4, 0);
            duration = new TimeSpan(1, 53, 44);
            end = start + duration;
            videos.Add(new ApiReviewVideo("dJLmN6u98Z4", start, end, ".NET Design Reviews: GitHub Quick Reviews", "https://i.ytimg.com/vi/dJLmN6u98Z4/mqdefault.jpg"));
        }

        private static ApiReviewVideo CreateVideo(Video v)
        {
            return new ApiReviewVideo(v.Id,
                                      v.LiveStreamingDetails.ActualStartTime!.Value,
                                      v.LiveStreamingDetails.ActualEndTime!.Value,
                                      v.Snippet.Title,
                                      v.Snippet.Thumbnails?.Medium?.Url);
        }

        private static async Task<IReadOnlyList<ApiReviewVideo>> LoadVideosAsync(string path)
        {
            using (var stream = File.OpenRead(path))
                return await JsonSerializer.DeserializeAsync<IReadOnlyList<ApiReviewVideo>>(stream);
        }
    }
}

using System.Text.Json;

using ApiReviewDotNet.Data;

using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace RepoIndexer;

internal sealed class YouTubeVideoCache
{
    private readonly string _path;
    private readonly string _youTubeKey;
    private readonly Dictionary<string, ApiReviewVideo> _videos = new(StringComparer.OrdinalIgnoreCase);

    private YouTubeVideoCache(string path, string youTubeKey)
    {
        _path = path;
        _youTubeKey = youTubeKey;
    }

    public static async Task<YouTubeVideoCache> CreateAsync(string path, string youTubeKey)
    {
        var result = new YouTubeVideoCache(path, youTubeKey);
        await result.InitializeAsync();

        return result;
    }

    private async Task InitializeAsync()
    {
        var cachedVideos = await LoadCachedAsync(_path);

        foreach (var video in cachedVideos)
            _videos.Add(video.Id, video);
    }

    private static async Task<IReadOnlyList<ApiReviewVideo>> LoadCachedAsync(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<ApiReviewVideo>();

        using (var stream = File.OpenRead(path))
            return await JsonSerializer.DeserializeAsync<IReadOnlyList<ApiReviewVideo>>(stream) ?? Array.Empty<ApiReviewVideo>();
    }

    public async Task<ApiReviewVideo?> LoadVideoAsync(string? videoId)
    {
        if (videoId is null)
            return null;

        if (_videos.TryGetValue(videoId, out var result))
            return result;

        var initializer = new BaseClientService.Initializer
        {
            ApiKey = _youTubeKey
        };

        var service = new YouTubeService(initializer);

        var videoRequest = service.Videos.List("snippet,liveStreamingDetails");
        videoRequest.Id = videoId;

        var videoResponse = await videoRequest.ExecuteAsync();

        result = videoResponse.Items.Where(v => v.LiveStreamingDetails != null &&
                                                v.LiveStreamingDetails.ActualStartTime != null &&
                                                v.LiveStreamingDetails.ActualEndTime != null)
                                    .Select(CreateVideo)
                                    .FirstOrDefault();

        if (result is not null)
        {
            _videos.Add(result.Id, result);

            var allVideos = _videos.Values.OrderBy(v => v.StartDateTime)
                                          .ToArray();

            using (var stream = File.Create(_path))
                await JsonSerializer.SerializeAsync(stream, allVideos, MyJsonSerializerOptions.Instance);
        }

        return result;
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
}

﻿using System.Text.Json.Serialization;

namespace ApiReviewDotNet.Data;

public sealed class ApiReviewVideo
{
    public ApiReviewVideo(string id, DateTimeOffset startDateTime, DateTimeOffset endDateTime, string title, string? thumbnailUrl)
    {
        Id = id;
        StartDateTime = startDateTime;
        EndDateTime = endDateTime;
        Title = title;
        ThumbnailUrl = thumbnailUrl;
    }

    [JsonIgnore]
    public string Url => $"https://www.youtube.com/watch?v={Id}";
    public string Id { get; }
    public DateTimeOffset StartDateTime { get; }
    public DateTimeOffset EndDateTime { get; }
    [JsonIgnore]
    public TimeSpan Duration => EndDateTime - StartDateTime;
    public string Title { get; }
    public string? ThumbnailUrl { get; }
}

namespace Tubester.Domain;

public sealed record GeoLocation(double Latitude, double Longitude);

public enum VideoVisibility
{
    Public = 0,
    Unlisted = 1,
    Private = 2,
    Scheduled = 3
}

[Flags]
public enum AiVideoOperationFlags
{
    None = 0,
    Title = 1,
    Description = 2,
    Tags = 4,
    PlaylistSuggestion = 8
}

public sealed class Video : Entity
{
    public string UploadsPlaylistId { get; private set; } = default!;
    public string VideoId { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string[] Tags { get; private set; } = [];
    public TimeSpan Duration { get; private set; }
    public VideoVisibility Visibility { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public string? CategoryId { get; private set; }
    public string? DefaultLanguage { get; private set; }
    public string? DefaultAudioLanguage { get; private set; }
    public GeoLocation? Location { get; private set; }
    public string? LocationDescription { get; private set; }
    public string? ETag { get; private set; }
    public bool? CommentsAllowed { get; private set; }
    public DateTimeOffset CachedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    
    // Bitmask for AI operations in progress
    public AiVideoOperationFlags AiOperationsInProgress { get; private set; }
    
    // Computed compatibility properties for API/DTO exposure
    public bool IsAiTitleInProgress => AiOperationsInProgress.HasFlag(AiVideoOperationFlags.Title);
    public bool IsAiDescriptionInProgress => AiOperationsInProgress.HasFlag(AiVideoOperationFlags.Description);
    public bool IsAiTagsInProgress => AiOperationsInProgress.HasFlag(AiVideoOperationFlags.Tags);
    public bool IsAiPlaylistSuggestionInProgress => AiOperationsInProgress.HasFlag(AiVideoOperationFlags.PlaylistSuggestion);

    public bool IsShort => Duration <= TimeSpan.FromSeconds(60);

    public string Url => $"https://www.youtube.com/watch?v={VideoId}";
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{VideoId}/sddefault.jpg";

    public static Video Create(
        string uploadsPlaylistId,
        string videoId,
        string? title,
        string? description,
        DateTimeOffset publishedAt,
        TimeSpan duration,
        VideoVisibility visibility,
        IEnumerable<string>? tags,
        string? categoryId,
        string? defaultLanguage,
        string? defaultAudioLanguage,
        GeoLocation? location,
        string? locationDescription,
        DateTimeOffset nowUtc,
        string? etag = null,
        bool? commentsAllowed = null)
    {
        return new Video
        {
            UploadsPlaylistId = uploadsPlaylistId,
            VideoId = videoId,
            Title = title,
            Description = description,
            PublishedAt = publishedAt,
            Duration = duration,
            Visibility = visibility,
            Tags = (tags ?? Array.Empty<string>()).ToArray(),
            CategoryId = categoryId,
            DefaultLanguage = defaultLanguage,
            DefaultAudioLanguage = defaultAudioLanguage,
            Location = location,
            LocationDescription = locationDescription,
            ETag = etag,
            CommentsAllowed = commentsAllowed,
            CachedAt = nowUtc,
            UpdatedAt = nowUtc,
            AiOperationsInProgress = AiVideoOperationFlags.None
        };
    }

    public bool Synchronize(string? title,
        string? description,
        DateTimeOffset publishedAt,
        TimeSpan duration,
        VideoVisibility visibility,
        IEnumerable<string>? tags,
        string? categoryId,
        string? defaultLanguage,
        string? defaultAudioLanguage,
        DateTimeOffset nowUtc,
        string? etag,
        bool? commentsAllowed = null)
    {
        CachedAt = nowUtc;
        return ApplyDetails(title, description, publishedAt, duration, visibility, tags, categoryId, defaultLanguage, defaultAudioLanguage, nowUtc, etag, commentsAllowed);
    }

    public bool ApplyDetails(
        string? title,
        string? description,
        DateTimeOffset publishedAt,
        TimeSpan duration,
        VideoVisibility visibility,
        IEnumerable<string>? tags,
        string? categoryId,
        string? defaultLanguage,
        string? defaultAudioLanguage,
        DateTimeOffset nowUtc,
        string? etag,
        bool? commentsAllowed = null)
    {
        // ---------- ETag fast-path ----------
        // If caller fetched with the same `part` set and the ETag is unchanged,
        // the video metadata we care about hasn't changed.
        if (!string.IsNullOrEmpty(etag) && StringComparer.Ordinal.Equals(ETag, etag))
        {
            var dirtyFast = false;

            // We still allow updates to CommentsAllowed (since you may probe it separately),
            // without touching other fields.
            if (CommentsAllowed != commentsAllowed)
            {
                CommentsAllowed = commentsAllowed;
                dirtyFast = true;
            }

            CachedAt = nowUtc;
            if (dirtyFast)
            {
                UpdatedAt = nowUtc;
            }

            return dirtyFast;
        }

        // ---------- Full field-by-field comparison ----------
        var dirty = false;

        if (!StringComparer.Ordinal.Equals(Title, title))
        {
            Title = title;
            dirty = true;
        }

        if (!StringComparer.Ordinal.Equals(Description, description))
        {
            Description = description;
            dirty = true;
        }

        if (PublishedAt != publishedAt)
        {
            PublishedAt = publishedAt;
            dirty = true;
        }

        if (Duration != duration)
        {
            Duration = duration;
            dirty = true;
        }

        if (Visibility != visibility)
        {
            Visibility = visibility;
            dirty = true;
        }

        if (tags is not null)
        {
            var newTags = tags.ToArray();
            if (!Tags.SequenceEqual(newTags, StringComparer.Ordinal))
            {
                Tags = newTags;
                dirty = true;
            }
        }

        if (!StringComparer.Ordinal.Equals(CategoryId, categoryId))
        {
            CategoryId = categoryId;
            dirty = true;
        }

        if (!StringComparer.Ordinal.Equals(DefaultLanguage, defaultLanguage))
        {
            DefaultLanguage = defaultLanguage;
            dirty = true;
        }

        if (!StringComparer.Ordinal.Equals(DefaultAudioLanguage, defaultAudioLanguage))
        {
            DefaultAudioLanguage = defaultAudioLanguage;
            dirty = true;
        }

        if (!StringComparer.Ordinal.Equals(ETag, etag))
        {
            ETag = etag;
            dirty = true;
        }

        if (CommentsAllowed != commentsAllowed)
        {
            CommentsAllowed = commentsAllowed;
            dirty = true;
        }
        
        if (dirty)
        {
            UpdatedAt = nowUtc;
        }

        return dirty;
    }
    
    private Video()
    {
    }
}
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
    public string? ETag { get; private set; }
    public bool? CommentsAllowed { get; private set; }
    public DateTimeOffset CachedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    // Indicates if video metadata has been modified by AI or draft operations
    public bool IsDirty { get; private set; }

    // Bitmask for AI operations in progress
    public AiVideoOperationFlags AiOperationsInProgress { get; private set; }

    public bool MarkAsDirty(DateTimeOffset nowUtc)
    {
        if (IsDirty)
        {
            return false;
        }

        IsDirty = true;
        UpdatedAt = nowUtc;
        return true;
    }

    public void MarkAsClean(DateTimeOffset nowUtc)
    {
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        UpdatedAt = nowUtc;
    }

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
            ETag = etag,
            CommentsAllowed = commentsAllowed,
            CachedAt = nowUtc,
            UpdatedAt = nowUtc,
            AiOperationsInProgress = AiVideoOperationFlags.None
        };
    }

    public bool ApplyLocalChanges(
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
        var changed = ApplyDetailsCore(
            title,
            description,
            publishedAt,
            duration,
            visibility,
            tags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            nowUtc,
            etag,
            commentsAllowed);

        if (changed)
        {
            IsDirty = true;
        }

        return changed;
    }

    public bool OverrideFromRemote(
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
        var wasDirty = IsDirty;
        CachedAt = nowUtc;
        var changed = ApplyDetailsCore(
            title,
            description,
            publishedAt,
            duration,
            visibility,
            tags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            nowUtc,
            etag,
            commentsAllowed);

        if (wasDirty)
        {
            IsDirty = false;
            UpdatedAt = nowUtc;
        }

        return changed || wasDirty;
    }

    public bool SyncFromRemote(
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
        if (HasSameEtag(etag))
        {
            CachedAt = nowUtc;
            return false;
        }

        return ApplyDetailsCore(
            title,
            description,
            publishedAt,
            duration,
            visibility,
            tags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            nowUtc,
            etag,
            commentsAllowed);
    }

    private bool HasSameEtag(string? etag)
    {
        return !string.IsNullOrEmpty(etag)
               && StringComparer.Ordinal.Equals(ETag, etag);
    }

    private bool ApplyDetailsCore(
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
        

        var newTags = (tags ?? []).ToArray();

        if (!Tags.SequenceEqual(newTags, StringComparer.Ordinal))
        {
            Tags = newTags;
            dirty = true;
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
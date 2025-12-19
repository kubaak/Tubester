using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Playlists;
using YouTubester.Abstractions.Videos;
using YouTubester.Domain;
using YouTubester.Integration;

namespace YouTubester.Application;

public sealed class VideoTemplatingService(
    IYouTubeIntegration youTubeIntegration,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    ICurrentChannelContext channelContext,
    IPlaylistRepository playlistRepository)
    : IVideoTemplatingService
{
    public async Task<CopyVideoTemplateResult> CopyTemplateAsync(
        string userId,
        CopyVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var channelId = channelContext.GetRequiredChannelId();

        // Load source and target videos from DB
        var sourceVideo = await videoRepository.GetVideoByIdAsync(channelId, request.SourceVideoId, cancellationToken)
                          ?? throw new ArgumentException($"Source video {request.SourceVideoId} not found in cache.");

        var targetVideo = await videoRepository.GetVideoByIdAsync(channelId, request.TargetVideoId, cancellationToken)
                          ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

        // Build effective metadata starting from source
        var newTitle = sourceVideo.Title ?? string.Empty;
        var newDescription = sourceVideo.Description ?? string.Empty;
        var newTags = request.CopyTags ? SanitizeTags(sourceVideo.Tags) : targetVideo.Tags;
        var location = request.CopyLocation
            ? ConvertToLocationTuple(sourceVideo.Location)
            : ConvertToLocationTuple(targetVideo.Location);
        var locationDescription =
            request.CopyLocation ? sourceVideo.LocationDescription : targetVideo.LocationDescription;
        var categoryId = request.CopyCategory ? sourceVideo.CategoryId : targetVideo.CategoryId;
        var defaultLanguage = request.CopyDefaultLanguages ? sourceVideo.DefaultLanguage : targetVideo.DefaultLanguage;
        var defaultAudioLanguage = request.CopyDefaultLanguages
            ? sourceVideo.DefaultAudioLanguage
            : targetVideo.DefaultAudioLanguage;


        // Update target video on YouTube
        await youTubeIntegration.UpdateVideoAsync(
            request.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            location,
            locationDescription,
            cancellationToken);

        // Persist changes to DB only after successful YouTube update
        var nowUtc = DateTimeOffset.UtcNow;
        targetVideo.ApplyDetails(
            newTitle,
            newDescription,
            targetVideo.PublishedAt,
            targetVideo.Duration,
            targetVideo.Visibility,
            newTags,
            categoryId,
            defaultLanguage,
            defaultAudioLanguage,
            ConvertFromLocationTuple(location),
            locationDescription,
            nowUtc,
            null, //we won't know the etag at from this point 
            targetVideo.CommentsAllowed
        );

        await videoRepository.UpsertAsync(channelId, [targetVideo], cancellationToken);

        if (!request.CopyPlaylists)
        {
            return new CopyVideoTemplateResult(
                request.SourceVideoId,
                request.TargetVideoId,
                newTitle,
                newDescription,
                newTags,
                locationDescription,
                location,
                [],
                request.CopyCategory,
                request.CopyDefaultLanguages
            );
        }

        var playlistIds =
            (await playlistRepository.GetPlaylistIdsByVideoAsync(sourceVideo.VideoId, cancellationToken))
            .ToHashSet();
        foreach (var playlistId in playlistIds)
        {
            await youTubeIntegration.AddVideoToPlaylistAsync(playlistId, targetVideo.VideoId,
                cancellationToken);
        }

        await playlistRepository.SetMembershipsToPlaylistsAsync(targetVideo.VideoId, playlistIds,
            cancellationToken);


        return new CopyVideoTemplateResult(
            request.SourceVideoId,
            request.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            locationDescription,
            location,
            playlistIds.ToArray(),
            request.CopyCategory,
            request.CopyDefaultLanguages
        );
    }

    private static (double lat, double lng)? ConvertToLocationTuple(GeoLocation? location)
    {
        return location is not null
            ? (location.Latitude, location.Longitude)
            : null;
    }

    private static GeoLocation? ConvertFromLocationTuple((double lat, double lng)? location)
    {
        return location.HasValue ? new GeoLocation(location.Value.lat, location.Value.lng) : null;
    }

    private static string[] SanitizeTags(IReadOnlyList<string> tags)
    {
        var cleaned = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        var total = 0;
        foreach (var tag in cleaned)
        {
            var add = tag.Length;
            if (total + add > 500)
            {
                break;
            }

            result.Add(tag);
            total += add;
        }

        return result.ToArray();
    }

    public async Task GenerateAiTemplateAsync(
        string userId,
        AiVideoTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var channelId = channelContext.GetRequiredChannelId();

        var targetVideo = await videoRepository.GetVideoByIdAsync(channelId, request.TargetVideoId, cancellationToken)
                          ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

        var (suggestedTitle, suggestedDescription, suggestedTags) =
            await aiClient.SuggestMetadataAsync(request.PromptEnrichment, cancellationToken);

        var newTitle = request.GenerateTitle ? suggestedTitle : (targetVideo.Title ?? string.Empty);
        var newDescription = request.GenerateDescription ? suggestedDescription : (targetVideo.Description ?? string.Empty);
        var newTags = request.GenerateTags ? SanitizeTags(suggestedTags.ToArray()) : targetVideo.Tags;

        await youTubeIntegration.UpdateVideoAsync(
            request.TargetVideoId,
            newTitle,
            newDescription,
            newTags,
            targetVideo.CategoryId,
            targetVideo.DefaultLanguage,
            targetVideo.DefaultAudioLanguage,
            ConvertToLocationTuple(targetVideo.Location),
            targetVideo.LocationDescription,
            cancellationToken);

        var nowUtc = DateTimeOffset.UtcNow;
        targetVideo.ApplyDetails(
            newTitle,
            newDescription,
            targetVideo.PublishedAt,
            targetVideo.Duration,
            targetVideo.Visibility,
            newTags,
            targetVideo.CategoryId,
            targetVideo.DefaultLanguage,
            targetVideo.DefaultAudioLanguage,
            targetVideo.Location,
            targetVideo.LocationDescription,
            nowUtc,
            null,
            targetVideo.CommentsAllowed);

        await videoRepository.UpsertAsync(channelId, [targetVideo], cancellationToken);
    }
}

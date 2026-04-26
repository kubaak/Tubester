using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Videos;
using Tubester.Application.Contracts.Videos;
using Tubester.Application.Options;
using Tubester.Integration;

namespace Tubester.Application.Videos;

public sealed class AiVideoImprovingService(
    ILogger<AiVideoImprovingService> logger,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    IPlaylistRepository playlistRepository,
    IChannelRepository channelRepository,
    IOptions<PlaylistSuggestionOptions> playlistSuggestionOptions)
    : IAiVideoImprovingService
{
    public async Task GenerateAiTemplateAsync(
    string channelId,
    AiVideoTemplateRequest request,
    CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(channelId));
        }

        logger.LogInformation(
            "Generating AI template for channel {ChannelId}, target video {TargetVideoId}. GenerateTitle: {GenerateTitle}, GenerateDescription: {GenerateDescription}, GenerateTags: {GenerateTags}",
            channelId,
            request.TargetVideoId,
            request.GenerateTitle,
            request.GenerateDescription,
            request.GenerateTags);

        try
        {
            var targetVideo = await videoRepository.GetVideoByIdAsync(channelId, request.TargetVideoId, cancellationToken)
                          ?? throw new ArgumentException($"Target video {request.TargetVideoId} not found in cache.");

            logger.LogDebug(
                "Loaded target video {TargetVideoId}. Current title length: {TitleLength}, description length: {DescriptionLength}, tag count: {TagCount}",
                targetVideo.VideoId,
                targetVideo.Title?.Length ?? 0,
                targetVideo.Description?.Length ?? 0,
                targetVideo.Tags.Length);

            var suggestedMetadata =
                await aiClient.SuggestMetadataAsync(
                    request.PromptEnrichment,
                    request.GenerateTitle,
                    request.GenerateDescription,
                    request.GenerateTags,
                    cancellationToken);

            logger.LogInformation(
                "AI metadata generated for video {TargetVideoId}. Suggested title present: {HasTitle}, suggested description present: {HasDescription}, suggested tag count: {SuggestedTagCount}",
                request.TargetVideoId,
                !string.IsNullOrWhiteSpace(suggestedMetadata.Title),
                !string.IsNullOrWhiteSpace(suggestedMetadata.Description),
                suggestedMetadata.Tags.Count);

            var suggestedTitle = suggestedMetadata.Title;
            var suggestedDescription = suggestedMetadata.Description;
            var suggestedTags = suggestedMetadata.Tags;

            var newTitle = request.GenerateTitle ? suggestedTitle : targetVideo.Title ?? string.Empty;
            var newDescription = request.GenerateDescription
                ? suggestedDescription
                : targetVideo.Description ?? string.Empty;
            var newTags = request.GenerateTags
                ? SanitizeTags(suggestedTags.ToArray())
                : targetVideo.Tags;

            logger.LogDebug(
                "Prepared updated metadata for video {TargetVideoId}. New title length: {TitleLength}, new description length: {DescriptionLength}, new tag count: {TagCount}",
                request.TargetVideoId,
                newTitle?.Length ?? 0,
                newDescription?.Length ?? 0,
                newTags.Length);

            var nowUtc = dateTimeOffsetProvider.GetUtcNowDateTimeOffset();
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
                nowUtc,
                null,
                targetVideo.CommentsAllowed);

            await videoRepository.UpsertAsync(channelId, [targetVideo], cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI templating failed for video {TargetVideoId}", request.TargetVideoId);
            throw;
        }

        logger.LogInformation(
            "AI template generation completed for channel {ChannelId}, video {TargetVideoId}",
            channelId,
            request.TargetVideoId);
    }

    public async Task SuggestPlaylistIdsAsync(string channelId, string targetVideoId,
        string promptEnrichment, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting playlist suggestion for video {TargetVideoId}", targetVideoId);

            var targetVideo = await videoRepository.GetVideoByIdAsync(channelId, targetVideoId, cancellationToken)
                              ?? throw new InvalidOperationException($"Target video {targetVideoId} not found in cache.");

            var playlistCandidates = await playlistRepository.GetPublicByChannelAsync(channelId, cancellationToken);

            if (playlistCandidates.Count == 0)
            {
                logger.LogWarning("No public playlists found for channel {ChannelId}", channelId);
                return;
            }

            var latestVideoPlaylistNames = await playlistRepository.GetPlaylistNamesForLatestPublicVideoAsync(channelId, cancellationToken);
            
            var maxBatchSize = playlistSuggestionOptions.Value.MaxPlaylistsPerBatch;

            logger.LogDebug(
                "Using MaxPlaylistsPerBatch: {MaxBatchSize}, total playlists: {TotalPlaylists}",
                maxBatchSize,
                playlistCandidates.Count);

            var allSuggestedPlaylistIds = new HashSet<string>(StringComparer.Ordinal);
            var batchCount = (int)Math.Ceiling((double)playlistCandidates.Count / maxBatchSize);

            for (var i = 0; i < playlistCandidates.Count; i += maxBatchSize)
            {
                var batch = playlistCandidates.Skip(i).Take(maxBatchSize).ToList();
                var batchIndex = (i / maxBatchSize) + 1;

                logger.LogInformation(
                    "Processing playlist batch {BatchIndex} of {TotalBatches} ({BatchSize} playlists)",
                    batchIndex,
                    batchCount,
                    batch.Count);

                cancellationToken.ThrowIfCancellationRequested();

                var context = new PlaylistSuggestionContext
                {
                    PromptEnrichment = promptEnrichment, LatestPlaylistTitlesUsed = latestVideoPlaylistNames
                };

                var batchSuggestedIds = await aiClient.SuggestPlaylistIdsAsync(
                    context,
                    batch,
                    cancellationToken);

                foreach (var playlistId in batchSuggestedIds)
                {
                    allSuggestedPlaylistIds.Add(playlistId);
                }
            }

            if (allSuggestedPlaylistIds.Count != 0)
            {
                await playlistRepository.SetMembershipsToPlaylistsAsync(
                    targetVideo.VideoId,
                    allSuggestedPlaylistIds,
                    cancellationToken);
                logger.LogInformation(
                    "Assigned video {TargetVideoId} to {PlaylistCount} playlists",
                    targetVideoId,
                    allSuggestedPlaylistIds.Count);
            }
            else
            {
                logger.LogInformation("No playlists suggested for video {TargetVideoId}", targetVideoId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playlist suggestion failed for video {TargetVideoId}", targetVideoId);
            throw;
        }
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
}

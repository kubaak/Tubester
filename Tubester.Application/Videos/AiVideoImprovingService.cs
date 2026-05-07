using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tubester.Abstractions;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Videos;
using Tubester.Application.Common;
using Tubester.Application.Jobs;
using Tubester.Application.Options;
using Tubester.Domain;
using Tubester.Integration;

namespace Tubester.Application.Videos;

public sealed class AiVideoImprovingService(
    ILogger<AiVideoImprovingService> logger,
    IAiClient aiClient,
    IVideoRepository videoRepository,
    IDateTimeOffsetProvider dateTimeOffsetProvider,
    IPlaylistRepository playlistRepository,
    IOptions<PlaylistSuggestionOptions> playlistSuggestionOptions)
    : IAiVideoImprovingService
{
    public async Task GenerateAiTemplateAsync(
        AiVideoDetailsRequest request,
        CancellationToken cancellationToken)
    {
        var channelId = request.ChannelId;
        var uploadPlaylistId = request.UploadPlaylistId;
        var targetVideoId = request.TargetVideoId;

        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadPlaylistId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVideoId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.ChannelId, channelId },
            { LoggingConstants.UploadPlaylistId, uploadPlaylistId },
            { LoggingConstants.VideoId, targetVideoId }
        });

        logger.LogInformation(
            "Generating AI template. GenerateTitle: {GenerateTitle}, GenerateDescription: {GenerateDescription}, GenerateTags: {GenerateTags}",
            request.GenerateTitle,
            request.GenerateDescription,
            request.GenerateTags);

        try
        {
            var targetVideo =
                await videoRepository.GetVideoByIdAsync(uploadPlaylistId, targetVideoId, cancellationToken)
                ?? throw new ArgumentException($"Target video {targetVideoId} not found in cache.");

            logger.LogDebug(
                "Loaded target video. Current title length: {TitleLength}, description length: {DescriptionLength}, tag count: {TagCount}",
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
                "AI metadata generated. Suggested title present: {HasTitle}, suggested description present: {HasDescription}, suggested tag count: {SuggestedTagCount}",
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
                "Prepared updated metadata. New title length: {TitleLength}, new description length: {DescriptionLength}, new tag count: {TagCount}",
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

            await videoRepository.UpsertAsync(uploadPlaylistId, [targetVideo], cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI templating failed");
            throw;
        }
        finally
        {
            await videoRepository.TryClearAiOperationsInProgressAsync(
                uploadPlaylistId,
                targetVideoId,
                AiVideoOperationFlags.Title | AiVideoOperationFlags.Description | AiVideoOperationFlags.Tags,
                cancellationToken);
        }

        logger.LogInformation("AI template generation completed");
    }

    public async Task SuggestPlaylistIdsAsync(
        PlaylistSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var channelId = request.ChannelId;
        var uploadPlaylistId = request.UploadPlaylistId;
        var targetVideoId = request.TargetVideoId;

        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(channelId));
        }

        if (string.IsNullOrWhiteSpace(uploadPlaylistId))
        {
            throw new ArgumentException("Upload playlist id is required.", nameof(uploadPlaylistId));
        }

        if (string.IsNullOrWhiteSpace(targetVideoId))
        {
            throw new ArgumentException("Target video id is required.", nameof(targetVideoId));
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            { LoggingConstants.ChannelId, channelId },
            { LoggingConstants.UploadPlaylistId, uploadPlaylistId },
            { LoggingConstants.VideoId, targetVideoId }
        });

        try
        {
            logger.LogInformation("Starting playlist suggestion");

            var targetVideo =
                await videoRepository.GetVideoByIdAsync(uploadPlaylistId, targetVideoId, cancellationToken)
                ?? throw new InvalidOperationException($"Target video {targetVideoId} not found in cache.");

            var playlistCandidates = await playlistRepository.GetPublicByChannelAsync(channelId, cancellationToken);

            if (playlistCandidates.Count == 0)
            {
                logger.LogWarning("No public playlists found");
                return;
            }

            var latestVideoPlaylistNames =
                await playlistRepository.GetPlaylistNamesForLatestPublicVideoAsync(uploadPlaylistId, cancellationToken);

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
                    PromptEnrichment = request.PromptEnrichment,
                    LatestPlaylistTitlesUsed = latestVideoPlaylistNames
                };

                var batchSuggestedIds = (await aiClient.SuggestPlaylistIdsAsync(
                    context,
                    batch,
                    cancellationToken)).ToList();

                logger.LogDebug(
                    "Suggested playlist ids: {SuggestedPlaylistIds} in batch {BatchIndex}",
                    string.Join(", ", batchSuggestedIds),
                    batchIndex);

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
                    "Assigned video to {PlaylistCount} playlists",
                    allSuggestedPlaylistIds.Count);
            }
            else
            {
                logger.LogInformation("No playlists suggested");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playlist suggestion failed");
            throw;
        }
        finally
        {
            await videoRepository.TryClearAiOperationsInProgressAsync(
                uploadPlaylistId,
                targetVideoId,
                AiVideoOperationFlags.PlaylistSuggestion,
                cancellationToken);
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
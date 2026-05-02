namespace Tubester.Abstractions.Credits;

/// <summary>
/// Represents billable actions that consume credits.
/// </summary>
public enum CreditActionType
{
    /// <summary>
    /// Copying template metadata between videos.
    /// </summary>
    CopyTemplateExecuted = 0,

    /// <summary>
    /// Enqueuing an AI template generation job.
    /// </summary>
    AiTemplateEnqueued = 1,

    /// <summary>
    /// Submitting AI-generated template changes to YouTube.
    /// </summary>
    AiTemplateSubmitted = 2,

    /// <summary>
    /// Generating an AI reply for a comment (background job).
    /// </summary>
    AiReplyGenerated = 3,

    /// <summary>
    /// Posting a reply to YouTube.
    /// </summary>
    ReplyPostedToYouTube = 4,

    /// <summary>
    /// Enqueuing an AI template generation job. With suggesting playlists for a video (background job).
    /// </summary>
    AiTemplateWithPlaylistEnqueued = 5,
    
    /// <summary>
    /// Suggesting playlists for a video.
    /// </summary>
    AiPlaylistSuggestionEnqueued = 6,
}

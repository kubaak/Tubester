namespace Tubester.Abstractions.Credits;

/// <summary>
/// Represents billable actions that consume credits.
/// </summary>
public enum CreditActionType
{
    /// <summary>
    /// Copying template metadata between videos.
    /// </summary>
    CopyTemplateExecuted,

    /// <summary>
    /// Enqueuing an AI template generation job.
    /// </summary>
    AiTemplateEnqueued,

    /// <summary>
    /// Submitting AI-generated template changes to YouTube.
    /// </summary>
    AiTemplateSubmitted,

    /// <summary>
    /// Generating an AI reply for a comment (background job).
    /// </summary>
    AiReplyGenerated,

    /// <summary>
    /// Posting a reply to YouTube.
    /// </summary>
    ReplyPostedToYouTube,

    /// <summary>
    /// Enqueuing an AI template generation job. With suggesting playlists for a video (background job).
    /// </summary>
    AiTemplateWithPlaylistEnqueued
}

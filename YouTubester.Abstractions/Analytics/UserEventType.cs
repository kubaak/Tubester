namespace YouTubester.Abstractions.Analytics;

/// <summary>
/// Represents the different types of user actions that can be tracked.
/// </summary>
public enum UserEventType
{
    /// <summary>
    /// The user signed in to the application.
    /// </summary>
    Login = 0,

    /// <summary>
    /// The user executed a copy template action.
    /// </summary>
    CopyTemplateExecuted = 1,

    /// <summary>
    /// The user enqueued an artificial intelligence powered template job.
    /// </summary>
    AiTemplateEnqueued = 2,

    /// <summary>
    /// The user submitted an artificial intelligence powered template result.
    /// </summary>
    AiTemplateSubmitted = 3,

    /// <summary>
    /// The user posted a reply to YouTube.
    /// </summary>
    ReplyPostedToYouTube = 4,

    /// <summary>
    /// The user granted write consent for managing their YouTube content.
    /// </summary>
    WriteConsentGranted = 5
}

We already have:

Cookie-based auth with Google.

A login flow that enriches the auth cookie with claims:

yt_channel_id

yt_channel_title

yt_channel_picture

An /api/auth/me endpoint that reads these claims and returns them to the frontend.

Now I want to treat channelId as the tenant key for the whole app and introduce a clean, reusable abstraction for the
“current channel”:

Goal:

Introduce an ICurrentChannelContext abstraction in Abstractions,

Implement it in Api using HttpContext.User claims (specifically yt_channel_id),

Use it in Application and Integration services (instead of manually reading claims in controllers),

Start wiring channelId into repository calls to support per-channel multi-tenancy.

Please implement the following steps.

1. Add ICurrentChannelContext to Abstractions

In Tubester.Abstractions.Channels (or a similar namespace where channel-related abstractions live), add a new
interface:

namespace Tubester.Abstractions.Channels;

public interface ICurrentChannelContext
{
/// <summary>
/// Returns the YouTube channel id for the current session, or null if not available.
/// </summary>
string? ChannelId { get; }

    /// <summary>
    /// Returns the channel id for the current session, or throws if not available.
    /// Use this from code that requires a channel to be present.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no channel id is available.</exception>
    string GetRequiredChannelId();

}

This interface must be framework-agnostic (no ASP.NET types) and Google-agnostic.

2. Implement ICurrentChannelContext in the Api project

In Tubester.Api (e.g. Tubester.Api/Auth/CurrentChannelContext.cs), add a concrete implementation:

It should use IHttpContextAccessor to reach HttpContext.User.

It should read the yt_channel_id claim.

It must implement both ChannelId (nullable) and GetRequiredChannelId() (throws if missing).

Example behavior (do NOT copy code verbatim, follow project conventions):

ChannelId:

Returns User.FindFirst("yt_channel_id")?.Value if present.

Returns null if there is no HttpContext or no claim.

GetRequiredChannelId():

Calls ChannelId.

If null/whitespace → throws InvalidOperationException("Current channel id is not available.").

Register it in Program.cs in the API project:

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentChannelContext, CurrentChannelContext>();

(ICurrentChannelContext is from Abstractions; CurrentChannelContext is the implementation in Api.)

3. Start using ICurrentChannelContext in Application services

Now update your Application layer to depend on the current channel context instead of making controllers pass channelId
around.

3.1. VideoService

Locate VideoService in Tubester.Application and:

Inject ICurrentChannelContext:

private readonly ICurrentChannelContext _channelContext;

Add it via constructor injection.

Modify methods that work on “current channel videos” to use channelId from the context instead of (or in addition to)
accepting it as a parameter.

For example, change something like:

Task<IReadOnlyList<VideoDto>> GetVideosAsync(CancellationToken ct);

to internally do:

var channelId = _channelContext.GetRequiredChannelId();

Call the repository with that channelId.

If there are any overloads currently taking channelId explicitly, you can keep them for now, but add new overloads that
derive the channelId from _channelContext to support the common case “current channel only”.

3.2. ChannelSyncService

Do the same for ChannelSyncService or any other service where the concept of “current channel” makes sense:

Inject ICurrentChannelContext.

Where you previously used the Google sub to find channels, now:

Use GetRequiredChannelId() when you need the specific selected channel for this session.

Or still use UserId / sub for user-level operations, but use channelId to narrow down which channel’s data to operate
on.

4. Wire channelId into repository calls where appropriate

In the Persistence layer (e.g. Tubester.Persistence.Videos, Tubester.Persistence.Replies):

For repositories where data is inherently per-channel (videos, replies, etc.), introduce channel-aware methods like:

Task<IReadOnlyList<Video>> GetVideosByChannelAsync(string channelId, CancellationToken ct);

and update existing Application code to use this, passing channelId obtained from _channelContext.

Do not inject ICurrentChannelContext into repositories directly (keep Persistence dumb); instead:

Application layer uses _channelContext.GetRequiredChannelId().

Application passes channelId as a plain string to Persistence.

This keeps your layering clean: Abstractions + Application know about “current channel”, Persistence just filters by the
value passed in.

5. Optional: use ICurrentChannelContext in Integration, too

If your YouTubeIntegration needs to know the current channel for certain operations (e.g. to log or map YouTube data to
a specific channel in DB):

Inject ICurrentChannelContext into YouTubeIntegration (Integration already depends on Abstractions).

Use _channelContext.ChannelId or GetRequiredChannelId() where appropriate (e.g. when associating fetched data with your
own Channel entity).

Note: For many YouTube API calls you do not need the channelId explicitly, because YouTube uses the access token to
determine it. But for internal mapping/logging/storage you might still want to record which channelId your integration
call is logically associated with.

6. Controllers should not manually read yt_channel_id anymore

As a consequence of adding ICurrentChannelContext, clean up controllers that might manually read the yt_channel_id
claim:

For standard “current channel” operations:

Controllers should just call videoService.GetVideosAsync(ct) (no channelId param).

The service resolves the channel via ICurrentChannelContext.

If you have endpoints that need to work across channels (future multi-channel support), you can still accept channelId
as a query/path parameter and pass it into services; but for the “one channel per session” scenario, this should not be
necessary.

7. Ensure everything compiles and behavior stays the same

Update all affected constructors in DI registration (AddScoped<IVideoService, VideoService>, etc.) to account for the
new ICurrentChannelContext dependency.

Run the solution build and fix any compile errors related to:

new interface usage,

missing constructor parameters,

methods signatures changes (e.g. GetVideosAsync losing a channelId parameter).

Ensure that:

Auth flow still sets yt_channel_id claim at login (already implemented previously).

/api/auth/me continues to work as before (reading claims only).

A typical flow:

user logs in,

FE calls /api/auth/me and /api/videos,

VideoService uses ICurrentChannelContext → correct channelId is applied in all queries.

Please apply these changes incrementally, respecting existing naming and coding conventions in the Tubester codebase.
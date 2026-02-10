I want to adjust the authentication flow and the /api/auth/me endpoint as follows:

On login with Google, we call the YouTube Data API once to discover the user’s current channel.

From that channel we take: channelId, channel title, and channel picture URL.

We reuse Tubester.Abstractions.Channels.UserChannelDto to represent this channel.

We store three values as claims in the auth cookie:

yt_channel_id

yt_channel_title

yt_channel_picture

The /api/auth/me endpoint should only read from claims and return user + channel info, without calling YouTube again.

We are not storing tokens in DB in this phase.

Please modify the solution accordingly, using existing architecture and naming conventions.

1. Use UserChannelDto in YouTubeIntegration for current-channel discovery

In Tubester.Abstractions.Channels, there is already a UserChannelDto. Reuse it instead of creating a new DTO.

In Tubester.Integration:

Extend IYouTubeIntegration with:

Task<UserChannelDto?> GetCurrentChannelAsync(string accessToken, CancellationToken ct);

(Namespace: Tubester.Abstractions.Channels for UserChannelDto.)

Implement GetCurrentChannelAsync in YouTubeIntegration:

Build a GoogleCredential.FromAccessToken(accessToken).CreateScoped(YouTubeService.Scope.YoutubeReadonly).

Create a temporary YouTubeService from that credential.

Call Channels.List("snippet") with Mine = true.

Take the first item from the response and map it to UserChannelDto, using appropriate properties, e.g.:

UserChannelDto.ChannelId ← item.Id

UserChannelDto.Title ← item.Snippet.Title

UserChannelDto.PictureUrl (or similar field) ← best thumbnail URL from item.Snippet.Thumbnails.

If no channel is returned, return null.

This method is used only during login with the raw access token; it must not depend on DB token storage.

2. Enrich the principal with channel claims during Google login

In Tubester.Api.Extensions.ServiceCollectionExtensions.AddCookieWithGoogle where Google auth is configured:

Keep o.SaveTokens = true.

In the Google auth events (OAuthEvents), e.g. OnTicketReceived or OnCreatingTicket:

Extract access token:

var accessToken = context.Properties?.GetTokenValue("access_token");

If accessToken is null/empty, log and skip channel enrichment.

Resolve IYouTubeIntegration:

var yt = context.HttpContext.RequestServices.GetRequiredService<IYouTubeIntegration>();

Call:

var channel = await yt.GetCurrentChannelAsync(accessToken, context.HttpContext.RequestAborted);

If channel is not null, get the identity:

var identity = (ClaimsIdentity)context.Principal!.Identity!;

Add claims based on UserChannelDto:

identity.AddClaim(new Claim("yt_channel_id", channel.ChannelId));
identity.AddClaim(new Claim("yt_channel_title", channel.Title ?? string.Empty));
identity.AddClaim(new Claim("yt_channel_picture", channel.PictureUrl ?? string.Empty));

Do not persist tokens to DB here.
This step only enriches the auth cookie with channel id/title/picture via claims.

Ensure the Google auth scopes for this flow include YouTubeService.Scope.YoutubeReadonly (read-only).

3. Update /api/auth/me to read channel info from claims only

In Tubester.Api.AuthController:

Keep [Authorize] and [Route("api/auth")].

Update Me so it:

Reads basic user info from claims:

var name = User.Identity?.Name;
var email = User.FindFirst(ClaimTypes.Email)?.Value;
var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var googlePicture = User.FindFirst("picture")?.Value;

Reads channel-related claims:

var channelId = User.FindFirst("yt_channel_id")?.Value;
var channelTitle = User.FindFirst("yt_channel_title")?.Value;
var channelPicture = User.FindFirst("yt_channel_picture")?.Value;

Chooses picture as:

var picture = string.IsNullOrWhiteSpace(channelPicture)

    ? googlePicture
    : channelPicture;

Returns JSON like:

[HttpGet("me")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public IActionResult Me()
{
var name = User.Identity?.Name;
var email = User.FindFirst(ClaimTypes.Email)?.Value;
var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var googlePicture = User.FindFirst("picture")?.Value;

    var channelId      = User.FindFirst("yt_channel_id")?.Value;
    var channelTitle   = User.FindFirst("yt_channel_title")?.Value;
    var channelPicture = User.FindFirst("yt_channel_picture")?.Value;

    var picture = string.IsNullOrWhiteSpace(channelPicture)
        ? googlePicture
        : channelPicture;

    return Ok(new
    {
        name,
        email,
        sub,
        channelId,
        channelTitle,
        picture
    });

}

Me should not call IYouTubeIntegration; all info must come from claims added at login.

4. Ensure DI wiring is correct

IYouTubeIntegration is already registered (e.g. through AddYoutubeServices); reuse it.

The Google auth event handler must be able to resolve IYouTubeIntegration from context.HttpContext.RequestServices.

No new dependency from Integration to Api; Integration depends only on Abstractions and Google packages, and uses
UserChannelDto from Tubester.Abstractions.Channels.

5. Tokens are not persisted in DB

Confirm that in this change:

No call to IUserTokenStore / UserTokenStore is made from the auth events or /auth/me.

Tokens live in:

the auth cookie via SaveTokens = true,

the handler’s local variables during OnTicketReceived.

UserChannelDto is used only as a shape for the YouTube channel info returned by GetCurrentChannelAsync, and channel data
is stored into claims, not into the DB.

Apply these changes in small steps and keep everything consistent with the existing Tubester style and structure.
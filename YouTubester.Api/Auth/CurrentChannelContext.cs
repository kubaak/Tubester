using YouTubester.Abstractions.Channels;

namespace YouTubester.Api.Auth;

public sealed class CurrentChannelContext(IHttpContextAccessor httpContextAccessor) : ICurrentChannelContext
{
    public string? ChannelId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            var user = httpContext?.User;
            if (user is null || !user.Identity?.IsAuthenticated == true)
            {
                return null;
            }

            return user.FindFirst("yt_channel_id")?.Value;
        }
    }

    public string GetRequiredChannelId()
    {
        var channelId = ChannelId;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new InvalidOperationException("Current channel id is not available.");
        }

        return channelId;
    }
}
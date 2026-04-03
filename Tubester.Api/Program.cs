using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Users;
using Tubester.Abstractions.Videos;
using Tubester.Api.Auth;
using Tubester.Api.Extensions;
using Tubester.Api.Hangfire;
using Tubester.Api.Infrastructure;
using Tubester.Application;
using Tubester.Application.Channels;
using Tubester.Application.Credits;
using Tubester.Application.DomainEvents;
using Tubester.Application.Videos;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Persistence.Analytics;
using Tubester.Persistence.Channels;
using Tubester.Persistence.Credits;
using Tubester.Persistence.Playlists;
using Tubester.Persistence.Replies;
using Tubester.Persistence.Users;
using Tubester.Persistence.Videos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;

    //clearing so forwarded headers are accepted. Safe behind proxy (ngingx)
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwagger();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentChannelContext, CurrentChannelContext>();
builder.Services.AddAiClient(builder.Configuration);
builder.Services.AddOnlineYoutubeServices(builder.Configuration);
builder.Services.AddScoped<ICurrentUserTokenAccessor, CurrentUserTokenAccessor>();
builder.Services.AddScoped<IReplyRepository, ReplyRepository>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<IPlaylistRepository, PlaylistRepository>();
builder.Services.AddScoped<IUserTokenStore, UserTokenStore>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserEventLogger, UserEventLogger>();
builder.Services.AddScoped<ICreditsStore, CreditsStore>();
builder.Services.AddScoped<ICreditsService, CreditsService>();
builder.Services.AddScoped<IReplyService, ReplyService>();
builder.Services.AddDomainEventHandlers();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IChannelSyncService, ChannelSyncService>();
builder.Services.AddScoped<IAiVideoTemplatingService, AiVideoTemplatingService>();
builder.Services.AddScoped<IAiTemplateOrchestrationService, AiTemplateOrchestrationService>();
builder.Services.AddScoped<ICommentScanService, CommentScanService>();
builder.Services.AddSingleton<IDateTimeOffsetProvider, DateTimeOffsetProvider>();

builder.Services.AddVideoListingOptions(builder.Configuration);
builder.Services.AddCookieWithGoogle(builder.Configuration);

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddHangFireStorage(builder.Configuration);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRouting();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
var hangfireAdminEmails = app.Configuration
    .GetSection("Hangfire:AdminEmails")
    .Get<string[]>() ?? [];
var dashboardOptions = new DashboardOptions
{
    Authorization = [new EmailHangfireAuthorizationFilter(hangfireAdminEmails)]
};
app.UseHangfireDashboard("/hangfire", dashboardOptions);
//In local development to get redirected back to the client after the login
if (app.Environment.IsDevelopment())
{
    app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api") &&
                       !ctx.Request.Path.StartsWithSegments("/hangfire") &&
                       !ctx.Request.Path.StartsWithSegments("/swagger"), spa =>
    {
        spa.UseSpa(spaApp =>
        {
            spaApp.Options.SourcePath = "../Tubester-Client";
            if (app.Environment.IsDevelopment())
            {
                spaApp.UseProxyToSpaDevelopmentServer("http://localhost:5173");
            }
        });
    });
}
app.MapControllers();
app.Run();

namespace Tubester.Api
{
    /// <summary>
    /// Make Program class accessible for integration testing
    /// </summary>
    public class Program;
}
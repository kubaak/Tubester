using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Tubester.Abstractions;
using Tubester.Abstractions.Account;
using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.Auth;
using Tubester.Abstractions.Channels;
using Tubester.Abstractions.Credits;
using Tubester.Abstractions.Playlists;
using Tubester.Abstractions.Replies;
using Tubester.Abstractions.Transactions;
using Tubester.Abstractions.Users;
using Tubester.Abstractions.Videos;
using Tubester.Api.Auth;
using Tubester.Api.Extensions;
using Tubester.Api.Hangfire;
using Tubester.Api.Infrastructure;
using Tubester.Observability;
using Tubester.Application;
using Tubester.Application.Account;
using Tubester.Application.Auth;
using Tubester.Application.Channels;
using Tubester.Application.Credits;
using Tubester.Application.DomainEvents;
using Tubester.Application.Playlists;
using Tubester.Application.Users;
using Tubester.Application.Videos;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Persistence.Account;
using Tubester.Persistence.Analytics;
using Tubester.Persistence.Channels;
using Tubester.Persistence.Credits;
using Tubester.Persistence.Playlists;
using Tubester.Persistence.Replies;
using Tubester.Persistence.Transactions;
using Tubester.Persistence.Users;
using Tubester.Persistence.Videos;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog first for startup logging
builder.AddTubesterSerilog("Tubester.Api");

// Configure forwarded headers
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;

    //clearing so forwarded headers are accepted. Safe behind proxy (ngingx)
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwagger();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentChannelContext, CurrentChannelContext>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddAiClient(builder.Configuration);
builder.Services.AddApplicationConfigurationServices();
builder.Services.AddOnlineYoutubeServices(builder.Configuration);
builder.Services.AddScoped<ICurrentUserTokenAccessor, CurrentUserTokenAccessor>();
builder.Services.AddScoped<IReplyRepository, ReplyRepository>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IChannelSettingsRepository, ChannelSettingsRepository>();
builder.Services.AddScoped<IAccountSettingsRepository, AccountSettingsRepository>();
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<IPlaylistRepository, PlaylistRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserEventLogger, UserEventLogger>();
builder.Services.AddScoped<ICreditsStore, CreditsStore>();
builder.Services.AddScoped<ICreditsService, CreditsService>();
builder.Services.AddScoped<IReplyService, ReplyService>();
builder.Services.AddDomainEventHandlers();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IChannelSyncService, ChannelSyncService>();
builder.Services.AddScoped<IChannelSettingsService, ChannelSettingsService>();
builder.Services.AddScoped<IAccountSettingsService, AccountSettingsService>();
builder.Services.AddScoped<IAiVideoImprovingService, AiVideoImprovingService>();
builder.Services.AddScoped<IAiTemplateOrchestrationService, AiTemplateOrchestrationService>();
builder.Services.AddScoped<ICommentScanService, CommentScanService>();
builder.Services.AddScoped<IUserOnboardingService, UserOnboardingService>();
builder.Services.AddScoped<IUserDataDeletionService, UserDataDeletionService>();
builder.Services.AddSingleton<IDateTimeOffsetProvider, DateTimeOffsetProvider>();
builder.Services.AddScoped<IApplicationTransactionRunner, EfApplicationTransactionRunner>();

builder.Services.AddVideoListingOptions(builder.Configuration);
builder.Services.AddReplyListingOptions(builder.Configuration);
builder.Services.AddPlaylistSuggestionOptions(builder.Configuration);
builder.Services.AddCookieWithGoogle(builder.Configuration);

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddHangFireStorage(builder.Configuration);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Add OpenTelemetry metrics (API-specific instrumentation)
builder.AddTubesterOpenTelemetryMetrics("Tubester.Api", true);

// Add API-specific health checks
builder.Services.AddTubesterPostgresHealthChecks(
    builder.Configuration,
    serviceDisplayName: "API");

// Admin email authorization
var adminEmails = builder.Configuration.GetSection("AdminEmails").Get<string[]>() ?? [];
builder.Services.AddScoped<IAuthorizationHandler>(p =>
    new AdminEmailAuthorizationHandler(
        p.GetRequiredService<ICurrentUserContext>()));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminEmail", policy =>
        policy.Requirements.Add(new AdminEmailRequirement(adminEmails)));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRouting();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

// Map observability endpoints
app.MapTubesterMetricsEndpoint();
app.MapTubesterHealthEndpoints();

var dashboardOptions = new DashboardOptions
{
    Authorization = [new EmailHangfireAuthorizationFilter(adminEmails)]
};

app.UseHangfireDashboard("/hangfire", dashboardOptions);
// Opt in when developing with the client to support redirects back to its UI.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Spa:Enabled"))
{
    app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api") &&
                       !ctx.Request.Path.StartsWithSegments("/hangfire") &&
                       !ctx.Request.Path.StartsWithSegments("/swagger") &&
                       !ctx.Request.Path.StartsWithSegments("/health") &&
                       !ctx.Request.Path.StartsWithSegments("/metrics"), spa =>
    {
        spa.UseSpa(spaApp =>
        {
            spaApp.Options.SourcePath = "../Tubester-Client";
            spaApp.UseProxyToSpaDevelopmentServer("http://localhost:5173");
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

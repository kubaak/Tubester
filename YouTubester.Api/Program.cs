using Hangfire;
using Microsoft.EntityFrameworkCore;
using YouTubester.Abstractions.Auth;
using YouTubester.Abstractions.Channels;
using YouTubester.Abstractions.Playlists;
using YouTubester.Abstractions.Replies;
using YouTubester.Abstractions.Videos;
using YouTubester.Api.Auth;
using YouTubester.Api.Extensions;
using YouTubester.Api.Infrastructure;
using YouTubester.Application;
using YouTubester.Application.Channels;
using YouTubester.Integration;
using YouTubester.Persistence;
using YouTubester.Persistence.Channels;
using YouTubester.Persistence.Playlists;
using YouTubester.Persistence.Replies;
using YouTubester.Persistence.Users;
using YouTubester.Persistence.Videos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwagger();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentChannelContext, CurrentChannelContext>();
builder.Services.AddAiClient(builder.Configuration);
builder.Services.AddYoutubeServices(builder.Configuration);
builder.Services.AddScoped<ICurrentUserTokenAccessor, CurrentUserTokenAccessor>();
builder.Services.AddScoped<IReplyRepository, ReplyRepository>();
builder.Services.AddScoped<IChannelRepository, ChannelRepository>();
builder.Services.AddScoped<IVideoRepository, VideoRepository>();
builder.Services.AddScoped<IPlaylistRepository, PlaylistRepository>();
builder.Services.AddScoped<IUserTokenStore, UserTokenStore>();
builder.Services.AddSingleton<IAiClient, AiClient>();
builder.Services.AddScoped<IReplyService, ReplyService>();
builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IChannelSyncService, ChannelSyncService>();
builder.Services.AddScoped<IVideoTemplatingService, VideoTemplatingService>();
builder.Services.AddVideoListingOptions(builder.Configuration);
builder.Services.AddCookieWithGoogle(builder.Configuration);

var rootPath = builder.Environment.ContentRootPath;
builder.Services.AddDatabase(rootPath);
builder.Services.AddHangFireStorage(builder.Configuration, rootPath);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHangfireDashboard();
    app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"), spa =>
    {
        spa.UseSpa(spaApp =>
        {
            spaApp.Options.SourcePath = "../YouTubester.Client";
            if (app.Environment.IsDevelopment())
            {
                spaApp.UseProxyToSpaDevelopmentServer("http://localhost:5173");
            }
        });
    });

    var cfg = app.Services.GetRequiredService<IConfiguration>();
    if (cfg.GetValue<bool>("Seed:Enable"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();

        db.Database.Migrate();
        await DbSeeder.SeedAsync(db);
    }
}
else
{
    app.UseHangfireDashboard();
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "wwwroot";
    });
}


app.Run();

namespace YouTubester.Api
{
    /// <summary>
    /// Make Program class accessible for integration testing
    /// </summary>
    public class Program;
}
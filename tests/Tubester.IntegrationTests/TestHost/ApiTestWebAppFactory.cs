using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tubester.Abstractions;
using Tubester.Abstractions.Channels;
using Tubester.Integration;
using Tubester.Persistence;

namespace Tubester.IntegrationTests.TestHost;

public class ApiTestWebAppFactory : WebApplicationFactory<Program>
{
    private CapturingBackgroundJobClient CapturingJobClient { get; }
    public Mock<IYouTubeIntegration> MockYouTubeIntegration { get; }
    public Mock<ICurrentChannelContext> MockCurrentChannelContext { get; }
    public Mock<IDateTimeOffsetProvider> MockDateTimeOffsetProvider { get; }

    public ApiTestWebAppFactory(CapturingBackgroundJobClient capturingJobClient, DateTimeOffset testingUtcNow)
    {
        CapturingJobClient = capturingJobClient;
        MockYouTubeIntegration = new Mock<IYouTubeIntegration>(MockBehavior.Strict);
        MockCurrentChannelContext = new Mock<ICurrentChannelContext>(MockBehavior.Strict);
        MockCurrentChannelContext.Setup(x => x.GetRequiredChannelId()).Returns(TestConstants.ChannelId);
        MockCurrentChannelContext.Setup(x => x.ChannelId).Returns(TestConstants.ChannelId);
        MockCurrentChannelContext.Setup(x => x.UploadPlaylistId).Returns(TestConstants.UploadsPlaylistId);
        MockCurrentChannelContext.Setup(x => x.GetRequiredUploadPlaylistId()).Returns(TestConstants.UploadsPlaylistId);
        MockDateTimeOffsetProvider = new Mock<IDateTimeOffsetProvider>(MockBehavior.Strict);
        MockDateTimeOffsetProvider.Setup(x => x.GetUtcNowDateTimeOffset()).Returns(testingUtcNow);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureServices((context, services) =>
        {
            // Get connection string from appsettings.Test.json, with optional env override
            var csFromConfig = context.Configuration.GetConnectionString("TubesterDb");
            var envOverride = Environment.GetEnvironmentVariable("Tubester_INTEGRATIONTESTS_CONNECTION_STRING");
            var testConnectionString = envOverride ?? csFromConfig
                ?? throw new InvalidOperationException(
                    "Test DB connection string is not configured.");

            // Remove app registrations we're replacing
            services.RemoveAll<DbContextOptions<TubesterDb>>();
            services.RemoveAll<TubesterDb>();
            services.RemoveAll<IBackgroundJobClient>();
            services.RemoveAll<IAiTextGenerationClientFactory>();
            services.RemoveAll<IYouTubeIntegration>();
            services.RemoveAll<IDateTimeOffsetProvider>();

            // Also remove authorization services
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.RemoveAll<IPostConfigureOptions<AuthenticationOptions>>();
            services.RemoveAll<IAuthorizationService>();
            services.RemoveAll<IAuthorizationPolicyProvider>();
            services.RemoveAll<IAuthorizationHandlerProvider>();
            services.RemoveAll<ICurrentChannelContext>();

            // Add test database
            services.AddDbContext<TubesterDb>(options =>
            {
                options.UseNpgsql(testConnectionString);
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            });

            services.AddSingleton<IBackgroundJobClient>(CapturingJobClient);
            services.AddSingleton(MockYouTubeIntegration.Object);
            services.AddSingleton(MockDateTimeOffsetProvider.Object);

            // Add mock authentication
            services.AddMockAuthentication();
            services.AddSingleton(MockCurrentChannelContext.Object);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        await dbContext.Database.MigrateAsync();
    }
}

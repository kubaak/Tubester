using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Worker;

namespace Tubester.IntegrationTests.TestHost;

public sealed class WorkerTestHostFactory : WebApplicationFactory<Worker.Program>
{
    private CapturingBackgroundJobClient CapturingJobClient { get; }
    public Mock<IAiTextGenerationClient> MockAiTextGenerationClient { get; }
    public Mock<IAiTextGenerationClientFactory> MockAiTextGenerationClientFactory { get; }
    public Mock<IYouTubeIntegration> MockYouTubeIntegration { get; }
    public Mock<IBackgroundYoutubeIntegration> MockBackgroundYoutubeIntegration { get; }
    public Mock<IEmbeddingService> MockEmbeddingService { get; }
    public Mock<IEmbeddingServiceFactory> MockEmbeddingServiceFactory { get; }
    public Mock<IDateTimeOffsetProvider> MockDateTimeOffsetProvider { get; }
    private readonly TestAiMode _aiMode;

    public WorkerTestHostFactory(CapturingBackgroundJobClient capturingJobClient, DateTimeOffset testingUtcNow, TestAiMode aiMode = TestAiMode.Mock)
    {
        CapturingJobClient = capturingJobClient;
        _aiMode = aiMode;

        MockAiTextGenerationClient = new Mock<IAiTextGenerationClient>(MockBehavior.Strict);
        MockAiTextGenerationClient.SetupGet(x => x.Provider).Returns(AiProviders.Ollama);
        MockAiTextGenerationClientFactory = new Mock<IAiTextGenerationClientFactory>();
        MockAiTextGenerationClientFactory.Setup(x => x.GetClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockAiTextGenerationClient.Object);
        MockEmbeddingService = new Mock<IEmbeddingService>(MockBehavior.Strict);
        MockEmbeddingServiceFactory = new Mock<IEmbeddingServiceFactory>();
        MockEmbeddingServiceFactory.Setup(x => x.GetServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockEmbeddingService.Object);
        MockYouTubeIntegration = new Mock<IYouTubeIntegration>(MockBehavior.Strict);
        MockBackgroundYoutubeIntegration = new Mock<IBackgroundYoutubeIntegration>(MockBehavior.Strict);
        MockDateTimeOffsetProvider = new Mock<IDateTimeOffsetProvider>(MockBehavior.Strict);
        MockDateTimeOffsetProvider.Setup(x => x.GetUtcNowDateTimeOffset()).Returns(testingUtcNow);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureServices((context, services) =>
        {
            ConfigureServices(services, context.Configuration);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
    }

    private void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Resolve test connection string from config (appsettings.Test.json) with optional env override
        var csFromConfig = configuration.GetConnectionString("TubesterDb");
        var envOverride =
            Environment.GetEnvironmentVariable("Tubester_INTEGRATIONTESTS_CONNECTION_STRING");

        var testDatabaseConnectionString = envOverride ?? csFromConfig
            ?? throw new InvalidOperationException(
                "Test DB connection string is not configured. " +
                "Set ConnectionStrings:TubesterDb in appsettings.Test.json " +
                "or Tubester_INTEGRATIONTESTS_CONNECTION_STRING.");

        // Remove app registrations we're replacing
        services.RemoveAll<DbContextOptions<TubesterDb>>();
        services.RemoveAll<TubesterDb>();
        services.RemoveAll<IBackgroundJobClient>();
        services.RemoveAll<IAiTextGenerationClientFactory>();
        services.RemoveAll<IAiTextGenerationClient>();
        services.RemoveAll<IEmbeddingServiceFactory>();
        services.RemoveAll<IEmbeddingService>();
        services.RemoveAll<IYouTubeIntegration>();
        services.RemoveAll<IBackgroundYoutubeIntegration>();
        services.RemoveAll<IDateTimeOffsetProvider>();

        // Use the same core registrations, but without Hangfire server
        services.AddWorkerCore(configuration, addHangfireServer: false);

        // Replace the DB with the test DB
        services.AddDbContext<TubesterDb>(options =>
        {
            options.UseNpgsql(testDatabaseConnectionString);
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        // Override background job client + external integrations with mocks
        services.AddSingleton<IBackgroundJobClient>(CapturingJobClient);
        services.AddSingleton(MockYouTubeIntegration.Object);
        services.AddSingleton(MockBackgroundYoutubeIntegration.Object);
        services.AddSingleton(MockDateTimeOffsetProvider.Object);

        if (_aiMode == TestAiMode.Mock)
        {
            services.AddSingleton(MockAiTextGenerationClientFactory.Object);
            services.AddSingleton(MockAiTextGenerationClient.Object);
            services.AddSingleton(MockEmbeddingServiceFactory.Object);
            services.AddSingleton(MockEmbeddingService.Object);
        }
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        await dbContext.Database.MigrateAsync();
    }
}

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Tubester.Abstractions;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Integration;
using Tubester.Persistence;
using Tubester.Worker;

namespace Tubester.IntegrationTests.TestHost;

public sealed class WorkerTestHostFactory : IDisposable
{
    public IHost TestHost { get; }
    public string TestDatabaseConnectionString { get; private set; } = default!;
    public Mock<IAiTextGenerationClient> MockAiTextGenerationClient { get; }
    public Mock<IAiTextGenerationClientFactory> MockAiTextGenerationClientFactory { get; }
    public Mock<IYouTubeIntegration> MockYouTubeIntegration { get; }
    public Mock<IBackgroundYoutubeIntegration> MockBackgroundYoutubeIntegration { get; }
    public Mock<IDateTimeOffsetProvider> MockDateTimeOffsetProvider { get; }
    private readonly TestAiMode _aiMode;


    public WorkerTestHostFactory(CapturingBackgroundJobClient capturingJobClient, DateTimeOffset testingUtcNow, TestAiMode aiMode = TestAiMode.Mock)
    {
        _aiMode = aiMode;
        MockAiTextGenerationClient = new Mock<IAiTextGenerationClient>(MockBehavior.Strict);
        MockAiTextGenerationClient.SetupGet(x => x.Provider).Returns(AiProviders.Ollama);
        MockAiTextGenerationClientFactory = new Mock<IAiTextGenerationClientFactory>();
        MockAiTextGenerationClientFactory.Setup(x => x.GetClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockAiTextGenerationClient.Object);
        MockYouTubeIntegration = new Mock<IYouTubeIntegration>(MockBehavior.Strict);
        MockBackgroundYoutubeIntegration = new Mock<IBackgroundYoutubeIntegration>(MockBehavior.Strict);
        MockDateTimeOffsetProvider = new Mock<IDateTimeOffsetProvider>(MockBehavior.Strict);
        MockDateTimeOffsetProvider.Setup(x => x.GetUtcNowDateTimeOffset()).Returns(testingUtcNow);

        var hostBuilder = Host.CreateDefaultBuilder([]);

        hostBuilder.UseEnvironment("Test");

        hostBuilder.ConfigureServices((context, services) =>
        {
            ConfigureServices(services, context.Configuration, capturingJobClient);
        });

        hostBuilder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        TestHost = hostBuilder.Build();
    }

    private void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        CapturingBackgroundJobClient capturingJobClient)
    {
        // Resolve test connection string from config (appsettings.Test.json) with optional env override
        var csFromConfig = configuration.GetConnectionString("TubesterDb");
        var envOverride =
            Environment.GetEnvironmentVariable("Tubester_INTEGRATIONTESTS_CONNECTION_STRING");

        TestDatabaseConnectionString = envOverride ?? csFromConfig
            ?? throw new InvalidOperationException(
                "Test DB connection string is not configured. " +
                "Set ConnectionStrings:TubesterDb in appsettings.Test.json " +
                "or Tubester_INTEGRATIONTESTS_CONNECTION_STRING.");

        // Use the same core registrations, but without hosted services & Hangfire server
        services.AddWorkerCore(configuration, false);

        // Replace the DB with the test DB
        services.RemoveAll<DbContextOptions<TubesterDb>>();
        services.RemoveAll<TubesterDb>();
        services.AddDbContext<TubesterDb>(options =>
        {
            options.UseNpgsql(TestDatabaseConnectionString);
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        if (_aiMode == TestAiMode.Mock)
        {
            services.Replace(ServiceDescriptor.Scoped<IAiTextGenerationClientFactory>(_ => MockAiTextGenerationClientFactory.Object));
            services.Replace(ServiceDescriptor.Scoped<IAiTextGenerationClient>(_ => MockAiTextGenerationClient.Object));
        }

        // Override background job client + external integrations with mocks
        services.Replace(ServiceDescriptor.Singleton<IBackgroundJobClient>(capturingJobClient));
        services.Replace(ServiceDescriptor.Singleton(MockYouTubeIntegration.Object));
        services.Replace(ServiceDescriptor.Singleton(MockBackgroundYoutubeIntegration.Object));
        services.Replace(ServiceDescriptor.Singleton(MockDateTimeOffsetProvider.Object));
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = TestHost.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        await dbContext.Database.MigrateAsync();
    }

    public void Dispose()
    {
        TestHost.Dispose();
    }
}
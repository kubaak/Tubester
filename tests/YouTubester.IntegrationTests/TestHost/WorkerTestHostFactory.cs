using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using YouTubester.Integration;
using YouTubester.Persistence;
using YouTubester.Worker;

namespace YouTubester.IntegrationTests.TestHost;

public sealed class WorkerTestHostFactory : IDisposable
{
    public IHost TestHost { get; }
    public string TestDatabasePath { get; }
    public Mock<IAiClient> MockAiClient { get; }
    public Mock<IYouTubeIntegration> MockYouTubeIntegration { get; }

    public WorkerTestHostFactory(CapturingBackgroundJobClient capturingJobClient, string testDatabasePath)
    {
        TestDatabasePath = testDatabasePath;
        MockAiClient = new Mock<IAiClient>(MockBehavior.Strict);
        MockYouTubeIntegration = new Mock<IYouTubeIntegration>(MockBehavior.Strict);

        var hostBuilder = Host.CreateDefaultBuilder([]);

        hostBuilder.UseEnvironment("Test");
        hostBuilder.ConfigureAppConfiguration(config =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", $"Data Source={TestDatabasePath}" }
            });
        });

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

    private void ConfigureServices(IServiceCollection services, IConfiguration configuration,
        CapturingBackgroundJobClient capturingJobClient)
    {
        // Use the same core registrations, but without hosted services & server
        services.AddWorkerCore(configuration, Path.GetDirectoryName(TestDatabasePath)!, false);

        // Replace the DB with the test DB (overrides AddDatabase rootPath)
        services.RemoveAll<YouTubesterDb>();
        services.AddDbContext<YouTubesterDb>(options =>
        {
            options.UseSqlite($"Data Source={TestDatabasePath}");
            options.EnableSensitiveDataLogging();
        });

        // Override background job client + external integrations with mocks
        services.Replace(ServiceDescriptor.Singleton<IBackgroundJobClient>(capturingJobClient));
        services.Replace(ServiceDescriptor.Singleton(MockAiClient.Object));
        services.Replace(ServiceDescriptor.Singleton(MockYouTubeIntegration.Object));

        // If AddWorkerCore added any IHostedService (we disabled, but as a guard):
        services.RemoveAll<IHostedService>();
    }

    public async Task EnsureDatabaseCreatedAsync()
    {
        using var scope = TestHost.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public void Dispose()
    {
        TestHost.Dispose();
    }
}
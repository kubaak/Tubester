using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Domain;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests.TestHost;

public sealed class TestFixture : IAsyncLifetime
{
    public ApiTestWebAppFactory ApiFactory { get; private set; } = null!;
    public WorkerTestHostFactory WorkerFactory { get; private set; } = null!;
    public CapturingBackgroundJobClient CapturingJobClient { get; } = new();
    public HttpClient HttpClient { get; private set; } = null!;
    public IServiceProvider ApiServices => ApiFactory.Services;
    public IServiceProvider WorkerServices => WorkerFactory.TestHost.Services;
    public IFixture Auto { get; private set; } = CreateAuto();
    public static DateTimeOffset TestingDateTimeOffset { get; } = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        ApiFactory = new ApiTestWebAppFactory(CapturingJobClient, TestingDateTimeOffset);
        WorkerFactory = new WorkerTestHostFactory(CapturingJobClient, TestingDateTimeOffset);
        HttpClient = ApiFactory.CreateClient();

        // Ensure database is created once
        await ApiFactory.EnsureDatabaseCreatedAsync();
        await WorkerFactory.EnsureDatabaseCreatedAsync();

        await EnsureApplicationConfigurationAsync();
    }

    public async Task EnsureApplicationConfigurationAsync()
    {
        using var scope = ApiServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        var configurations = await dbContext.ApplicationConfigurations.ToDictionaryAsync(x => x.Key);
        var doSave = false;
        if (!configurations.ContainsKey("Ai.Provider"))
        {
            await dbContext.ApplicationConfigurations.AddAsync(ApplicationConfiguration.Create(
                "Ai.Provider", "Ollama", ConfigurationValueType.String, "Current AI provider used by Tubester",
                true, TestingDateTimeOffset
            ));
            doSave = true;
        }

        if (!configurations.ContainsKey("Ai.Provider"))
        {
            await dbContext.ApplicationConfigurations.AddAsync(ApplicationConfiguration.Create(
                "Ai.Model", "qwen3:8b", ConfigurationValueType.String, "Default AI model used by Tubester",
                true, TestingDateTimeOffset
            ));
            doSave = true;
        }

        if (doSave)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task ResetDbAsync()
    {
        using var scope = ApiServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
        await PostgresCleaner.CleanAsync(dbContext.Database.GetDbConnection());

        // Clear capturing job client
        CapturingJobClient.Clear();
        Auto = CreateAuto();
    }
    
    public Task DisposeAsync()
    {
        HttpClient.Dispose();
        WorkerFactory.Dispose();
        ApiFactory.Dispose();
        return Task.CompletedTask;
    }

    private static IFixture CreateAuto()
    {
        var f = new Fixture()
            .Customize(new AutoMoqCustomization { ConfigureMembers = true });

        // Make complex object graphs safe:
        f.Behaviors.Remove(new ThrowingRecursionBehavior());
        f.Behaviors.Add(new OmitOnRecursionBehavior(1));

        // Keep collections small & fast by default (adjust if you like)
        f.RepeatCount = 1;

        f.Register(() => TestingDateTimeOffset);

        return f;
    }
}
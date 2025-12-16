using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using YouTubester.Persistence;

namespace YouTubester.IntegrationTests.TestHost;

public sealed class TestFixture : IAsyncLifetime
{
    public ApiTestWebAppFactory ApiFactory { get; private set; } = null!;
    public WorkerTestHostFactory WorkerFactory { get; private set; } = null!;
    public CapturingBackgroundJobClient CapturingJobClient { get; } = new();
    public HttpClient HttpClient { get; private set; } = null!;
    public IServiceProvider ApiServices => ApiFactory.Services;
    public IServiceProvider WorkerServices => WorkerFactory.TestHost.Services;
    public IFixture Auto { get; private set; } = CreateAuto();
    public static DateTimeOffset TestingDateTimeOffset { get; } = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        ApiFactory = new ApiTestWebAppFactory(CapturingJobClient);
        WorkerFactory = new WorkerTestHostFactory(CapturingJobClient);
        HttpClient = ApiFactory.CreateClient();

        // Ensure database is created once
        await ApiFactory.EnsureDatabaseCreatedAsync();
        await WorkerFactory.EnsureDatabaseCreatedAsync();
    }

    public async Task ResetDbAsync()
    {
        using var scope = ApiServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<YouTubesterDb>();
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
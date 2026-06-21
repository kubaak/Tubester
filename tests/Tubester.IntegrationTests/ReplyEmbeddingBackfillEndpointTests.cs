using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tubester.Abstractions;
using Tubester.Application.Jobs;
using Tubester.IntegrationTests.TestHost;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class ReplyEmbeddingBackfillEndpointTests(TestFixture fixture)
{
    private readonly TestHelpers _helpers = new(fixture.WorkerServices);

    [Fact]
    public async Task BackfillEndpoint_EnqueuesJobWithCurrentChannelUploadPlaylistId()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        // Act - Use the factory to create an authenticated client
        var client = fixture.ApiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var request = new BackfillCommentEmbeddingsRequestDto
        {
            BatchSize = 100,
            OverwriteExisting = false
        };

        var response = await client.PostAsJsonAsync("api/replies/embeddings/backfill", request);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<BackfillResponse>(content, TubesterJsonSerializerOptions.DefaultWrite);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.JobId));

        // Verify the job was captured
        var capturedJobs = fixture.CapturingJobClient.GetEnqueued<ReplyEmbeddingBackfillJob>();
        Assert.NotEmpty(capturedJobs);

        var capturedJob = capturedJobs.First();
        Assert.Equal(TestConstants.UploadsPlaylistId, capturedJob.Job.Args[0]);
    }

    [Fact]
    public async Task BackfillEndpoint_RejectsInvalidBatchSize()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        var client = fixture.ApiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var request = new BackfillCommentEmbeddingsRequestDto
        {
            BatchSize = 0, // Invalid
            OverwriteExisting = false
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "api/replies/embeddings/backfill",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BackfillEndpoint_RejectsBatchSizeTooLarge()
    {
        // Arrange
        await fixture.CleanStateAsync();

        var video = TestHelpers.GetTargetVideo();
        await _helpers.SeedTestDataAsync(new TestDataOptions
        {
            Videos = [video]
        });

        var client = fixture.ApiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var request = new BackfillCommentEmbeddingsRequestDto
        {
            BatchSize = 501, // Exceeds max of 500
            OverwriteExisting = false
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "api/replies/embeddings/backfill",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

// DTOs for deserializing
public sealed record BackfillCommentEmbeddingsRequestDto
{
    public int? BatchSize { get; init; }
    public bool OverwriteExisting { get; init; }
}

public sealed record BackfillResponse(string JobId);

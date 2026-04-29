using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tubester.Abstractions.ApplicationConfiguration;
using Tubester.Domain;
using Tubester.IntegrationTests.TestHost;
using Tubester.Persistence;
using Xunit;

namespace Tubester.IntegrationTests;

[Collection(nameof(TestCollection))]
public class ApplicationConfigurationsTests(TestFixture fixture)
{
    private readonly JsonSerializerOptions _serializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    [Fact]
    public async Task GetAll_EmptyDb_ReturnsEmptyList()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/application-configurations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<IReadOnlyList<ApplicationConfigurationDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAll_WithConfigs_ReturnsAllConfigs()
    {
        // Arrange
        await fixture.ResetDbAsync();

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddRangeAsync(
                ApplicationConfiguration.Create(
                    "Test.Key1",
                    "Value1",
                    ConfigurationValueType.String,
                    "Description 1",
                    false,
                    TestFixture.TestingDateTimeOffset),
                ApplicationConfiguration.Create(
                    "Test.Key2",
                    "Value2",
                    ConfigurationValueType.Integer,
                    "Description 2",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/application-configurations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<IReadOnlyList<ApplicationConfigurationDto>>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetByKey_ExistingConfig_ReturnsOk()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "GetByKeyTest";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "TestValue",
                    ConfigurationValueType.String,
                    "Test description",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.GetAsync($"/api/application-configurations/{key}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(key, result.Key);
        Assert.Equal("TestValue", result.Value);
        Assert.Equal(ConfigurationValueType.String, result.ValueType);
    }

    [Fact]
    public async Task GetByKey_NonExistingConfig_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.GetAsync("/api/application-configurations/non-existent-key");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "NewKey",
            "NewValue",
            ConfigurationValueType.String,
            "New configuration",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("NewKey", result.Key);
        Assert.Equal("NewValue", result.Value);
        Assert.Equal(ConfigurationValueType.String, result.ValueType);
        Assert.Equal("New configuration", result.Description);
        Assert.False(result.IsSystem);

        // Verify config was persisted
        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var savedConfig = await dbContext.ApplicationConfigurations.FirstOrDefaultAsync(c => c.Key == "NewKey");
        Assert.NotNull(savedConfig);
        Assert.Equal("NewValue", savedConfig.Value);
    }

    [Fact]
    public async Task Create_DuplicateKey_ReturnsConflict()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "DuplicateKey";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "ExistingValue",
                    ConfigurationValueType.String,
                    "Existing description",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        var request = new CreateApplicationConfigurationRequest(
            key,
            "NewValue",
            ConfigurationValueType.String,
            "New description",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", responseContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_EmptyKey_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "",
            "Value",
            ConfigurationValueType.String,
            "Description",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Key", responseContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WhitespaceKey_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "   ",
            "Value",
            ConfigurationValueType.String,
            "Description",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ExistingConfig_ReturnsUpdated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "UpdateTestKey";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "OriginalValue",
                    ConfigurationValueType.String,
                    "Original description",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        var request = new UpdateApplicationConfigurationRequest(
            "UpdatedValue",
            ConfigurationValueType.String,
            "Updated description");
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PutAsync($"/api/application-configurations/{key}", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal(key, result.Key);
        Assert.Equal("UpdatedValue", result.Value);
        Assert.Equal(ConfigurationValueType.String, result.ValueType);
        Assert.Equal("Updated description", result.Description);

        // Verify config was updated in database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var savedConfig = await dbContext.ApplicationConfigurations.FirstOrDefaultAsync(c => c.Key == key);
        Assert.NotNull(savedConfig);
        Assert.Equal("UpdatedValue", savedConfig.Value);
        Assert.Equal(ConfigurationValueType.String, savedConfig.ValueType);
    }

    [Fact]
    public async Task Update_NonExistingConfig_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new UpdateApplicationConfigurationRequest(
            "NewValue",
            ConfigurationValueType.String,
            "Description");
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PutAsync("/api/application-configurations/non-existent-key", content);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingConfig_ReturnsNoContent()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "DeleteTestKey";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "ValueToDelete",
                    ConfigurationValueType.String,
                    "Description",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.DeleteAsync($"/api/application-configurations/{key}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify config was deleted from database
        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var deletedConfig = await dbContext.ApplicationConfigurations.FirstOrDefaultAsync(c => c.Key == key);
        Assert.Null(deletedConfig);
    }

    [Fact]
    public async Task Delete_NonExistingConfig_ReturnsNotFound()
    {
        // Arrange
        await fixture.ResetDbAsync();

        // Act
        var response = await fixture.HttpClient.DeleteAsync("/api/application-configurations/non-existent-key");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SystemConfig_ReturnsBadRequest()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "SystemConfigKey";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "SystemValue",
                    ConfigurationValueType.String,
                    "System configuration",
                    true, // IsSystem = true
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        // Act
        var response = await fixture.HttpClient.DeleteAsync($"/api/application-configurations/{key}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("system", responseContent, StringComparison.OrdinalIgnoreCase);

        // Verify config was NOT deleted
        using var verificationScope = fixture.ApiServices.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TubesterDb>();
        var savedConfig = await dbContext.ApplicationConfigurations.FirstOrDefaultAsync(c => c.Key == key);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public async Task Create_WithBooleanValue_ReturnsCreated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "Feature.Enabled",
            "true",
            ConfigurationValueType.Boolean,
            "Feature flag",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("Feature.Enabled", result.Key);
        Assert.Equal("true", result.Value);
        Assert.Equal(ConfigurationValueType.Boolean, result.ValueType);
    }

    [Fact]
    public async Task Create_WithIntegerValue_ReturnsCreated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "Cache.Duration",
            "3600",
            ConfigurationValueType.Integer,
            "Cache duration in seconds",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("Cache.Duration", result.Key);
        Assert.Equal("3600", result.Value);
        Assert.Equal(ConfigurationValueType.Integer, result.ValueType);
    }

    [Fact]
    public async Task Create_WithDecimalValue_ReturnsCreated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var request = new CreateApplicationConfigurationRequest(
            "Rate.Limit",
            "0.75",
            ConfigurationValueType.Decimal,
            "Rate limit multiplier",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("Rate.Limit", result.Key);
        Assert.Equal("0.75", result.Value);
        Assert.Equal(ConfigurationValueType.Decimal, result.ValueType);
    }

    [Fact]
    public async Task Create_WithJsonValue_ReturnsCreated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        var jsonValue = "{\"key\": \"value\", \"nested\": {\"a\": 1}}";
        var request = new CreateApplicationConfigurationRequest(
            "Settings.Json",
            jsonValue,
            ConfigurationValueType.Json,
            "JSON configuration",
            false);
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PostAsync("/api/application-configurations", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("Settings.Json", result.Key);
        Assert.Equal(jsonValue, result.Value);
        Assert.Equal(ConfigurationValueType.Json, result.ValueType);
    }

    [Fact]
    public async Task Update_ChangeValueType_ReturnsUpdated()
    {
        // Arrange
        await fixture.ResetDbAsync();
        const string key = "ChangeTypeTestKey";

        using (var scope = fixture.ApiServices.CreateScope())
        {
            var databaseContext = scope.ServiceProvider.GetRequiredService<TubesterDb>();
            await databaseContext.ApplicationConfigurations.AddAsync(
                ApplicationConfiguration.Create(
                    key,
                    "500",
                    ConfigurationValueType.Integer,
                    "Original",
                    false,
                    TestFixture.TestingDateTimeOffset));
            await databaseContext.SaveChangesAsync();
        }

        var request = new UpdateApplicationConfigurationRequest(
            "999.99",
            ConfigurationValueType.Decimal,
            "Changed to decimal");
        var json = JsonSerializer.Serialize(request, _serializerOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await fixture.HttpClient.PutAsync($"/api/application-configurations/{key}", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<ApplicationConfigurationDto>(responseContent, _serializerOptions);

        Assert.NotNull(result);
        Assert.Equal("999.99", result.Value);
        Assert.Equal(ConfigurationValueType.Decimal, result.ValueType);
    }
}
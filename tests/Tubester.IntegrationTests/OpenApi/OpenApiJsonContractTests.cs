using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tubester.IntegrationTests.OpenApi;

public class OpenApiJsonContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Historically the OpenAPI spec contained also text/plain which in orval version 7.21 let to incorrectly generated client
    /// </summary>
    [Fact]
    public async Task All_controller_endpoints_should_use_only_application_json_for_request_and_response_bodies()
    {
        using var scope = factory.Services.CreateScope();
        var apiDescriptionProvider = scope.ServiceProvider.GetRequiredService<IApiDescriptionGroupCollectionProvider>();

        var client = factory.CreateClient();
        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");

        using var swagger = JsonDocument.Parse(swaggerJson);
        var paths = swagger.RootElement.GetProperty("paths");

        var failures = new List<string>();

        var controllerApiDescriptions = apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .Where(d => d.ActionDescriptor is ControllerActionDescriptor)
            .ToList();

        foreach (var api in controllerApiDescriptions)
        {
            var action = (ControllerActionDescriptor)api.ActionDescriptor;
            var httpMethod = api.HttpMethod?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(httpMethod))
            {
                failures.Add($"{action.ControllerTypeInfo.FullName}.{action.ActionName}: missing HTTP method.");
                continue;
            }

            var openApiPath = ToOpenApiPath(api.RelativePath);
            if (!paths.TryGetProperty(openApiPath, out var pathItem))
            {
                failures.Add($"{action.ControllerTypeInfo.FullName}.{action.ActionName}: path '{openApiPath}' not found in swagger.");
                continue;
            }

            if (!pathItem.TryGetProperty(httpMethod, out var operation))
            {
                failures.Add($"{action.ControllerTypeInfo.FullName}.{action.ActionName}: method '{httpMethod}' not found for swagger path '{openApiPath}'.");
                continue;
            }

            CheckRequestBody(action, operation, failures, openApiPath, httpMethod);
            CheckResponses(action, operation, failures, openApiPath, httpMethod);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }
    
    private static void CheckRequestBody(
        ControllerActionDescriptor action,
        JsonElement operation,
        List<string> failures,
        string openApiPath,
        string httpMethod)
    {
        var bodyParameterExists = HasBodyParameter(action);

        if (!bodyParameterExists)
        {
            // No request body expected, so swagger should ideally not declare one.
            if (operation.TryGetProperty("requestBody", out _))
            {
                failures.Add(
                    $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}]: " +
                    "swagger contains requestBody, but action does not appear to accept one.");
            }

            return;
        }

        if (!operation.TryGetProperty("requestBody", out var requestBody))
        {
            failures.Add(
                $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}]: " +
                "action accepts a body, but swagger requestBody is missing.");
            return;
        }

        if (!requestBody.TryGetProperty("content", out var content))
        {
            failures.Add(
                $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}]: " +
                "requestBody.content is missing.");
            return;
        }

        var mediaTypes = content.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();

        if (mediaTypes.Length != 1 || mediaTypes[0] != "application/json")
        {
            failures.Add(
                $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}]: " +
                $"request body content types must be exactly [application/json], but were [{string.Join(", ", mediaTypes)}].");
        }
    }

    private static void CheckResponses(
        ControllerActionDescriptor action,
        JsonElement operation,
        List<string> failures,
        string openApiPath,
        string httpMethod)
    {
        if (!operation.TryGetProperty("responses", out var responses))
        {
            failures.Add(
                $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}]: responses section is missing.");
            return;
        }

        foreach (var responseProperty in responses.EnumerateObject())
        {
            var statusCode = responseProperty.Name;
            var response = responseProperty.Value;

            // Endpoints with no response body are fine.
            if (!response.TryGetProperty("content", out var content))
            {
                continue;
            }

            var mediaTypes = content.EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();

            if (mediaTypes.Length != 1 || mediaTypes[0] != "application/json")
            {
                failures.Add(
                    $"{action.ControllerTypeInfo.FullName}.{action.ActionName} [{httpMethod.ToUpperInvariant()} {openApiPath}] response {statusCode}: " +
                    $"response content types must be exactly [application/json], but were [{string.Join(", ", mediaTypes)}].");
            }
        }
    }

    private static bool HasBodyParameter(ControllerActionDescriptor action)
    {
        foreach (var parameter in action.MethodInfo.GetParameters())
        {
            if (parameter.GetCustomAttribute<FromBodyAttribute>() != null)
                return true;

            if (parameter.ParameterType == typeof(CancellationToken))
                continue;

            if (IsSimpleType(parameter.ParameterType))
                continue;

            // Under [ApiController], complex types are inferred from body unless marked otherwise.
            if (parameter.GetCustomAttribute<FromQueryAttribute>() != null ||
                parameter.GetCustomAttribute<FromRouteAttribute>() != null ||
                parameter.GetCustomAttribute<FromHeaderAttribute>() != null ||
                parameter.GetCustomAttribute<FromServicesAttribute>() != null ||
                parameter.GetCustomAttribute<FromFormAttribute>() != null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsSimpleType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(Guid)
               || type == typeof(TimeSpan)
               || type == typeof(Uri);
    }

    private static string ToOpenApiPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("ApiDescription.RelativePath was null or empty.");

        var pathWithoutQuery = relativePath.Split('?', 2)[0];
        return "/" + pathWithoutQuery.Trim('/');
    }
}
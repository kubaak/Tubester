using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace YouTubester.Api.Swagger;

/// <summary>
/// 
/// </summary>
public sealed class RequiresYouTubeWriteOperationFilter : IOperationFilter
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="operation"></param>
    /// <param name="context"></param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasWritePolicy =
            context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
                .Any(a => a.Policy == "RequiresYouTubeWrite")
            ||
            context.MethodInfo.DeclaringType?
                .GetCustomAttributes(true).OfType<AuthorizeAttribute>()
                .Any(a => a.Policy == "RequiresYouTubeWrite") == true;

        if (!hasWritePolicy)
        {
            return;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        // Custom extension visible in swagger.json
        operation.Extensions.Add("x-requires-youtube-write", new JsonNodeExtension(JsonValue.Create(true)));
    }
}
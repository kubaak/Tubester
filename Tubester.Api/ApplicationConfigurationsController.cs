using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tubester.Abstractions.ApplicationConfiguration;

namespace Tubester.Api;

/// <summary>
/// API endpoints for managing application configurations.
/// </summary>
[ApiController]
[Route("api/application-configurations")]
[Tags("ApplicationConfigurations")]
[Authorize(Policy = "AdminEmail")]
public sealed class ApplicationConfigurationsController(IApplicationConfigurationService configService)
    : ApiControllerBase
{
    /// <summary>
    /// Gets all application configurations.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApplicationConfigurationDto>>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var configs = await configService.GetAllAsync(cancellationToken);
        return Ok(configs);
    }

    /// <summary>
    /// Gets an application configuration by key.
    /// </summary>
    [HttpGet("{key}", Name = nameof(GetByKeyAsync))]
    [ProducesResponseType(typeof(ApplicationConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationConfigurationDto>> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var config = await configService.GetByKeyAsync(key, cancellationToken);
        if (config is null)
        {
            return NotFound();
        }

        return Ok(config);
    }

    /// <summary>
    /// Creates a new application configuration.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationConfigurationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationConfigurationDto>> CreateAsync(
        [FromBody] CreateApplicationConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var config = await configService.CreateAsync(request, cancellationToken);
            
        return CreatedAtRoute(
            nameof(GetByKeyAsync),
            new { key = config.Key },
            config);
    }

    /// <summary>
    /// Updates an existing application configuration.
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(ApplicationConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApplicationConfigurationDto>> UpdateAsync(
        string key,
        [FromBody] UpdateApplicationConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var config = await configService.UpdateAsync(key, request, cancellationToken);
        if (config is null)
        {
            return NotFound();
        }

        return Ok(config);
    }

    /// <summary>
    /// Deletes an application configuration.
    /// </summary>
    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var deleted = await configService.DeleteAsync(key, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}

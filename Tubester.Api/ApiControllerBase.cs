using Microsoft.AspNetCore.Mvc;

namespace Tubester.Api;

/// <summary>
/// Base Controller
/// </summary>
[ApiController]
[Produces("application/json")]
[Consumes("application/json")]
public class ApiControllerBase : ControllerBase;
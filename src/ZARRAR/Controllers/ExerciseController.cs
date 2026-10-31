using ZARRAR.Middlewares;
using Microsoft.AspNetCore.Mvc;

namespace ZARRAR.Controllers;

/// <summary>
/// Exercise related APIs
/// </summary>
[RequireUser]
[ApiController]
[Route("api/[controller]")]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ExerciseController : ControllerBase
{
    // Reserved legacy namespace. The supported event-CTF product exposes no exercise routes.
}

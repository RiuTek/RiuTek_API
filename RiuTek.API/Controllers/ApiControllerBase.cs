using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Core.Common;

namespace RiuTek.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;
    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapErrorToResponse(result.Error);
    }

    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok();
        }

        return MapErrorToResponse(result.Error);
    }

    private IActionResult MapErrorToResponse(Error error)
    {
        return error.Type switch
        {
            ErrorType.NotFound => NotFound(new { error.Code, error.Description }),
            ErrorType.Unauthorized => Unauthorized(new { error.Code, error.Description }),
            ErrorType.Forbidden => StatusCode(403, new { error.Code, error.Description }),
            ErrorType.Conflict => Conflict(new { error.Code, error.Description }),
            ErrorType.Validation => BadRequest(new { error.Code, error.Description }),
            _ => BadRequest(new { error.Code, error.Description })
        };
    }
}

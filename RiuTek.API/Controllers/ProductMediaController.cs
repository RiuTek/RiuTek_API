using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Core.Constants;

namespace RiuTek.API.Controllers;

[Route("api/v1/product-media")]
[Authorize(Policy = Policies.ContentManager)]
public class ProductMediaController : ApiControllerBase
{
    [HttpPost("upload-requests")]
    [ProducesResponseType(typeof(ProductMediaUploadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateUploadRequest(
        [FromBody] CreateProductMediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new CreateProductMediaUploadCommand(
                request.FileName,
                request.ContentType,
                request.ContentLength),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("confirmations")]
    [ProducesResponseType(typeof(ProductMediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ConfirmUpload(
        [FromBody] ConfirmProductMediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new ConfirmProductMediaUploadCommand(request.ObjectKey),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DeleteMedia(
        [FromBody] DeleteProductMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new DeleteProductMediaCommand(request.ObjectKey),
            cancellationToken);

        return HandleNoContentResult(result);
    }
}

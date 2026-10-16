using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Constants;

namespace RiuTek.API.Controllers;

[Route("api/v1/product-imports")]
[Authorize(Policy = Policies.ContentManager)]
public class ProductImportsController : ApiControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MiB = 5242880 bytes

    [HttpGet("template")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadTemplate(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetProductImportTemplateQuery(), cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("validate")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MiB to accommodate multipart headers
    [ProducesResponseType(typeof(ProductImportValidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateImport(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        var fileError = ValidateFile(file);
        if (fileError is not null)
        {
            return fileError;
        }

        await using var stream = file!.OpenReadStream();
        var result = await Mediator.Send(
            new ValidateProductImportCommand(stream, file.FileName, file.Length),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType(typeof(ProductImportCommitResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProductImportCommitResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Import(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        var fileError = ValidateFile(file);
        if (fileError is not null)
        {
            return fileError;
        }

        await using var stream = file!.OpenReadStream();
        var result = await Mediator.Send(
            new ImportProductsCommand(stream, file.FileName, file.Length),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        return result.Value.IsCommitted
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Ok(result.Value);
    }

    private IActionResult? ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                Code = "ProductImport.MissingFile",
                Description = "File upload không được để trống."
            });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                Code = "ProductImport.InvalidFileExtension",
                Description = "Chỉ chấp nhận file định dạng .xlsx."
            });
        }

        return file.Length > MaxFileSizeBytes
            ? BadRequest(new
            {
                Code = "ProductImport.FileTooLarge",
                Description = "Dung lượng file không được vượt quá 5 MiB."
            })
            : null;
    }
}

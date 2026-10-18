using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;

namespace RiuTek.API.Controllers;

public class ProductsController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] ProductListRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new GetProductsQuery(request.ToFilterOptions());
        var result = await Mediator.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("slug/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetProductBySlugQuery(slug), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetProductByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateProductCommand(
            CategoryId: request.CategoryId,
            Name: request.Name,
            Sku: request.Sku,
            Brand: request.Brand,
            Price: request.Price,
            OriginalPrice: request.OriginalPrice,
            StockQuantity: request.StockQuantity,
            ImageUrl: request.ImageUrl,
            AdditionalImages: request.AdditionalImages,
            ComponentType: request.ComponentType,
            Specifications: request.Specifications
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleCreatedResult(result, nameof(GetById), result.IsSuccess ? new { id = result.Value.Id } : null);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateProductCommand(
            Id: id,
            CategoryId: request.CategoryId,
            Name: request.Name,
            Sku: request.Sku,
            Brand: request.Brand,
            Price: request.Price,
            OriginalPrice: request.OriginalPrice,
            IsActive: request.IsActive,
            ImageUrl: request.ImageUrl,
            AdditionalImages: request.AdditionalImages,
            ComponentType: request.ComponentType,
            Specifications: request.Specifications
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/stock-adjustments")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(StockAdjustmentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdjustStock(
        Guid id,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new AdjustStockCommand(
            ProductId: id,
            ExpectedStockQuantity: request.ExpectedStockQuantity,
            QuantityDelta: request.QuantityDelta,
            Note: request.Note
        );

        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}/stock-movements")]
    [Authorize(Policy = Policies.ContentManager)]
    [ProducesResponseType(typeof(PagedResult<StockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStockMovements(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] StockMovementReason? reason = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetStockMovementsQuery(
            ProductId: id,
            PageIndex: pageIndex,
            PageSize: pageSize,
            Reason: reason
        );

        var result = await Mediator.Send(query, cancellationToken);
        return HandleResult(result);
    }
}

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Reviews.Commands;

public record CreateReviewCommand(
    Guid OrderId,
    Guid ProductId,
    int Rating,
    string? Title = null,
    string? Content = null
) : IRequest<Result<MyReviewDto>>;

public class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId is required.");

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.");

        RuleFor(x => x.Title)
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.")
            .When(x => !string.IsNullOrEmpty(x.Title));

        RuleFor(x => x.Content)
            .MaximumLength(2000).WithMessage("Content cannot exceed 2000 characters.")
            .When(x => !string.IsNullOrEmpty(x.Content));
    }
}

public class CreateReviewCommandHandler : IRequestHandler<CreateReviewCommand, Result<MyReviewDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CreateReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<MyReviewDto>> Handle(
        CreateReviewCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<MyReviewDto>(authResult.Error);
        }

        var customer = authResult.Value;

        var product = await _context.Products
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.Slug, p.ImageUrl })
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product is null)
        {
            return Result.Failure<MyReviewDto>(Error.NotFound(
                "Review.ProductNotFound",
                "Product was not found."));
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == customer.Id, cancellationToken);

        if (order is null)
        {
            return Result.Failure<MyReviewDto>(Error.NotFound(
                "Review.OrderNotFound",
                "Order was not found."));
        }

        if (order.Status != OrderStatus.Completed)
        {
            return Result.Failure<MyReviewDto>(Error.Conflict(
                "Review.OrderNotCompleted",
                "Reviews can only be submitted for completed orders."));
        }

        var hasPurchased = order.Items.Any(i => i.ProductId == request.ProductId);
        if (!hasPurchased)
        {
            return Result.Failure<MyReviewDto>(Error.Conflict(
                "Review.ProductNotPurchased",
                "Product was not part of this completed order."));
        }

        var exists = await _context.Reviews
            .AsNoTracking()
            .AnyAsync(r => r.UserId == customer.Id && r.ProductId == request.ProductId && r.OrderId == request.OrderId, cancellationToken);

        if (exists)
        {
            return Result.Failure<MyReviewDto>(Error.Conflict(
                "Review.AlreadyExists",
                "A review has already been submitted for this order item."));
        }

        var review = new Review(
            productId: request.ProductId,
            userId: customer.Id,
            orderId: request.OrderId,
            rating: request.Rating,
            title: request.Title,
            content: request.Content,
            images: []);

        _context.Reviews.Add(review);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (_context.IsUniqueViolation(
                ex,
                "IX_Reviews_UserId_ProductId_OrderId"))
        {
            _context.ClearTrackedChanges();
            return Result.Failure<MyReviewDto>(Error.Conflict(
                "Review.AlreadyExists",
                "A review has already been submitted for this order item."));
        }

        return Result.Success(new MyReviewDto(
            review.Id,
            product.Id,
            product.Name,
            product.Slug,
            product.ImageUrl,
            review.OrderId,
            review.Rating,
            review.Title,
            review.Content,
            review.Images,
            review.StaffReply,
            review.StaffReplyAt,
            review.CreatedAt,
            review.UpdatedAt
        ));
    }
}

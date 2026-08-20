using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class EcommerceMappingExtensions
{
    public static UserAddressDto ToDto(this UserAddress address) => new(
        address.Id,
        address.UserId,
        address.ReceiverName,
        address.PhoneNumber,
        address.AddressLine,
        address.Ward,
        address.District,
        address.City,
        address.IsDefault
    );

    public static ReviewDto ToDto(this Review review) => new(
        review.Id,
        review.ProductId,
        review.UserId,
        review.User?.FullName ?? string.Empty,
        review.OrderId,
        review.Rating,
        review.Title,
        review.Content,
        review.Images,
        review.StaffReply,
        review.StaffReplyAt,
        review.CreatedAt
    );

    public static CommentDto ToDto(this Comment comment) => new(
        comment.Id,
        comment.ProductId,
        comment.UserId,
        comment.User?.FullName ?? string.Empty,
        comment.Content,
        comment.ParentCommentId,
        comment.IsStaffAnswer,
        comment.Replies.Select(r => r.ToDto()).ToList(),
        comment.CreatedAt
    );

    public static WishlistDto ToDto(this Wishlist wishlist) => new(
        wishlist.Id,
        wishlist.UserId,
        wishlist.ProductId,
        wishlist.Product?.Name ?? string.Empty,
        wishlist.Product?.Price ?? 0,
        wishlist.Product?.ImageUrl ?? string.Empty,
        wishlist.CreatedAt
    );
}

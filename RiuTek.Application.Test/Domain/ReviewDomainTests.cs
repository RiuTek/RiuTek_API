using FluentAssertions;
using RiuTek.Core.Entities;
using Xunit;

namespace RiuTek.Application.Test.Domain;

public class ReviewDomainTests
{
    [Fact]
    public void Constructor_WithValidData_InitializesCorrectly()
    {
        var productId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var review = new Review(productId, userId, orderId, 5, " Great title ", " Excellent product! ");

        review.ProductId.Should().Be(productId);
        review.UserId.Should().Be(userId);
        review.OrderId.Should().Be(orderId);
        review.Rating.Should().Be(5);
        review.Title.Should().Be("Great title");
        review.Content.Should().Be("Excellent product!");
        review.Images.Should().BeEmpty();
        review.StaffReply.Should().BeNull();
        review.StaffReplyAt.Should().BeNull();
        review.UpdatedAt.Should().BeNull();
        review.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_WithRatingOnly_AllowsNullOrWhitespaceTitleAndContent()
    {
        var productId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var review = new Review(productId, userId, orderId, 4, "   ", null);

        review.Rating.Should().Be(4);
        review.Title.Should().BeNull();
        review.Content.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Constructor_WithInvalidRating_ThrowsArgumentOutOfRangeException(int invalidRating)
    {
        var act = () => new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), invalidRating, "Title", "Content");

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Rating must be between 1 and 5*");
    }

    [Fact]
    public void Constructor_WithEmptyIds_ThrowsArgumentException()
    {
        var act1 = () => new Review(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 5, null, null);
        act1.Should().Throw<ArgumentException>().WithMessage("*ProductId*");

        var act2 = () => new Review(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), 5, null, null);
        act2.Should().Throw<ArgumentException>().WithMessage("*UserId*");

        var act3 = () => new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 5, null, null);
        act3.Should().Throw<ArgumentException>().WithMessage("*OrderId*");
    }

    [Fact]
    public void Constructor_WithTooLongTitleOrContent_ThrowsArgumentException()
    {
        var longTitle = new string('a', 201);
        var actTitle = () => new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, longTitle, null);
        actTitle.Should().Throw<ArgumentException>().WithMessage("*Text cannot exceed 200*");

        var longContent = new string('b', 2001);
        var actContent = () => new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, null, longContent);
        actContent.Should().Throw<ArgumentException>().WithMessage("*Text cannot exceed 2000*");
    }

    [Fact]
    public void UpdateReview_WithValidValues_UpdatesFieldsAndSetsUpdatedAt()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Old Title", "Old Content");
        var beforeUpdate = DateTime.UtcNow;

        var result = review.UpdateReview(5, "  New Title  ", "  New Content  ");

        result.IsSuccess.Should().BeTrue();
        review.Rating.Should().Be(5);
        review.Title.Should().Be("New Title");
        review.Content.Should().Be("New Content");
        review.UpdatedAt.Should().NotBeNull();
        review.UpdatedAt!.Value.Should().BeOnOrAfter(beforeUpdate);
    }

    [Fact]
    public void UpdateReview_CanClearTitleAndContent()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Old Title", "Old Content");

        var result = review.UpdateReview(4, "   ", null);

        result.IsSuccess.Should().BeTrue();
        review.Rating.Should().Be(4);
        review.Title.Should().BeNull();
        review.Content.Should().BeNull();
        review.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void UpdateReview_WithInvalidRating_ReturnsFailure(int invalidRating)
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Old", "Old");

        var result = review.UpdateReview(invalidRating, "Title", "Content");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Review.InvalidRating");
    }

    [Fact]
    public void SetStaffReply_WithValidReply_SetsReplyAndTimestamp()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "Title", "Content");
        var beforeReply = DateTime.UtcNow;

        var result = review.SetStaffReply("  Thank you for your feedback!  ");

        result.IsSuccess.Should().BeTrue();
        review.StaffReply.Should().Be("Thank you for your feedback!");
        review.StaffReplyAt.Should().NotBeNull();
        review.StaffReplyAt!.Value.Should().BeOnOrAfter(beforeReply);
        review.UpdatedAt.Should().NotBeNull();
        review.UpdatedAt!.Value.Should().BeOnOrAfter(beforeReply);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetStaffReply_WithEmptyReply_ReturnsFailure(string? invalidReply)
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "Title", "Content");

        var result = review.SetStaffReply(invalidReply!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Review.InvalidReply");
    }

    [Fact]
    public void SetStaffReply_ExceedingLength_ReturnsFailure()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "Title", "Content");
        var longReply = new string('x', 2001);

        var result = review.SetStaffReply(longReply);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Review.InvalidReply");
    }

    [Fact]
    public void ClearStaffReply_ClearsReplyAndUpdatesTimestamp()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, "Title", "Content");
        review.SetStaffReply("Feedback reply");

        var beforeClear = DateTime.UtcNow;
        var result = review.ClearStaffReply();

        result.IsSuccess.Should().BeTrue();
        review.StaffReply.Should().BeNull();
        review.StaffReplyAt.Should().BeNull();
        review.UpdatedAt.Should().NotBeNull();
        review.UpdatedAt!.Value.Should().BeOnOrAfter(beforeClear);
    }

    [Fact]
    public void Constructor_WithNullImages_InitializesEmptyList()
    {
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "Title", "Content", null);
        review.Images.Should().NotBeNull();
        review.Images.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithImagesContainingWhitespaceAndNulls_TrimsAndFiltersEmpty()
    {
        var rawImages = new List<string?> { "  https://img.test/1.jpg  ", null, "", "   ", "https://img.test/2.jpg" }!;
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "Title", "Content", rawImages!);

        review.Images.Should().HaveCount(2);
        review.Images[0].Should().Be("https://img.test/1.jpg");
        review.Images[1].Should().Be("https://img.test/2.jpg");
    }

    [Fact]
    public void Constructor_WithDuplicateImages_RemovesDuplicatesUsingOrdinalComparisonAndPreservesOrder()
    {
        var images = new List<string>
        {
            "https://img.test/first.jpg",
            "https://img.test/second.jpg",
            "  https://img.test/first.jpg  ",
            "https://img.test/third.jpg",
            "https://img.test/second.jpg"
        };

        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "Title", "Content", images);

        review.Images.Should().HaveCount(3);
        review.Images.Should().ContainInOrder(
            "https://img.test/first.jpg",
            "https://img.test/second.jpg",
            "https://img.test/third.jpg");
    }

    [Fact]
    public void Constructor_WithImages_PerformsDefensiveCopy()
    {
        var originalList = new List<string> { "https://img.test/1.jpg", "https://img.test/2.jpg" };
        var review = new Review(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, "Title", "Content", originalList);

        // Mutate original list
        originalList.Add("https://img.test/3.jpg");
        originalList[0] = "https://img.test/mutated.jpg";

        review.Images.Should().HaveCount(2);
        review.Images[0].Should().Be("https://img.test/1.jpg");
        review.Images[1].Should().Be("https://img.test/2.jpg");
    }
}

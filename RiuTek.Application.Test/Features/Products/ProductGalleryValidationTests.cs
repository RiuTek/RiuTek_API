using FluentAssertions;
using FluentValidation.TestHelper;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Application.Features.Products.Validation;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class ProductGalleryValidationTests
{
    private readonly CreateProductCommandValidator _createValidator = new();
    private readonly UpdateProductCommandValidator _updateValidator = new();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ftp://example.com/img.jpg", false)]
    [InlineData("http://example.com/img.jpg", false)]
    [InlineData("not-a-url", false)]
    [InlineData("/images/cpu.jpg", false)]
    [InlineData("https://", false)]
    [InlineData("https://example.com/img.jpg", true)]
    [InlineData("https://media.riutek.com/products/2026/10/test.webp", true)]
    public void IsValidHttpsUrl_ValidatesSchemeAndFormat(string? url, bool expected)
    {
        ProductMediaHelper.IsValidHttpsUrl(url).Should().Be(expected);
    }

    [Fact]
    public void NormalizeAdditionalImages_RemovesDuplicates_TrimsWhitespace_AndExcludesPrimary()
    {
        const string primary = "https://media.riutek.com/products/main.jpg";
        var additional = new List<string>
        {
            " https://media.riutek.com/products/main.jpg ",      // Duplicate of primary
            "https://media.riutek.com/products/gallery-1.jpg",
            "https://media.riutek.com/products/gallery-1.jpg",    // Duplicate of gallery-1
            " https://media.riutek.com/products/gallery-2.jpg  ",
            ""                                                    // Empty
        };

        var result = ProductMediaHelper.NormalizeAdditionalImages(primary, additional);

        result.Should().HaveCount(2);
        result.Should().ContainInOrder(
            "https://media.riutek.com/products/gallery-1.jpg",
            "https://media.riutek.com/products/gallery-2.jpg");
    }

    [Theory]
    [InlineData("http://insecure.com/pic.jpg")]
    [InlineData("relative/path/pic.jpg")]
    [InlineData("ftp://server.com/pic.jpg")]
    public void CreateProductValidator_WhenImageUrlIsNotHttps_Fails(string invalidUrl)
    {
        var command = new CreateProductCommand(
            Guid.NewGuid(), "CPU", "SKU1", "Intel", 100, null, 10,
            invalidUrl, null, ComponentType.Cpu, ProductCommandValidatorTests.CreateValidCpuSpec());

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ImageUrl);
    }

    [Fact]
    public void CreateProductValidator_WhenAdditionalImagesExceeds10_Fails()
    {
        var additional = Enumerable.Range(1, 11)
            .Select(i => $"https://media.riutek.com/products/{i}.jpg")
            .ToList();

        var command = new CreateProductCommand(
            Guid.NewGuid(), "CPU", "SKU1", "Intel", 100, null, 10,
            "https://media.riutek.com/products/main.jpg",
            additional,
            ComponentType.Cpu,
            ProductCommandValidatorTests.CreateValidCpuSpec());

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AdditionalImages);
    }

    [Fact]
    public void CreateProductValidator_WhenAdditionalImagesContainsInvalidUrl_Fails()
    {
        var additional = new List<string>
        {
            "https://media.riutek.com/products/valid.jpg",
            "http://insecure.com/invalid.jpg"
        };

        var command = new CreateProductCommand(
            Guid.NewGuid(), "CPU", "SKU1", "Intel", 100, null, 10,
            "https://media.riutek.com/products/main.jpg",
            additional,
            ComponentType.Cpu,
            ProductCommandValidatorTests.CreateValidCpuSpec());

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AdditionalImages);
    }

    [Fact]
    public void CreateProductValidator_WhenAdditionalImagesDuplicatesPrimary_Fails()
    {
        const string primary = "https://media.riutek.com/products/main.jpg";
        var additional = new List<string> { primary };

        var command = new CreateProductCommand(
            Guid.NewGuid(), "CPU", "SKU1", "Intel", 100, null, 10,
            primary,
            additional,
            ComponentType.Cpu,
            ProductCommandValidatorTests.CreateValidCpuSpec());

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AdditionalImages);
    }

    [Fact]
    public void UpdateProductValidator_WhenGalleryValid_Passes()
    {
        var command = new UpdateProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Updated CPU", "SKU1", "Intel", 150, null, 5, true,
            "https://media.riutek.com/products/main.jpg",
            ["https://media.riutek.com/products/gallery-1.jpg"],
            ComponentType.Cpu,
            ProductCommandValidatorTests.CreateValidCpuSpec());

        var result = _updateValidator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(x => x.ImageUrl);
        result.ShouldNotHaveValidationErrorFor(x => x.AdditionalImages);
    }
}

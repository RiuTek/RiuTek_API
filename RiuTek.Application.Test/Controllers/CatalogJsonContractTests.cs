using System.Text.Json;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Features.Products;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Controllers;

public class CatalogJsonContractTests
{
    private readonly JsonSerializerOptions _jsonOptions;

    public CatalogJsonContractTests()
    {
        var services = new ServiceCollection();
        services.AddControllers();
        using var sp = services.BuildServiceProvider();
        _jsonOptions = sp.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
    }

    private static (ComponentType componentType, ComponentSpecification spec) GetSpecForType(string discriminator) =>
        discriminator switch
        {
            "cpu" => (ComponentType.Cpu, ProductCommandValidatorTests.CreateValidCpuSpec()),
            "motherboard" => (ComponentType.Motherboard, ProductCommandValidatorTests.CreateValidMotherboardSpec()),
            "gpu" => (ComponentType.Gpu, ProductCommandValidatorTests.CreateValidGpuSpec()),
            "ram" => (ComponentType.Ram, ProductCommandValidatorTests.CreateValidRamSpec()),
            "storage" => (ComponentType.Storage, ProductCommandValidatorTests.CreateValidStorageSpec()),
            "psu" => (ComponentType.Psu, ProductCommandValidatorTests.CreateValidPsuSpec()),
            "case" => (ComponentType.Case, ProductCommandValidatorTests.CreateValidCaseSpec()),
            "cooler" => (ComponentType.Cooler, ProductCommandValidatorTests.CreateValidAirCoolerSpec()),
            "accessory" => (ComponentType.Accessory, ProductCommandValidatorTests.CreateValidAccessorySpec()),
            _ => throw new ArgumentException($"Unknown discriminator {discriminator}")
        };

    #region 1. CreateProductRequest Polymorphic Deserialization for all 9 subtypes

    [Theory]
    [InlineData("cpu", typeof(CpuSpecification))]
    [InlineData("motherboard", typeof(MotherboardSpecification))]
    [InlineData("gpu", typeof(GpuSpecification))]
    [InlineData("ram", typeof(RamSpecification))]
    [InlineData("storage", typeof(StorageSpecification))]
    [InlineData("psu", typeof(PsuSpecification))]
    [InlineData("case", typeof(CaseSpecification))]
    [InlineData("cooler", typeof(CoolerSpecification))]
    [InlineData("accessory", typeof(AccessorySpecification))]
    public void CreateProductRequest_DeserializesAll9ComponentSpecifications_Polymorphically(string discriminator, Type expectedType)
    {
        var (componentType, originalSpec) = GetSpecForType(discriminator);
        var originalRequest = new CreateProductRequest(
            CategoryId: Guid.NewGuid(),
            Name: $"Test {discriminator}",
            Sku: $"SKU-{discriminator.ToUpperInvariant()}",
            Brand: "RiuTek",
            Price: 199.99m,
            OriginalPrice: 249.99m,
            StockQuantity: 15,
            ImageUrl: "https://example.com/img.jpg",
            AdditionalImages: ["https://example.com/extra1.jpg"],
            ComponentType: componentType,
            Specifications: originalSpec
        );

        var json = JsonSerializer.Serialize(originalRequest, _jsonOptions);
        json.Should().Contain($"\"$type\":\"{discriminator}\"");

        var deserialized = JsonSerializer.Deserialize<CreateProductRequest>(json, _jsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.ComponentType.Should().Be(componentType);
        deserialized.Specifications.Should().NotBeNull();
        deserialized.Specifications.Should().BeOfType(expectedType);
        deserialized.Specifications.Should().BeEquivalentTo(originalSpec);

        // Map to command without data loss
        var command = new CreateProductCommand(
            CategoryId: deserialized.CategoryId,
            Name: deserialized.Name,
            Sku: deserialized.Sku,
            Brand: deserialized.Brand,
            Price: deserialized.Price,
            OriginalPrice: deserialized.OriginalPrice,
            StockQuantity: deserialized.StockQuantity,
            ImageUrl: deserialized.ImageUrl,
            AdditionalImages: deserialized.AdditionalImages,
            ComponentType: deserialized.ComponentType,
            Specifications: deserialized.Specifications
        );

        command.Specifications.Should().BeEquivalentTo(originalSpec);
    }

    #endregion

    #region 2. ProductDto Round-Trip for all 9 subtypes

    [Theory]
    [InlineData("cpu", typeof(CpuSpecification))]
    [InlineData("motherboard", typeof(MotherboardSpecification))]
    [InlineData("gpu", typeof(GpuSpecification))]
    [InlineData("ram", typeof(RamSpecification))]
    [InlineData("storage", typeof(StorageSpecification))]
    [InlineData("psu", typeof(PsuSpecification))]
    [InlineData("case", typeof(CaseSpecification))]
    [InlineData("cooler", typeof(CoolerSpecification))]
    [InlineData("accessory", typeof(AccessorySpecification))]
    public void ProductDto_RoundTripsAll9ComponentSpecifications_PreservingDiscriminatorAndDerivedFields(string discriminator, Type expectedType)
    {
        var (componentType, originalSpec) = GetSpecForType(discriminator);
        var productDto = new ProductDto(
            Id: Guid.NewGuid(),
            CategoryId: Guid.NewGuid(),
            CategoryName: "Category",
            Name: $"Product {discriminator}",
            Slug: $"product-{discriminator}",
            Sku: $"SKU-{discriminator}",
            Brand: "Brand",
            Price: 500m,
            OriginalPrice: 600m,
            StockQuantity: 8,
            IsActive: true,
            ImageUrl: "main.jpg",
            AdditionalImages: ["alt1.jpg", "alt2.jpg"],
            ComponentType: componentType,
            Specifications: originalSpec,
            CreatedAt: DateTime.UtcNow.AddDays(-5)
        );

        var json = JsonSerializer.Serialize(productDto, _jsonOptions);
        json.Should().Contain($"\"$type\":\"{discriminator}\"");

        var roundTripped = JsonSerializer.Deserialize<ProductDto>(json, _jsonOptions);

        roundTripped.Should().NotBeNull();
        roundTripped!.Specifications.Should().BeOfType(expectedType);
        roundTripped.Specifications.Should().BeEquivalentTo(originalSpec);
        roundTripped.ComponentType.Should().Be(componentType);
    }

    #endregion

    #region 3. UpdateProductRequest IsActive Wire Contract

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateProductRequest_DeserializesExplicitIsActive_Correctly(bool isActive)
    {
        var json = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Updated CPU",
            "sku": "CPU-UPD",
            "brand": "Intel",
            "price": 300,
            "originalPrice": null,
            "stockQuantity": 10,
            "isActive": {{isActive.ToString().ToLowerInvariant()}},
            "imageUrl": "img.jpg",
            "additionalImages": null,
            "componentType": 1,
            "specifications": {
                "$type": "cpu",
                "socket": 1,
                "coreCount": 8,
                "threadCount": 16,
                "baseClockGhz": 3.6,
                "boostClockGhz": 5.0,
                "tdpWattage": 65,
                "hasIntegratedGpu": true,
                "supportedMemoryType": 1,
                "maxMemorySpeedMhz": 5600
            }
        }
        """;

        var request = JsonSerializer.Deserialize<UpdateProductRequest>(json, _jsonOptions);

        request.Should().NotBeNull();
        request!.IsActive.Should().Be(isActive);
    }

    [Fact]
    public void UpdateProductRequest_WhenIsActiveMissing_ThrowsJsonException()
    {
        var json = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Updated CPU",
            "sku": "CPU-UPD",
            "brand": "Intel",
            "price": 300,
            "stockQuantity": 10,
            "imageUrl": "img.jpg",
            "componentType": 1,
            "specifications": {
                "$type": "cpu",
                "socket": 1,
                "coreCount": 8,
                "threadCount": 16,
                "baseClockGhz": 3.6,
                "boostClockGhz": 5.0,
                "tdpWattage": 65,
                "hasIntegratedGpu": true,
                "supportedMemoryType": 1,
                "maxMemorySpeedMhz": 5600
            }
        }
        """;

        var act = () => JsonSerializer.Deserialize<UpdateProductRequest>(json, _jsonOptions);

        act.Should().Throw<JsonException>("missing [property: JsonRequired] IsActive must be rejected by serializer");
    }

    [Fact]
    public void UpdateProductRequest_WhenIsActiveNull_ThrowsJsonException()
    {
        var json = $$"""
        {
            "categoryId": "{{Guid.NewGuid()}}",
            "name": "Updated CPU",
            "sku": "CPU-UPD",
            "brand": "Intel",
            "price": 300,
            "stockQuantity": 10,
            "isActive": null,
            "imageUrl": "img.jpg",
            "componentType": 1,
            "specifications": {
                "$type": "cpu",
                "socket": 1,
                "coreCount": 8,
                "threadCount": 16,
                "baseClockGhz": 3.6,
                "boostClockGhz": 5.0,
                "tdpWattage": 65,
                "hasIntegratedGpu": true,
                "supportedMemoryType": 1,
                "maxMemorySpeedMhz": 5600
            }
        }
        """;

        var act = () => JsonSerializer.Deserialize<UpdateProductRequest>(json, _jsonOptions);

        act.Should().Throw<JsonException>("null value for non-nullable boolean [JsonRequired] must be rejected");
    }

    #endregion

    #region 4. Unknown or Missing Discriminator Rejection

    [Fact]
    public void Deserialize_WhenUnknownDiscriminator_ThrowsJsonExceptionOrNotSupportedException()
    {
        var json = """
        {
            "$type": "quantum_core",
            "qubitCount": 128
        }
        """;

        var act = () => JsonSerializer.Deserialize<ComponentSpecification>(json, _jsonOptions);

        act.Should().Throw<Exception>()
            .Where(e => e is JsonException || e is NotSupportedException);
    }

    [Fact]
    public void Deserialize_WhenMissingDiscriminator_ThrowsJsonExceptionOrNotSupportedException()
    {
        var json = """
        {
            "coreCount": 8,
            "threadCount": 16
        }
        """;

        var act = () => JsonSerializer.Deserialize<ComponentSpecification>(json, _jsonOptions);

        act.Should().Throw<Exception>()
            .Where(e => e is JsonException || e is NotSupportedException);
    }

    #endregion

    #region 5. Null Specifications Handling at Boundary & Validator

    [Fact]
    public void CreateProductRequest_WhenSpecificationsIsNull_IsCaughtByCommandValidator()
    {
        var command = new CreateProductCommand(
            CategoryId: Guid.NewGuid(),
            Name: "No Spec CPU",
            Sku: "CPU-NOSPEC",
            Brand: "Brand",
            Price: 200,
            OriginalPrice: null,
            StockQuantity: 10,
            ImageUrl: "img.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: null!
        );

        var validator = new CreateProductCommandValidator();
        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Specifications);
    }

    #endregion
}

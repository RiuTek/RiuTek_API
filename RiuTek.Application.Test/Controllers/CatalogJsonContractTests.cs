using System.Reflection;
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
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
                resolver.Modifiers.Add(typeInfo =>
                {
                    if (typeInfo.Type == typeof(ComponentSpecification))
                    {
                        typeInfo.PolymorphismOptions = null;
                    }
                });
                options.JsonSerializerOptions.TypeInfoResolver = resolver;
                options.JsonSerializerOptions.Converters.Add(new RiuTek.API.Serialization.ComponentSpecificationJsonConverter());
                options.AllowInputFormatterExceptionMessages = false;
            });
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

    private static void AssertSpecificationsEquivalent(ComponentSpecification? actual, ComponentSpecification? expected)
    {
        actual.Should().NotBeNull();
        expected.Should().NotBeNull();
        actual.Should().BeOfType(expected!.GetType());
        actual.Should().BeEquivalentTo(expected, options => options
            .PreferringRuntimeMemberTypes()
            .ComparingRecordsByMembers()
            .WithStrictOrdering());
    }

    private static (ComponentSpecification expected, ComponentSpecification actual, string modifiedFieldName) CreateCorruptedSpecificationPair(string discriminator)
    {
        var (_, original) = GetSpecForType(discriminator);
        return discriminator switch
        {
            "cpu" => (original, ((CpuSpecification)original) with { CoreCount = ((CpuSpecification)original).CoreCount + 4 }, nameof(CpuSpecification.CoreCount)),
            "motherboard" => (original, ((MotherboardSpecification)original) with { M2Slots = ((MotherboardSpecification)original).M2Slots + 1 }, nameof(MotherboardSpecification.M2Slots)),
            "gpu" => (original, ((GpuSpecification)original) with { VramGb = ((GpuSpecification)original).VramGb + 4 }, nameof(GpuSpecification.VramGb)),
            "ram" => (original, ((RamSpecification)original) with { CapacityGb = ((RamSpecification)original).CapacityGb * 2 }, nameof(RamSpecification.CapacityGb)),
            "storage" => (original, ((StorageSpecification)original) with { ReadSpeedMBs = ((StorageSpecification)original).ReadSpeedMBs + 500 }, nameof(StorageSpecification.ReadSpeedMBs)),
            "psu" => (original, ((PsuSpecification)original) with { Wattage = ((PsuSpecification)original).Wattage + 100 }, nameof(PsuSpecification.Wattage)),
            "case" => (original, ((CaseSpecification)original) with { MaxGpuLengthMm = ((CaseSpecification)original).MaxGpuLengthMm + 20 }, nameof(CaseSpecification.MaxGpuLengthMm)),
            "cooler" => (original, ((CoolerSpecification)original) with { MaxTdpRating = ((CoolerSpecification)original).MaxTdpRating + 50 }, nameof(CoolerSpecification.MaxTdpRating)),
            "accessory" => (original, ((AccessorySpecification)original) with { Details = "Corrupted accessory details" }, nameof(AccessorySpecification.Details)),
            _ => throw new ArgumentException($"Unknown discriminator {discriminator}")
        };
    }

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
        AssertSpecificationsEquivalent(deserialized.Specifications, originalSpec);

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

        AssertSpecificationsEquivalent(command.Specifications, originalSpec);
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
        AssertSpecificationsEquivalent(roundTripped.Specifications, originalSpec);
        roundTripped.ComponentType.Should().Be(componentType);
    }

    #endregion

    #region 3. Negative-Control Regression for Assertion Equivalency

    [Theory]
    [InlineData("cpu", "CoreCount")]
    [InlineData("motherboard", "M2Slots")]
    [InlineData("gpu", "VramGb")]
    [InlineData("ram", "CapacityGb")]
    [InlineData("storage", "ReadSpeedMBs")]
    [InlineData("psu", "Wattage")]
    [InlineData("case", "MaxGpuLengthMm")]
    [InlineData("cooler", "MaxTdpRating")]
    [InlineData("accessory", "Details")]
    public void AssertSpecificationsEquivalent_WhenDerivedFieldDiffers_DetectsCorruptionAndMentionsField(string discriminator, string expectedFieldName)
    {
        var (expectedSpec, actualSpec, modifiedFieldName) = CreateCorruptedSpecificationPair(discriminator);
        modifiedFieldName.Should().Be(expectedFieldName);

        using var scope = new FluentAssertions.Execution.AssertionScope();
        AssertSpecificationsEquivalent(actualSpec, expectedSpec);
        var failures = scope.Discard();

        failures.Should().NotBeEmpty(
            $"Equivalency assertion must reject corrupted {discriminator} specification when {expectedFieldName} is changed");
        failures.Should().Contain(f => f.Contains(expectedFieldName, StringComparison.OrdinalIgnoreCase),
            $"Failure message must mention the modified field '{expectedFieldName}'. Actual failures: {string.Join("; ", failures)}");
    }

    #endregion

    #region 4. UpdateProductRequest IsActive Wire Contract

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

    #region 5. Unknown or Missing Discriminator Rejection

    [Fact]
    public void Deserialize_WhenUnknownDiscriminator_ThrowsJsonException()
    {
        var json = """
        {
            "$type": "quantum_core",
            "qubitCount": 128
        }
        """;

        var act = () => JsonSerializer.Deserialize<ComponentSpecification>(json, _jsonOptions);

        act.Should().Throw<JsonException>("unknown $type discriminator must be rejected with JsonException");
    }

    [Fact]
    public void Deserialize_WhenMissingDiscriminator_ThrowsJsonException()
    {
        var json = """
        {
            "coreCount": 8,
            "threadCount": 16
        }
        """;

        var act = () => JsonSerializer.Deserialize<ComponentSpecification>(json, _jsonOptions);

        act.Should().Throw<JsonException>("missing polymorphic $type discriminator must be rejected with JsonException");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Deserialize_WhenInvalidDiscriminatorFormat_ThrowsJsonException(string typeValue)
    {
        var json = $$"""
        {
            "$type": {{typeValue}},
            "coreCount": 8
        }
        """;

        var act = () => JsonSerializer.Deserialize<ComponentSpecification>(json, _jsonOptions);

        act.Should().Throw<JsonException>("invalid discriminator format must be rejected with JsonException");
    }

    private record UnsupportedSpecification : ComponentSpecification
    {
        public override ComponentType ComponentType => (ComponentType)999;
    }

    [Fact]
    public void Serialize_WhenUnsupportedSpecificationSubtype_ThrowsJsonException()
    {
        var unsupported = new UnsupportedSpecification();

        var act = () => JsonSerializer.Serialize<ComponentSpecification>(unsupported, _jsonOptions);

        act.Should().Throw<JsonException>("unsupported derived runtime type must fail clearly on write");
    }

    #endregion

    #region 6. Null Specifications Handling at Serializer Boundary & Validator Pipeline

    [Fact]
    public void CreateProductRequest_WhenSpecificationsIsNull_DeserializesAndIsCaughtByCommandValidator()
    {
        // 1. JSON create request with specifications: null (serializer boundary)
        var categoryId = Guid.NewGuid();
        var json = $$"""
        {
            "categoryId": "{{categoryId}}",
            "name": "No Spec CPU",
            "sku": "CPU-NOSPEC",
            "brand": "Brand",
            "price": 200,
            "originalPrice": null,
            "stockQuantity": 10,
            "imageUrl": "img.jpg",
            "additionalImages": null,
            "componentType": 1,
            "specifications": null
        }
        """;

        // 2. Deserialize CreateProductRequest using MVC options
        var request = JsonSerializer.Deserialize<CreateProductRequest>(json, _jsonOptions);
        request.Should().NotBeNull();
        request!.Specifications.Should().BeNull();

        // 3. Map to CreateProductCommand (in-process mapping test, not full HTTP model-binding)
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

        // 4. Validate with CreateProductCommandValidator
        var validator = new CreateProductCommandValidator();
        var result = validator.TestValidate(command);

        // 5. Assert validation error for Specifications
        result.ShouldHaveValidationErrorFor(c => c.Specifications);
    }

    #endregion

    #region 7. Metadata Registry Consistency and Dynamic Subtype Round-Trip

    [Fact]
    public void MetadataRegistry_ReflectedFromDomainAttributes_IsConsistentAndRoundTripsAllDerivedTypes()
    {
        var attributes = typeof(ComponentSpecification)
            .GetCustomAttributes<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
            .ToList();

        attributes.Should().HaveCount(9, "Domain model currently declares exactly 9 component specification subtypes");

        var discriminators = new HashSet<string>(StringComparer.Ordinal);
        var derivedTypes = new HashSet<Type>();

        foreach (var attr in attributes)
        {
            attr.TypeDiscriminator.Should().BeOfType<string>("TypeDiscriminator must be a string");
            var discriminator = (string)attr.TypeDiscriminator!;
            discriminator.Should().NotBeNullOrWhiteSpace("TypeDiscriminator must not be null or whitespace");
            discriminators.Add(discriminator).Should().BeTrue($"Discriminator '{discriminator}' must be unique");

            var type = attr.DerivedType;
            type.Should().NotBeNull("DerivedType must not be null");
            typeof(ComponentSpecification).IsAssignableFrom(type).Should().BeTrue($"'{type.Name}' must inherit from ComponentSpecification");
            type.IsAbstract.Should().BeFalse($"'{type.Name}' must be a concrete type");
            derivedTypes.Add(type).Should().BeTrue($"Derived type '{type.Name}' must be unique across attributes");

            // Instantiate via parameterless constructor
            var instance = (ComponentSpecification)Activator.CreateInstance(type)!;
            instance.Should().NotBeNull();

            // Serialize under declared base type ComponentSpecification
            var serialized = JsonSerializer.Serialize<ComponentSpecification>(instance, _jsonOptions);
            serialized.Should().NotBeNullOrWhiteSpace();

            // Verify discriminator appears exactly once
            var expectedToken = $"\"$type\":\"{discriminator}\"";
            var firstIdx = serialized.IndexOf(expectedToken, StringComparison.Ordinal);
            firstIdx.Should().BeGreaterThanOrEqualTo(0, $"Discriminator token '{expectedToken}' must be present in serialized JSON");
            var lastIdx = serialized.LastIndexOf(expectedToken, StringComparison.Ordinal);
            firstIdx.Should().Be(lastIdx, $"Discriminator token '{expectedToken}' must appear exactly once in serialized JSON");

            // Deserialize back to ComponentSpecification and verify concrete type
            var roundTripped = JsonSerializer.Deserialize<ComponentSpecification>(serialized, _jsonOptions);
            roundTripped.Should().NotBeNull();
            roundTripped.Should().BeOfType(type, $"Deserialized instance must match concrete derived type '{type.Name}'");
        }
    }

    #endregion
}

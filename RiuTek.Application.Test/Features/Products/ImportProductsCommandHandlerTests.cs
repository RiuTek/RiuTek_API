using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class ImportProductsCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenValidationFails_DoesNotParseOrWriteAnything()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var workbookService = new Mock<IProductImportWorkbookService>();
        var sender = new Mock<ISender>();
        var validation = new ProductImportValidationResultDto(
            "1.0",
            IsValid: false,
            TotalRows: 1,
            ValidRows: 0,
            InvalidRows: 1,
            ErrorsTruncated: false,
            Errors:
            [
                new("Products", 2, "SKU", "BAD", "ProductImport.InvalidSku", "SKU không hợp lệ.")
            ]);

        sender.Setup(x => x.Send(
                It.IsAny<ValidateProductImportCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(validation));

        var handler = new ImportProductsCommandHandler(db, workbookService.Object, sender.Object);
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await handler.Handle(
            new ImportProductsCommand(stream, "products.xlsx", stream.Length),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsCommitted.Should().BeFalse();
        result.Value.CreatedCount.Should().Be(0);
        result.Value.Validation.Should().BeSameAs(validation);
        (await db.Products.CountAsync()).Should().Be(0);
        workbookService.Verify(
            x => x.ParseWorkbookAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenWorkbookIsValid_CreatesWholeBatchWithNormalizedValues()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var cpuCategory = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        var gpuCategory = new Category("Card đồ họa", "gpu", ComponentType.Gpu);
        db.Categories.AddRange(cpuCategory, gpuCategory);
        await db.SaveChangesAsync();

        var rows = new ProductImportRowDraft[]
        {
            new(
                2,
                " cpu-7600 ",
                " AMD Ryzen 5 7600 ",
                "cpu",
                ComponentType.Cpu,
                " AMD ",
                5_000_000,
                5_500_000,
                8,
                null,
                "https://media.riutek.test/cpu.webp",
                new CpuSpecification
                {
                    Socket = CpuSocket.AM5,
                    CoreCount = 6,
                    ThreadCount = 12,
                    BaseClockGhz = 3.8,
                    BoostClockGhz = 5.1,
                    TdpWattage = 65,
                    HasIntegratedGpu = true,
                    SupportedMemoryType = RamType.DDR5,
                    MaxMemorySpeedMhz = 5200
                },
                2,
                "CPU"),
            new(
                3,
                "gpu-4070",
                "NVIDIA RTX 4070",
                "gpu",
                ComponentType.Gpu,
                "NVIDIA",
                15_000_000,
                null,
                4,
                null,
                "https://media.riutek.test/gpu.webp",
                new GpuSpecification
                {
                    Chipset = "AD104",
                    VramGb = 12,
                    VramType = "GDDR6X",
                    LengthMm = 285,
                    SlotWidth = 2.5,
                    TdpWattage = 200,
                    RecommendedPsuWattage = 650,
                    Requires12VHPWR = true,
                    PowerConnectors = "1x 16-pin"
                },
                2,
                "GPU")
        };

        var validation = new ProductImportValidationResultDto(
            "1.0", true, 2, 2, 0, false, []);
        var parsed = new ProductImportParsedWorkbook("1.0", 2, rows, []);
        var sender = new Mock<ISender>();
        sender.Setup(x => x.Send(
                It.IsAny<ValidateProductImportCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(validation));
        var workbookService = new Mock<IProductImportWorkbookService>();
        workbookService.Setup(x => x.ParseWorkbookAsync(
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(parsed));

        var handler = new ImportProductsCommandHandler(db, workbookService.Object, sender.Object);
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await handler.Handle(
            new ImportProductsCommand(stream, "products.xlsx", stream.Length),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsCommitted.Should().BeTrue();
        result.Value.CreatedCount.Should().Be(2);
        result.Value.CreatedProducts.Should().HaveCount(2);

        var products = await db.Products.OrderBy(x => x.Sku).ToListAsync();
        products.Should().HaveCount(2);
        products[0].Sku.Should().Be("CPU-7600");
        products[0].Name.Should().Be("AMD Ryzen 5 7600");
        products[0].Slug.Should().Be("amd-ryzen-5-7600");
        products[0].Brand.Should().Be("AMD");
        products[0].IsActive.Should().BeTrue();
        products[0].AdditionalImages.Should().BeEmpty();
        products[1].Sku.Should().Be("GPU-4070");
    }
}

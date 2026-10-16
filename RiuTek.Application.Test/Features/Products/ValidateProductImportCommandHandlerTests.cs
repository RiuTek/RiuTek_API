using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Services;

namespace RiuTek.Application.Test.Features.Products;

public class ValidateProductImportCommandHandlerTests
{
    private readonly ClosedXmlProductImportWorkbookService _workbookService = new();

    private static (TestApplicationDbContext db, User adminUser) CreateTestDbWithAdmin()
    {
        var db = TestDbContextFactory.CreateInMemoryDbContext();
        var adminUser = new User("admin@riutek.com", "hash", "Admin User", UserRole.Admin)
        {
            IsActive = true
        };
        db.Users.Add(adminUser);
        db.SaveChanges();
        return (db, adminUser);
    }

    private static Mock<ICurrentUserService> CreateMockUserService(User user)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(true);
        mock.Setup(x => x.UserId).Returns(user.Id);
        mock.Setup(x => x.UserRole).Returns(user.Role.ToString());
        return mock;
    }

    [Fact]
    public async Task Handle_UnauthorizedUser_ShouldReturnUnauthorized()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        var mockUser = new Mock<ICurrentUserService>();
        mockUser.Setup(x => x.IsAuthenticated).Returns(false);

        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);
        using var ms = new MemoryStream(new byte[100]);

        var result = await handler.Handle(
            new ValidateProductImportCommand(ms, "import.xlsx", 100),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_CustomerRole_ShouldReturnForbidden()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var customer = new User("cust@riutek.com", "hash", "Customer", UserRole.Customer) { IsActive = true };
        db.Users.Add(customer);
        db.SaveChanges();

        var mockUser = CreateMockUserService(customer);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);
        using var ms = new MemoryStream(new byte[100]);

        var result = await handler.Handle(
            new ValidateProductImportCommand(ms, "import.xlsx", 100),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Forbidden");
    }

    [Fact]
    public async Task Handle_InvalidExtension_ShouldReturn400Validation()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);
        using var ms = new MemoryStream(new byte[100]);

        var result = await handler.Handle(
            new ValidateProductImportCommand(ms, "import.csv", 100),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductImport.InvalidFileExtension");
    }

    [Fact]
    public async Task Handle_FileTooLarge_ShouldReturn400Validation()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);
        using var ms = new MemoryStream(new byte[10]);

        var result = await handler.Handle(
            new ValidateProductImportCommand(ms, "import.xlsx", 6 * 1024 * 1024),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductImport.FileTooLarge");
    }

    [Fact]
    public async Task Handle_ValidWorkbook_ShouldPassAndNotWriteToDatabase()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);
        db.SaveChanges();

        var initialProductCount = await db.Products.CountAsync();

        // Build valid workbook
        var template = await _workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);
        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        pWs.Cell(2, 1).Value = "CPU-RYZEN-5600";
        pWs.Cell(2, 2).Value = "AMD Ryzen 5 5600";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "AMD";
        pWs.Cell(2, 6).Value = 3200000;
        pWs.Cell(2, 7).Value = 3500000;
        pWs.Cell(2, 8).Value = 15;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/cpu.jpg";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "CPU-RYZEN-5600";
        cpuWs.Cell(2, 2).Value = "AM4";
        cpuWs.Cell(2, 3).Value = 6;
        cpuWs.Cell(2, 4).Value = 12;
        cpuWs.Cell(2, 5).Value = 3.5;
        cpuWs.Cell(2, 6).Value = 4.4;
        cpuWs.Cell(2, 7).Value = 65;
        cpuWs.Cell(2, 8).Value = false;
        cpuWs.Cell(2, 9).Value = "DDR4";
        cpuWs.Cell(2, 10).Value = 3200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        saveMs.Position = 0;

        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);

        // Act
        var result = await handler.Handle(
            new ValidateProductImportCommand(saveMs, "test.xlsx", saveMs.Length),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.TotalRows.Should().Be(1);
        result.Value.ValidRows.Should().Be(1);
        result.Value.InvalidRows.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();

        // Verify NO records were written to Database (pure dry run!)
        var finalProductCount = await db.Products.CountAsync();
        finalProductCount.Should().Be(initialProductCount);
    }

    [Fact]
    public async Task Handle_DatabaseSkuAndSlugConflict_ShouldBeDetected()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);

        // Seed existing product in DB with SKU "CPU-EXISTING-01" and slug "intel-core-i9-14900k"
        var existingProduct = new Product(
            category.Id,
            "Intel Core i9 14900K",
            "intel-core-i9-14900k",
            "CPU-EXISTING-01",
            "Intel",
            14000000,
            10,
            "https://media.riutek.com/i9.jpg",
            ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 24, ThreadCount = 32, BaseClockGhz = 3.2, BoostClockGhz = 6.0, TdpWattage = 253, SupportedMemoryType = RamType.DDR5, MaxMemorySpeedMhz = 5600 });
        db.Products.Add(existingProduct);
        db.SaveChanges();

        // Workbook containing products that conflict with DB SKU and DB Slug
        var template = await _workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);
        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        // Row 2: Conflicts with existing SKU
        pWs.Cell(2, 1).Value = "cpu-existing-01"; // Case-insensitive collision!
        pWs.Cell(2, 2).Value = "Different Name For Sku Collision";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Intel";
        pWs.Cell(2, 6).Value = 1000000;
        pWs.Cell(2, 8).Value = 5;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.jpg";

        // Row 3: Conflicts with existing Slug
        pWs.Cell(3, 1).Value = "NEW-UNIQUE-SKU";
        pWs.Cell(3, 2).Value = "Intel Core i9 14900K"; // Generates duplicate slug "intel-core-i9-14900k"!
        pWs.Cell(3, 3).Value = "cpu";
        pWs.Cell(3, 4).Value = "Cpu";
        pWs.Cell(3, 5).Value = "Intel";
        pWs.Cell(3, 6).Value = 15000000;
        pWs.Cell(3, 8).Value = 5;
        pWs.Cell(3, 10).Value = "https://media.riutek.com/img.jpg";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "cpu-existing-01";
        cpuWs.Cell(2, 2).Value = "LGA1700"; cpuWs.Cell(2, 3).Value = 8; cpuWs.Cell(2, 4).Value = 16; cpuWs.Cell(2, 5).Value = 3.0; cpuWs.Cell(2, 6).Value = 4.5; cpuWs.Cell(2, 7).Value = 65; cpuWs.Cell(2, 8).Value = false; cpuWs.Cell(2, 9).Value = "DDR5"; cpuWs.Cell(2, 10).Value = 5600;

        cpuWs.Cell(3, 1).Value = "NEW-UNIQUE-SKU";
        cpuWs.Cell(3, 2).Value = "LGA1700"; cpuWs.Cell(3, 3).Value = 8; cpuWs.Cell(3, 4).Value = 16; cpuWs.Cell(3, 5).Value = 3.0; cpuWs.Cell(3, 6).Value = 4.5; cpuWs.Cell(3, 7).Value = 65; cpuWs.Cell(3, 8).Value = false; cpuWs.Cell(3, 9).Value = "DDR5"; cpuWs.Cell(3, 10).Value = 5600;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        saveMs.Position = 0;

        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);

        // Act
        var result = await handler.Handle(
            new ValidateProductImportCommand(saveMs, "test.xlsx", saveMs.Length),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.SkuConflict" && e.Row == 2);
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.SlugConflict" && e.Row == 3);
    }

    [Fact]
    public async Task Handle_CategoryNotFoundAndTypeMismatch_ShouldBeDetected()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var vgaCategory = new Category("Card màn hình", "vga", ComponentType.Gpu);
        db.Categories.Add(vgaCategory);
        db.SaveChanges();

        var template = await _workbookService.GenerateTemplateAsync([new("vga", "Card màn hình", ComponentType.Gpu)]);
        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        // Row 2: Category slug does not exist in DB
        pWs.Cell(2, 1).Value = "SKU-NON-EXIST-CAT";
        pWs.Cell(2, 2).Value = "Prod Non Exist Cat";
        pWs.Cell(2, 3).Value = "non-existent-category-slug";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Brand";
        pWs.Cell(2, 6).Value = 1000;
        pWs.Cell(2, 8).Value = 5;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.jpg";

        // Row 3: Category ComponentType mismatch (Product is Cpu, but category is Gpu)
        pWs.Cell(3, 1).Value = "SKU-CAT-MISMATCH";
        pWs.Cell(3, 2).Value = "Prod Cat Mismatch";
        pWs.Cell(3, 3).Value = "vga"; // Gpu category
        pWs.Cell(3, 4).Value = "Cpu"; // Cpu component
        pWs.Cell(3, 5).Value = "Brand";
        pWs.Cell(3, 6).Value = 1000;
        pWs.Cell(3, 8).Value = 5;
        pWs.Cell(3, 10).Value = "https://media.riutek.com/img.jpg";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-NON-EXIST-CAT";
        cpuWs.Cell(2, 2).Value = "AM4"; cpuWs.Cell(2, 3).Value = 6; cpuWs.Cell(2, 4).Value = 12; cpuWs.Cell(2, 5).Value = 3.5; cpuWs.Cell(2, 6).Value = 4.4; cpuWs.Cell(2, 7).Value = 65; cpuWs.Cell(2, 8).Value = false; cpuWs.Cell(2, 9).Value = "DDR4"; cpuWs.Cell(2, 10).Value = 3200;

        cpuWs.Cell(3, 1).Value = "SKU-CAT-MISMATCH";
        cpuWs.Cell(3, 2).Value = "AM4"; cpuWs.Cell(3, 3).Value = 6; cpuWs.Cell(3, 4).Value = 12; cpuWs.Cell(3, 5).Value = 3.5; cpuWs.Cell(3, 6).Value = 4.4; cpuWs.Cell(3, 7).Value = 65; cpuWs.Cell(3, 8).Value = false; cpuWs.Cell(3, 9).Value = "DDR4"; cpuWs.Cell(3, 10).Value = 3200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        saveMs.Position = 0;

        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);

        // Act
        var result = await handler.Handle(
            new ValidateProductImportCommand(saveMs, "test.xlsx", saveMs.Length),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.CategoryNotFound" && e.Row == 2);
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.CategoryTypeMismatch" && e.Row == 3);
    }

    [Fact]
    public async Task Handle_DomainRuleViolations_ShouldReuseValidators()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);
        db.SaveChanges();

        var template = await _workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);
        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-RULE-FAIL";
        pWs.Cell(2, 2).Value = "Product Violating Rules";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Brand";
        pWs.Cell(2, 6).Value = 1000;
        pWs.Cell(2, 7).Value = 500; // OriginalPrice < Price!
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/valid.jpg";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-RULE-FAIL";
        cpuWs.Cell(2, 2).Value = "AM4";
        cpuWs.Cell(2, 3).Value = 0; // CoreCount <= 0!
        cpuWs.Cell(2, 4).Value = 12;
        cpuWs.Cell(2, 5).Value = 4.0;
        cpuWs.Cell(2, 6).Value = 3.5; // BoostClock < BaseClock!
        cpuWs.Cell(2, 7).Value = 65;
        cpuWs.Cell(2, 8).Value = false;
        cpuWs.Cell(2, 9).Value = "DDR4";
        cpuWs.Cell(2, 10).Value = 3200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        saveMs.Position = 0;

        var mockUser = CreateMockUserService(user);
        var handler = new ValidateProductImportCommandHandler(db, mockUser.Object, _workbookService);

        // Act
        var result = await handler.Handle(
            new ValidateProductImportCommand(saveMs, "test.xlsx", saveMs.Length),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidPrice" && e.Sheet == "Products" && e.Column == "OriginalPrice");
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidSpecification" && e.Sheet == "CPU" && e.Column == "CoreCount");
        result.Value.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidSpecification" && e.Sheet == "CPU" && e.Column == "BoostClockGhz");
    }

    [Fact]
    public async Task Handle_GetProductImportTemplateQuery_ShouldGenerateSuccessfully()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);
        db.SaveChanges();

        var mockUser = CreateMockUserService(user);
        var handler = new GetProductImportTemplateQueryHandler(db, mockUser.Object, _workbookService);

        var result = await handler.Handle(new GetProductImportTemplateQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Content.Should().NotBeEmpty();
        result.Value.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.Value.FileName.Should().Be("riutek-product-import-v1.0.xlsx");
    }

    [Fact]
    public async Task Handle_DuplicateSku_ShouldMarkEveryAffectedProductRowInvalid()
    {
        var (db, user) = CreateTestDbWithAdmin();
        var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
        db.Categories.Add(category);
        db.SaveChanges();

        var specification = new CpuSpecification
        {
            Socket = CpuSocket.AM5,
            CoreCount = 6,
            ThreadCount = 12,
            BaseClockGhz = 3.8,
            BoostClockGhz = 5.1,
            TdpWattage = 65,
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5200
        };

        var parsed = new ProductImportParsedWorkbook(
            "1.0",
            2,
            [
                new(2, "DUPLICATE-SKU", "First Product", "cpu", ComponentType.Cpu, "AMD", 1000, null, 1, null, "https://media.riutek.test/first.webp", specification, 2, "CPU"),
                new(3, "duplicate-sku", "Second Product", "cpu", ComponentType.Cpu, "AMD", 1200, null, 1, null, "https://media.riutek.test/second.webp", specification, 3, "CPU")
            ],
            []);

        var workbookService = new Mock<IProductImportWorkbookService>();
        workbookService
            .Setup(x => x.ParseWorkbookAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(parsed));

        var handler = new ValidateProductImportCommandHandler(
            db,
            CreateMockUserService(user).Object,
            workbookService.Object);
        using var stream = new MemoryStream([1]);

        var result = await handler.Handle(
            new ValidateProductImportCommand(stream, "import.xlsx", stream.Length),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.ValidRows.Should().Be(0);
        result.Value.InvalidRows.Should().Be(2);
        result.Value.Errors.Where(e => e.Code == "ProductImport.DuplicateSku")
            .Select(e => e.Row)
            .Should().BeEquivalentTo([2, 3]);
    }
}

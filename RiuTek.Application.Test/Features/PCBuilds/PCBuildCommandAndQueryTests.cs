using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Commands;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Application.Features.PCBuilds.Queries;
using RiuTek.Application.Services;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.PCBuilds;

public class PCBuildCommandAndQueryTests
{
    private readonly HardwareCompatibilityService _compatibilityService = new();

    private static (User User, Product Cpu, Product Mobo, Product Ram) SeedStandardScenario(
        TestApplicationDbContext context,
        UserRole role = UserRole.Customer,
        bool isActive = true)
    {
        var user = new User($"user_{Guid.NewGuid():N}@test.com", "hash", "Test Customer", role)
        {
            IsActive = isActive
        };
        context.Users.Add(user);

        var catCpu = new Category("CPU", "cpu", ComponentType.Cpu, "desc");
        var catMobo = new Category("Mobo", "mobo", ComponentType.Motherboard, "desc");
        var catRam = new Category("RAM", "ram", ComponentType.Ram, "desc");
        context.Categories.AddRange(catCpu, catMobo, catRam);

        var cpuSpec = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 14,
            ThreadCount = 20,
            TdpWattage = 125,
            SupportedMemoryType = RamType.DDR5
        };

        var moboSpec = new MotherboardSpecification
        {
            Socket = CpuSocket.LGA1700,
            Chipset = "B760",
            FormFactor = MotherboardFormFactor.ATX,
            MemoryType = RamType.DDR5,
            MemorySlots = 4,
            MaxMemoryCapacityGb = 128
        };

        var ramSpec = new RamSpecification
        {
            MemoryType = RamType.DDR5,
            ModuleCount = 1,
            CapacityGb = 16
        };

        var cpu = new Product(catCpu.Id, "Core i5-13600K", "cpu-slug", "SKU_CPU", "Intel", 7_000_000m, 10, "cpu.png", ComponentType.Cpu, cpuSpec);
        var mobo = new Product(catMobo.Id, "B760M Gaming", "mobo-slug", "SKU_MOBO", "MSI", 3_500_000m, 5, "mobo.png", ComponentType.Motherboard, moboSpec);
        var ram = new Product(catRam.Id, "Kingston 16GB", "ram-slug", "SKU_RAM", "Kingston", 1_200_000m, 20, "ram.png", ComponentType.Ram, ramSpec);

        context.Products.AddRange(cpu, mobo, ram);
        return (user, cpu, mobo, ram);
    }

    private static Mock<ICurrentUserService> MockUser(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(m => m.UserId).Returns(userId);
        mock.Setup(m => m.IsAuthenticated).Returns(isAuthenticated);
        return mock;
    }

    [Fact]
    public async Task PCBuildAuthHelper_AuthMatrixTests()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, _, _) = SeedStandardScenario(context, UserRole.Customer, true);
        var (staff, _, _, _) = SeedStandardScenario(context, UserRole.Staff, true);
        var (inactiveCust, _, _, _) = SeedStandardScenario(context, UserRole.Customer, false);
        await context.SaveChangesAsync();

        // 1. Anonymous
        var anonMock = MockUser(null, false);
        var r1 = await PCBuildAuthHelper.ValidateActiveCustomerAsync(anonMock.Object, context, CancellationToken.None);
        r1.IsFailure.Should().BeTrue();
        r1.Error.Code.Should().Be("Auth.Unauthorized");

        // 2. Missing user in DB
        var missingMock = MockUser(Guid.NewGuid(), true);
        var r2 = await PCBuildAuthHelper.ValidateActiveCustomerAsync(missingMock.Object, context, CancellationToken.None);
        r2.IsFailure.Should().BeTrue();
        r2.Error.Code.Should().Be("User.NotFound");

        // 3. Inactive customer
        var inactiveMock = MockUser(inactiveCust.Id, true);
        var r3 = await PCBuildAuthHelper.ValidateActiveCustomerAsync(inactiveMock.Object, context, CancellationToken.None);
        r3.IsFailure.Should().BeTrue();
        r3.Error.Code.Should().Be("User.AccountInactive");

        // 4. Staff
        var staffMock = MockUser(staff.Id, true);
        var r4 = await PCBuildAuthHelper.ValidateActiveCustomerAsync(staffMock.Object, context, CancellationToken.None);
        r4.IsFailure.Should().BeTrue();
        r4.Error.Code.Should().Be("PCBuild.Forbidden");

        // 5. Active customer -> Success
        var activeMock = MockUser(customer.Id, true);
        var r5 = await PCBuildAuthHelper.ValidateActiveCustomerAsync(activeMock.Object, context, CancellationToken.None);
        r5.IsSuccess.Should().BeTrue();
        r5.Value.Id.Should().Be(customer.Id);
    }

    [Fact]
    public async Task PreviewPCBuildCommand_ShouldNotCallSaveChanges_AndSeparateAvailability()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, cpu, mobo, ram) = SeedStandardScenario(context);
        ram.StockQuantity = 0; // Out of stock
        await context.SaveChangesAsync();

        var saveCalled = false;
        context.SaveChangesInterceptor = () =>
        {
            saveCalled = true;
            return Task.FromResult(0);
        };

        var handler = new PreviewPCBuildCommandHandler(context, _compatibilityService);
        var command = new PreviewPCBuildCommand(
        [
            new(cpu.Id, 1),
            new(mobo.Id, 1),
            new(ram.Id, 2)
        ]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saveCalled.Should().BeFalse("Preview must not call SaveChanges");

        var preview = result.Value;
        preview.TotalPrice.Should().Be(cpu.Price + mobo.Price + ram.Price * 2);
        preview.CanPurchaseAll.Should().BeFalse(); // Because RAM is out of stock

        var ramPreview = preview.Items.First(i => i.ProductId == ram.Id);
        ramPreview.CanPurchase.Should().BeFalse();

        var cpuPreview = preview.Items.First(i => i.ProductId == cpu.Id);
        cpuPreview.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePCBuildCommand_ShouldSaveBuildWithCustomerOwnership_AndSnapshotPrices()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, cpu, mobo, ram) = SeedStandardScenario(context);
        await context.SaveChangesAsync();

        var userMock = MockUser(customer.Id);
        var handler = new CreatePCBuildCommandHandler(context, userMock.Object, _compatibilityService);

        var command = new CreatePCBuildCommand(
            Name: "Workstation 2026",
            Description: "For 3D rendering",
            Items:
            [
                new(cpu.Id, 1),
                new(mobo.Id, 1),
                new(ram.Id, 2)
            ]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Name.Should().Be("Workstation 2026");
        dto.Status.Should().Be(PCBuildStatus.Saved);
        dto.TotalPrice.Should().Be(cpu.Price + mobo.Price + ram.Price * 2);

        // Verify database persistence
        var dbBuild = await context.PCBuilds.Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == dto.Id);
        dbBuild.Should().NotBeNull();
        dbBuild!.UserId.Should().Be(customer.Id);
        dbBuild.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMyPCBuildByIdQuery_WhenOwnershipMismatch_ShouldReturnNotFound()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, _, _) = SeedStandardScenario(context);
        var (otherCustomer, _, _, _) = SeedStandardScenario(context);

        var build = new PCBuild("Owner Build", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        // Other customer attempts to get owner's build
        var otherUserMock = MockUser(otherCustomer.Id);
        var handler = new GetMyPCBuildByIdQueryHandler(context, otherUserMock.Object, _compatibilityService);

        var result = await handler.Handle(new GetMyPCBuildByIdQuery(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.NotFound");
    }

    [Fact]
    public async Task UpdateMyPCBuildCommand_WhenValid_ShouldReplaceItemsAndRecomputeSnapshot()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, mobo, ram) = SeedStandardScenario(context);

        var build = new PCBuild("Original Build", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        var userMock = MockUser(owner.Id);
        var handler = new UpdateMyPCBuildCommandHandler(context, userMock.Object, _compatibilityService);

        var updateCommand = new UpdateMyPCBuildCommand(
            Id: build.Id,
            Name: "Updated Rig",
            Description: "Updated desc",
            Items:
            [
                new(mobo.Id, 1),
                new(ram.Id, 4)
            ]);

        var result = await handler.Handle(updateCommand, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error?.Description ?? result.Error?.Code);
        result.Value.Name.Should().Be("Updated Rig");
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalPrice.Should().Be(mobo.Price + ram.Price * 4);
    }

    [Fact]
    public async Task UpdateMyPCBuildCommand_WhenConcurrencyConflict_ShouldReturn409AndClearTracking()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, _, _) = SeedStandardScenario(context);

        var build = new PCBuild("Concurrency Test", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        context.SaveChangesInterceptor = () =>
            throw new DbUpdateConcurrencyException("Concurrency conflict simulated");

        var userMock = MockUser(owner.Id);
        var handler = new UpdateMyPCBuildCommandHandler(context, userMock.Object, _compatibilityService);

        var result = await handler.Handle(
            new UpdateMyPCBuildCommand(build.Id, "New Name", null, [new(cpu.Id, 1)]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.ConcurrencyConflict");
        context.ClearTrackedChangesCalled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteMyPCBuildCommand_WhenStatusIsOrdered_ShouldReturn409()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, _, _) = SeedStandardScenario(context);

        var build = new PCBuild("Ordered Build", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        build.GetType().GetProperty(nameof(PCBuild.Status))!.SetValue(build, PCBuildStatus.Ordered);
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        var userMock = MockUser(owner.Id);
        var handler = new DeleteMyPCBuildCommandHandler(context, userMock.Object);

        var result = await handler.Handle(new DeleteMyPCBuildCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.CannotDeleteOrderedBuild");

        // Verify build still exists in database
        (await context.PCBuilds.AnyAsync(b => b.Id == build.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteMyPCBuildCommand_WhenValid_ShouldHardDeleteBuild()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, _, _) = SeedStandardScenario(context);

        var build = new PCBuild("Draft Build", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        var userMock = MockUser(owner.Id);
        var handler = new DeleteMyPCBuildCommandHandler(context, userMock.Object);

        var result = await handler.Handle(new DeleteMyPCBuildCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await context.PCBuilds.AnyAsync(b => b.Id == build.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task GetMyPCBuildsQuery_WhenBeyondEnd_ShouldReturnEmptyListSafely()
    {
        using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (owner, cpu, _, _) = SeedStandardScenario(context);

        var build = new PCBuild("My Build", owner.Id);
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        var userMock = MockUser(owner.Id);
        var handler = new GetMyPCBuildsQueryHandler(context, userMock.Object);

        var result = await handler.Handle(new GetMyPCBuildsQuery(PageIndex: 999, PageSize: 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().BeEmpty();
    }
}

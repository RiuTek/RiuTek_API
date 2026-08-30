using FluentAssertions;
using RiuTek.Application.Features.Categories.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Categories;

public class CategoryQueryTests
{
    [Fact]
    public async Task GetCategoryById_WhenFound_ReturnsDtoWithSubCategories()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var parent = new Category("CPU", "cpu", ComponentType.Cpu, "Processors");
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var child1 = new Category("Intel", "intel", ComponentType.Cpu, null, parent.Id);
        var child2 = new Category("AMD", "amd", ComponentType.Cpu, null, parent.Id);
        context.Categories.AddRange(child1, child2);
        await context.SaveChangesAsync();

        var handler = new GetCategoryByIdQueryHandler(context);
        var result = await handler.Handle(new GetCategoryByIdQuery(parent.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(parent.Id);
        result.Value.Name.Should().Be("CPU");
        result.Value.SubCategories.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCategoryById_WhenNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetCategoryByIdQueryHandler(context);
        var result = await handler.Handle(new GetCategoryByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetCategoryTree_WhenDatabaseIsEmpty_ReturnsEmptyList()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var handler = new GetCategoryTreeQueryHandler(context);
        var result = await handler.Handle(new GetCategoryTreeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCategoryTree_BuildsMultiLevelTreeAndSortsDeterministically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();

        // 2 Roots: RAM and CPU (insert in reverse to test alphabetical sort)
        var ramRoot = new Category("RAM", "ram", ComponentType.Ram);
        var cpuRoot = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.AddRange(ramRoot, cpuRoot);
        await context.SaveChangesAsync();

        // CPU subcategories: Intel and AMD (insert in reverse to test sort)
        var intel = new Category("Intel", "intel", ComponentType.Cpu, null, cpuRoot.Id);
        var amd = new Category("AMD", "amd", ComponentType.Cpu, null, cpuRoot.Id);
        context.Categories.AddRange(intel, amd);
        await context.SaveChangesAsync();

        // Intel subcategories: Core i9 and Core i7
        var i9 = new Category("Core i9", "core-i9", ComponentType.Cpu, null, intel.Id);
        var i7 = new Category("Core i7", "core-i7", ComponentType.Cpu, null, intel.Id);
        context.Categories.AddRange(i9, i7);
        await context.SaveChangesAsync();

        var handler = new GetCategoryTreeQueryHandler(context);
        var result = await handler.Handle(new GetCategoryTreeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // Top level sorted: CPU before RAM
        result.Value[0].Name.Should().Be("CPU");
        result.Value[1].Name.Should().Be("RAM");

        // CPU children sorted: AMD before Intel
        var cpuNode = result.Value[0];
        cpuNode.SubCategories.Should().HaveCount(2);
        cpuNode.SubCategories[0].Name.Should().Be("AMD");
        cpuNode.SubCategories[1].Name.Should().Be("Intel");

        // Intel children sorted: Core i7 before Core i9
        var intelNode = cpuNode.SubCategories[1];
        intelNode.SubCategories.Should().HaveCount(2);
        intelNode.SubCategories[0].Name.Should().Be("Core i7");
        intelNode.SubCategories[1].Name.Should().Be("Core i9");
    }

    [Fact]
    public async Task GetCategoryTree_WhenOrphanParentExists_ReturnsInvalidHierarchy()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var orphan = new Category("Orphan Child", "orphan-child", ComponentType.Cpu, null, Guid.NewGuid());
        context.Categories.Add(orphan);
        await context.SaveChangesAsync();

        var handler = new GetCategoryTreeQueryHandler(context);
        var result = await handler.Handle(new GetCategoryTreeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.InvalidHierarchy");
    }

    [Fact]
    public async Task GetCategoryTree_WhenCorruptedLoopWithoutRoot_ReturnsInvalidHierarchy()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();

        // Loop: A -> B -> A (neither is root)
        var catA = new Category("Loop A", "loop-a", ComponentType.Cpu);
        var catB = new Category("Loop B", "loop-b", ComponentType.Cpu, null, catA.Id);
        catA.ParentId = catB.Id;

        context.Categories.AddRange(catA, catB);
        await context.SaveChangesAsync();

        var handler = new GetCategoryTreeQueryHandler(context);
        var result = await handler.Handle(new GetCategoryTreeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.InvalidHierarchy");
    }
}

using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Categories.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Categories;

public class CategoryCommandTests
{
    #region Validator Tests

    [Fact]
    public void CreateCategoryCommandValidator_ValidatesRulesProperly()
    {
        var validator = new CreateCategoryCommandValidator();

        // Empty Name
        validator.TestValidate(new CreateCategoryCommand("", ComponentType.Cpu))
            .ShouldHaveValidationErrorFor(x => x.Name);

        // Long Name (>150)
        validator.TestValidate(new CreateCategoryCommand(new string('a', 151), ComponentType.Cpu))
            .ShouldHaveValidationErrorFor(x => x.Name);

        // Long Description (>500)
        validator.TestValidate(new CreateCategoryCommand("Name", ComponentType.Cpu, new string('d', 501)))
            .ShouldHaveValidationErrorFor(x => x.Description);

        // Invalid enum
        validator.TestValidate(new CreateCategoryCommand("Name", (ComponentType)999))
            .ShouldHaveValidationErrorFor(x => x.ComponentType);

        // Guid.Empty ParentId
        validator.TestValidate(new CreateCategoryCommand("Name", ComponentType.Cpu, null, Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.ParentId);

        // Valid command
        validator.TestValidate(new CreateCategoryCommand("Valid Name", ComponentType.Cpu, "Valid Desc", Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateCategoryCommandValidator_ValidatesRulesProperly()
    {
        var validator = new UpdateCategoryCommandValidator();

        // Empty Id
        validator.TestValidate(new UpdateCategoryCommand(Guid.Empty, "Name", ComponentType.Cpu, null, null))
            .ShouldHaveValidationErrorFor(x => x.Id);

        // Valid command
        validator.TestValidate(new UpdateCategoryCommand(Guid.NewGuid(), "Name", ComponentType.Cpu, "Desc", null))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DeleteCategoryCommandValidator_ValidatesRulesProperly()
    {
        var validator = new DeleteCategoryCommandValidator();

        // Empty Id
        validator.TestValidate(new DeleteCategoryCommand(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.Id);

        // Valid command
        validator.TestValidate(new DeleteCategoryCommand(Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region Authorization Guards

    [Fact]
    public async Task CreateCategory_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("CPU", ComponentType.Cpu), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task CreateCategory_WhenCustomerRole_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("CPU", ComponentType.Cpu), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task CreateCategory_WhenAdminOrStaff_Succeeds(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("Intel CPUs", ComponentType.Cpu, "Intel processors"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Intel CPUs");
        result.Value.Slug.Should().Be("intel-cpus");
        result.Value.Description.Should().Be("Intel processors");
        result.Value.ComponentType.Should().Be(ComponentType.Cpu);
    }

    [Fact]
    public async Task UpdateCategory_WhenUnauthenticated_ReturnsUnauthorizedAndDoesNotModifyCategory()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu, "Original");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "CPU Modified", ComponentType.Cpu, "Changed", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);

        var unchanged = await context.Categories.FindAsync(category.Id);
        unchanged!.Name.Should().Be("CPU");
        unchanged.Description.Should().Be("Original");
    }

    [Fact]
    public async Task UpdateCategory_WhenCustomerRole_ReturnsForbiddenAndDoesNotModifyCategory()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu, "Original");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "CPU Modified", ComponentType.Cpu, "Changed", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);

        var unchanged = await context.Categories.FindAsync(category.Id);
        unchanged!.Name.Should().Be("CPU");
        unchanged.Description.Should().Be("Original");
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task UpdateCategory_WhenAdminOrStaff_Succeeds(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu, "Original");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "CPU Updated", ComponentType.Cpu, "Updated Desc", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("CPU Updated");
        result.Value.Description.Should().Be("Updated Desc");

        var updated = await context.Categories.FindAsync(category.Id);
        updated!.Name.Should().Be("CPU Updated");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteCategory_WhenUnauthenticated_ReturnsUnauthorizedAndDoesNotDeleteCategory()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);

        var stillExists = await context.Categories.FindAsync(category.Id);
        stillExists.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteCategory_WhenCustomerRole_ReturnsForbiddenAndDoesNotDeleteCategory()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);

        var stillExists = await context.Categories.FindAsync(category.Id);
        stillExists.Should().NotBeNull();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public async Task DeleteCategory_WhenAdminOrStaff_Succeeds(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(role.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var deleted = await context.Categories.FindAsync(category.Id);
        deleted.Should().BeNull();
    }

    #endregion

    #region Create Category Business Rules

    [Fact]
    public async Task CreateCategory_WithChildMatchingParentComponentType_Succeeds()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var parent = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("Intel Core", ComponentType.Cpu, "   ", parent.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentId.Should().Be(parent.Id);
        result.Value.Description.Should().BeNull();
    }

    [Fact]
    public async Task CreateCategory_WhenParentNotFound_ReturnsParentNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("Intel", ComponentType.Cpu, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.ParentNotFound");
    }

    [Fact]
    public async Task CreateCategory_WhenParentComponentTypeDiffers_ReturnsComponentTypeMismatch()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var parent = new Category("Motherboard", "motherboard", ComponentType.Motherboard);
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("Intel CPU", ComponentType.Cpu, null, parent.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.ComponentTypeMismatch");
    }

    [Fact]
    public async Task CreateCategory_WhenSlugAlreadyExists_ReturnsSlugConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var existing = new Category("Intel Core", "intel-core", ComponentType.Cpu);
        context.Categories.Add(existing);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("Intel  Core", ComponentType.Cpu), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.SlugConflict");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task CreateCategory_WhenNameCannotProduceSlug_ReturnsInvalidSlug()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new CreateCategoryCommand("--- $$$ ---", ComponentType.Cpu), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.InvalidSlug");
    }

    #endregion

    #region Update Category Business Rules

    [Fact]
    public async Task UpdateCategory_WhenNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(Guid.NewGuid(), "Name", ComponentType.Cpu, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateCategory_WhenSettingSelfAsParent_ReturnsSelfParentError()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "CPU Updated", ComponentType.Cpu, null, category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.SelfParent");
    }

    [Fact]
    public async Task UpdateCategory_WhenSettingChildOrGrandchildAsParent_ReturnsCycleDetected()
    {
        // Hierarchy: Root -> Child -> GrandChild
        // Attempt: Set Root's ParentId = GrandChild.Id
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var root = new Category("Root", "root", ComponentType.Cpu);
        context.Categories.Add(root);
        await context.SaveChangesAsync();

        var child = new Category("Child", "child", ComponentType.Cpu, null, root.Id);
        context.Categories.Add(child);
        await context.SaveChangesAsync();

        var grandChild = new Category("GrandChild", "grandchild", ComponentType.Cpu, null, child.Id);
        context.Categories.Add(grandChild);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(root.Id, "Root Updated", ComponentType.Cpu, null, grandChild.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.CycleDetected");
    }

    [Fact]
    public async Task UpdateCategory_WhenSlugConflictsWithAnotherCategory_ReturnsSlugConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var cat1 = new Category("Intel CPUs", "intel-cpus", ComponentType.Cpu);
        var cat2 = new Category("AMD CPUs", "amd-cpus", ComponentType.Cpu);
        context.Categories.AddRange(cat1, cat2);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(cat2.Id, "Intel CPUs", ComponentType.Cpu, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.SlugConflict");
    }

    [Fact]
    public async Task UpdateCategory_WhenChangingComponentTypeWithExistingProducts_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        );
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "CPU", ComponentType.Gpu, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.HasProducts");
    }

    [Fact]
    public async Task UpdateCategory_WhenChangingComponentTypeWithExistingSubCategories_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var parent = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var child = new Category("Intel", "intel", ComponentType.Cpu, null, parent.Id);
        context.Categories.Add(child);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(parent.Id, "CPU", ComponentType.Gpu, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.HasSubCategories");
    }

    [Fact]
    public async Task UpdateCategory_WithoutDependencies_CanChangeComponentTypeAndGeneratesNewSlug()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("Old Name", "old-name", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new UpdateCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new UpdateCategoryCommand(category.Id, "New GPU Name", ComponentType.Gpu, "New description", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New GPU Name");
        result.Value.Slug.Should().Be("new-gpu-name");
        result.Value.ComponentType.Should().Be(ComponentType.Gpu);
        result.Value.Description.Should().Be("New description");

        var updated = await context.Categories.FindAsync(category.Id);
        updated!.UpdatedAt.Should().NotBeNull();
    }

    #endregion

    #region Delete Category Business Rules

    [Fact]
    public async Task DeleteCategory_WhenNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteCategory_WhenHasSubCategories_ReturnsConflictAndDoesNotDelete()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var parent = new Category("Parent", "parent", ComponentType.Cpu);
        context.Categories.Add(parent);
        await context.SaveChangesAsync();

        var child = new Category("Child", "child", ComponentType.Cpu, null, parent.Id);
        context.Categories.Add(child);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(parent.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.HasSubCategories");

        var parentInDb = await context.Categories.FindAsync(parent.Id);
        parentInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteCategory_WhenHasProducts_ReturnsConflictAndDoesNotDelete()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("Category", "category", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel i9",
            "intel-i9",
            "SKU1",
            "Intel",
            500,
            10,
            "img.png",
            ComponentType.Cpu,
            new CpuSpecification()
        )
        {
            IsActive = false // Even inactive products must block hard delete
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Category.HasProducts");

        var catInDb = await context.Categories.FindAsync(category.Id);
        catInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteCategory_WhenEmptyWithoutDependencies_DeletesSuccessfully()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var category = new Category("Standalone", "standalone", ComponentType.Cpu);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteCategoryCommandHandler(context, currentUserMock.Object);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var catInDb = await context.Categories.FindAsync(category.Id);
        catInDb.Should().BeNull();
    }

    #endregion
}

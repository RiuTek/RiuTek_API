using FluentValidation.TestHelper;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class GetProductFilterOptionsQueryValidatorTests
{
    private readonly GetProductFilterOptionsQueryValidator _validator = new();

    [Fact]
    public void Validator_WhenValidQuery_Passes()
    {
        var query = new GetProductFilterOptionsQuery(
            ComponentType: ComponentType.Cpu,
            CategoryId: Guid.NewGuid(),
            IsActive: true,
            InStock: true);

        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_WhenAllParametersNull_Passes()
    {
        var query = new GetProductFilterOptionsQuery();
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_WhenComponentTypeIsInvalidEnum_Fails()
    {
        var query = new GetProductFilterOptionsQuery(ComponentType: (ComponentType)9999);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.ComponentType)
            .WithErrorMessage("Loại linh kiện không hợp lệ.");
    }

    [Fact]
    public void Validator_WhenCategoryIdIsEmptyGuid_Fails()
    {
        var query = new GetProductFilterOptionsQuery(CategoryId: Guid.Empty);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.CategoryId)
            .WithErrorMessage("Id danh mục không hợp lệ.");
    }
}

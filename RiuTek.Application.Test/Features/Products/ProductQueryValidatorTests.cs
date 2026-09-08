using FluentValidation.TestHelper;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Products;

public class ProductQueryValidatorTests
{
    private readonly GetProductsQueryValidator _getProductsValidator = new();
    private readonly GetProductBySlugQueryValidator _getBySlugValidator = new();
    private readonly GetProductByIdQueryValidator _getByIdValidator = new();

    #region GetProductsQuery & ProductFilterOptions Validator Tests

    [Fact]
    public void GetProductsQuery_WhenOptionsIsNull_FailsValidationWithoutException()
    {
        var query = new GetProductsQuery(null!);
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Options);
    }

    [Fact]
    public void GetProductsQuery_DefaultOptions_PassesValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions());
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void ProductFilterOptions_WhenPageIndexIsZeroOrNegative_FailsValidation(int pageIndex)
    {
        var query = new GetProductsQuery(new ProductFilterOptions(PageIndex: pageIndex));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.PageIndex");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(51)]
    [InlineData(100)]
    public void ProductFilterOptions_WhenPageSizeIsOutOfRange_FailsValidation(int pageSize)
    {
        var query = new GetProductsQuery(new ProductFilterOptions(PageSize: pageSize));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.PageSize");
    }

    [Fact]
    public void ProductFilterOptions_WhenSearchTermExceedsMaxLength_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(SearchTerm: new string('s', 101)));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.SearchTerm");
    }

    [Fact]
    public void ProductFilterOptions_WhenCategoryIdIsEmptyGuid_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(CategoryId: Guid.Empty));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.CategoryId");
    }

    [Fact]
    public void ProductFilterOptions_WhenComponentTypeIsInvalidEnum_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(ComponentType: (ComponentType)999));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.ComponentType");
    }

    [Fact]
    public void ProductFilterOptions_WhenBrandExceedsMaxLength_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(Brand: new string('b', 101)));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.Brand");
    }

    [Fact]
    public void ProductFilterOptions_WhenMinPriceIsNegative_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(MinPrice: -10));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.MinPrice");
    }

    [Fact]
    public void ProductFilterOptions_WhenMaxPriceIsNegative_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(MaxPrice: -5));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.MaxPrice");
    }

    [Fact]
    public void ProductFilterOptions_WhenMaxPriceIsLessThanMinPrice_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(MinPrice: 200, MaxPrice: 100));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.MaxPrice");
    }

    [Fact]
    public void ProductFilterOptions_WhenSortByIsInvalidEnum_FailsValidation()
    {
        var query = new GetProductsQuery(new ProductFilterOptions(SortBy: (ProductSortOption)999));
        var result = _getProductsValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor("Options.SortBy");
    }

    #endregion

    #region GetProductBySlugQuery Validator Tests

    [Fact]
    public void GetProductBySlugQuery_WhenValidSlug_PassesValidation()
    {
        var query = new GetProductBySlugQuery("intel-core-i7-14700k");
        var result = _getBySlugValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetProductBySlugQuery_WhenSlugIsEmptyOrWhitespace_FailsValidation(string invalidSlug)
    {
        var query = new GetProductBySlugQuery(invalidSlug);
        var result = _getBySlugValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Fact]
    public void GetProductBySlugQuery_WhenSlugExceedsMaxLength_FailsValidation()
    {
        var query = new GetProductBySlugQuery(new string('s', 256));
        var result = _getBySlugValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    #endregion

    #region GetProductByIdQuery Validator Tests

    [Fact]
    public void GetProductByIdQuery_WhenValidId_PassesValidation()
    {
        var query = new GetProductByIdQuery(Guid.NewGuid());
        var result = _getByIdValidator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GetProductByIdQuery_WhenIdIsEmptyGuid_FailsValidation()
    {
        var query = new GetProductByIdQuery(Guid.Empty);
        var result = _getByIdValidator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    #endregion
}

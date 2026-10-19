using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;

namespace RiuTek.Application.Common.Interfaces;

public record ComponentSelection(Product Product, int Quantity);
public record SpecificationSelection(ComponentSpecification Specification, int Quantity);

public interface IHardwareCompatibilityService
{
    /// <summary>
    /// Checks compatibility between a list of products with quantities.
    /// </summary>
    CompatibilityCheckResultDto ValidateComponentSelections(IReadOnlyList<ComponentSelection> selections);

    /// <summary>
    /// Checks compatibility between a list of specifications with quantities directly.
    /// </summary>
    CompatibilityCheckResultDto ValidateSpecificationSelections(IReadOnlyList<SpecificationSelection> selections);

    /// <summary>
    /// Checks compatibility between a list of products (with their specifications).
    /// </summary>
    CompatibilityCheckResultDto ValidateComponents(IReadOnlyList<Product> components);

    /// <summary>
    /// Checks compatibility between a list of specifications directly.
    /// </summary>
    CompatibilityCheckResultDto ValidateSpecifications(IReadOnlyList<ComponentSpecification> specifications);
}

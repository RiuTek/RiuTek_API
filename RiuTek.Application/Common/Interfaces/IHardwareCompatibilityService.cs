using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;

namespace RiuTek.Application.Common.Interfaces;

public interface IHardwareCompatibilityService
{
    /// <summary>
    /// Checks compatibility between a list of products (with their specifications).
    /// </summary>
    CompatibilityCheckResultDto ValidateComponents(IReadOnlyList<Product> components);

    /// <summary>
    /// Checks compatibility between a list of specifications directly.
    /// </summary>
    CompatibilityCheckResultDto ValidateSpecifications(IReadOnlyList<ComponentSpecification> specifications);
}

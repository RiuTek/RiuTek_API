using RiuTek.Core.Enums;

namespace RiuTek.API.Contracts;

public record CreateCategoryRequest(
    string Name,
    ComponentType ComponentType,
    string? Description = null,
    Guid? ParentId = null
);

public record UpdateCategoryRequest(
    string Name,
    ComponentType ComponentType,
    string? Description = null,
    Guid? ParentId = null
);

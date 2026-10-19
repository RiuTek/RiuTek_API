namespace RiuTek.API.Contracts;

public record PCBuildItemRequest(Guid ProductId, int Quantity = 1);

public record PreviewPCBuildRequest(List<PCBuildItemRequest> Items);

public record CreatePCBuildRequest(
    string Name,
    string? Description,
    List<PCBuildItemRequest> Items
);

public record UpdatePCBuildRequest(
    string Name,
    string? Description,
    List<PCBuildItemRequest> Items
);

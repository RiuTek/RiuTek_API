namespace RiuTek.API.Contracts;

public record AddCartItemRequest(Guid ProductId, int Quantity);

public record SetCartItemQuantityRequest(int Quantity);

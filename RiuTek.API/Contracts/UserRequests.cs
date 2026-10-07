namespace RiuTek.API.Contracts;

public record UpdateProfileRequest(string FullName, string? PhoneNumber);

public record AddUserAddressRequest(
    string ReceiverName,
    string PhoneNumber,
    string AddressLine,
    string Ward,
    string District,
    string City,
    bool IsDefault = false);

public record UpdateUserAddressRequest(
    string ReceiverName,
    string PhoneNumber,
    string AddressLine,
    string Ward,
    string District,
    string City);

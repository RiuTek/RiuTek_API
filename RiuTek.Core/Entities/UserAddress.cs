using RiuTek.Core.Common;

namespace RiuTek.Core.Entities;

public class UserAddress : BaseEntity
{
    public Guid UserId { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string Ward { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    // Navigation property
    public User User { get; set; } = null!;

    protected UserAddress() { }

    public UserAddress(
        Guid userId,
        string receiverName,
        string phoneNumber,
        string addressLine,
        string ward,
        string district,
        string city,
        bool isDefault = false)
    {
        UserId = userId;
        ReceiverName = receiverName;
        PhoneNumber = phoneNumber;
        AddressLine = addressLine;
        Ward = ward;
        District = district;
        City = city;
        IsDefault = isDefault;
    }
}

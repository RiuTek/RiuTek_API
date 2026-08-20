using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class User : BaseEntity, IAggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsActive { get; set; } = true;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }

    // Navigation properties
    public ICollection<PCBuild> PCBuilds { get; set; } = new List<PCBuild>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<UserAddress> Addresses { get; set; } = new List<UserAddress>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();

    protected User() { }

    public User(string email, string passwordHash, string fullName, UserRole role = UserRole.Customer, string? phoneNumber = null)
    {
        Email = email;
        PasswordHash = passwordHash;
        FullName = fullName;
        Role = role;
        PhoneNumber = phoneNumber;
        IsActive = true;
    }
}

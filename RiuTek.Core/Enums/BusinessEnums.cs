namespace RiuTek.Core.Enums;

public enum PCBuildStatus
{
    Draft = 1,
    Saved = 2,
    Ordered = 3
}

public enum OrderStatus
{
    Pending = 1,
    Paid = 2,
    Processing = 3,
    Shipping = 4,
    Completed = 5,
    Cancelled = 6,
    Refunded = 7
}

public enum PaymentMethod
{
    Stripe = 1,
    VNPay = 2,
    COD = 3
}

public enum PaymentStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4
}

public enum UserRole
{
    Customer = 1,
    Staff = 2,
    Admin = 3
}

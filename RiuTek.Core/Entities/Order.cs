using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities;

public class Order : BaseEntity, IAggregateRoot
{
    private readonly List<OrderItem> _items = [];
    private readonly List<PaymentAttempt> _paymentAttempts = [];

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string CheckoutIdempotencyKey { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string ShippingAddress { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "VND";
    public decimal TotalAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal FinalAmount { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string? Notes { get; private set; }

    // Navigation properties
    public User User { get; private set; } = null!;
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<PaymentAttempt> PaymentAttempts => _paymentAttempts.AsReadOnly();

    protected Order() { }

    public Order(
        string orderNumber,
        Guid userId,
        string checkoutIdempotencyKey,
        string customerName,
        string customerEmail,
        string customerPhone,
        string shippingAddress,
        PaymentMethod paymentMethod,
        string? notes = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("Order number cannot be empty.", nameof(orderNumber));
        if (orderNumber.Trim().Length > 50)
            throw new ArgumentException("Order number cannot exceed 50 characters.", nameof(orderNumber));
        if (string.IsNullOrWhiteSpace(checkoutIdempotencyKey))
            throw new ArgumentException("Checkout idempotency key cannot be empty.", nameof(checkoutIdempotencyKey));
        if (checkoutIdempotencyKey.Trim().Length > 128)
            throw new ArgumentException("Checkout idempotency key cannot exceed 128 characters.", nameof(checkoutIdempotencyKey));
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name cannot be empty.", nameof(customerName));
        if (customerName.Trim().Length > 150)
            throw new ArgumentException("Customer name cannot exceed 150 characters.", nameof(customerName));
        if (string.IsNullOrWhiteSpace(customerEmail))
            throw new ArgumentException("Customer email cannot be empty.", nameof(customerEmail));
        if (customerEmail.Trim().Length > 150)
            throw new ArgumentException("Customer email cannot exceed 150 characters.", nameof(customerEmail));
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new ArgumentException("Customer phone cannot be empty.", nameof(customerPhone));
        if (customerPhone.Trim().Length > 20)
            throw new ArgumentException("Customer phone cannot exceed 20 characters.", nameof(customerPhone));
        if (string.IsNullOrWhiteSpace(shippingAddress))
            throw new ArgumentException("Shipping address cannot be empty.", nameof(shippingAddress));
        if (shippingAddress.Trim().Length > 500)
            throw new ArgumentException("Shipping address cannot exceed 500 characters.", nameof(shippingAddress));
        if (notes?.Trim().Length > 1000)
            throw new ArgumentException("Notes cannot exceed 1000 characters.", nameof(notes));
        if (!Enum.IsDefined(paymentMethod))
            throw new ArgumentOutOfRangeException(nameof(paymentMethod), "Payment method is invalid.");

        OrderNumber = orderNumber.Trim();
        UserId = userId;
        CheckoutIdempotencyKey = checkoutIdempotencyKey.Trim();
        CustomerName = customerName.Trim();
        CustomerEmail = customerEmail.Trim().ToLowerInvariant();
        CustomerPhone = customerPhone.Trim();
        ShippingAddress = shippingAddress.Trim();
        PaymentMethod = paymentMethod;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = paymentMethod == PaymentMethod.COD
            ? OrderStatus.Confirmed
            : OrderStatus.PendingPayment;
        PaymentStatus = PaymentStatus.Pending;
    }

    public Result AddItem(
        Guid productId,
        string productName,
        string productSku,
        decimal unitPrice,
        int quantity = 1,
        Guid? pcBuildId = null)
    {
        if (productId == Guid.Empty)
            return Result.Failure(Error.Validation("Order.InvalidProductId", "ProductId is invalid."));
        if (string.IsNullOrWhiteSpace(productName))
            return Result.Failure(Error.Validation("Order.InvalidProductName", "Product name is required."));
        if (productName.Trim().Length > 255)
            return Result.Failure(Error.Validation("Order.InvalidProductName", "Product name cannot exceed 255 characters."));
        if (string.IsNullOrWhiteSpace(productSku))
            return Result.Failure(Error.Validation("Order.InvalidProductSku", "Product SKU is required."));
        if (productSku.Trim().Length > 100)
            return Result.Failure(Error.Validation("Order.InvalidProductSku", "Product SKU cannot exceed 100 characters."));
        if (unitPrice <= 0)
            return Result.Failure(Error.Validation("Order.InvalidUnitPrice", "Unit price must be greater than zero."));
        if (quantity < 1 || quantity > 99)
            return Result.Failure(Error.Validation("Order.InvalidQuantity", "Quantity must be between 1 and 99."));
        if (_items.Any(i => i.ProductId == productId))
            return Result.Failure(Error.Conflict("Order.DuplicateProduct", "Product already exists in the order."));

        _items.Add(new OrderItem(
            Id,
            productId,
            productName.Trim(),
            productSku.Trim().ToUpperInvariant(),
            unitPrice,
            quantity,
            pcBuildId));

        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result<PaymentAttempt> CreatePaymentAttempt(
        string idempotencyKey,
        DateTime expiresAtUtc,
        DateTime? nowUtc = null)
    {
        if (PaymentMethod == PaymentMethod.COD)
            return Result.Failure<PaymentAttempt>(Error.Conflict(
                "Payment.CodDoesNotUsePaymentAttempt",
                "COD orders do not create online payment attempts."));

        var normalizedKey = idempotencyKey?.Trim();
        var existing = _paymentAttempts.SingleOrDefault(p =>
            string.Equals(p.IdempotencyKey, normalizedKey, StringComparison.Ordinal));
        if (existing is not null)
            return Result.Success(existing);

        if (_paymentAttempts.Any(p => p.Status == PaymentAttemptStatus.Pending))
            return Result.Failure<PaymentAttempt>(Error.Conflict(
                "Payment.PendingAttemptExists",
                "The order already has a pending payment attempt."));

        if (FinalAmount <= 0)
            return Result.Failure<PaymentAttempt>(Error.Conflict(
                "Payment.OrderHasNoPayableAmount",
                "The order must contain a payable amount before creating a payment attempt."));

        try
        {
            var attempt = new PaymentAttempt(
                Id,
                PaymentMethod,
                FinalAmount,
                normalizedKey ?? string.Empty,
                expiresAtUtc,
                Currency,
                nowUtc);

            _paymentAttempts.Add(attempt);
            PaymentStatus = PaymentStatus.Pending;
            UpdatedAt = nowUtc ?? DateTime.UtcNow;
            return Result.Success(attempt);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<PaymentAttempt>(Error.Validation(
                "Payment.InvalidAttempt",
                ex.Message));
        }
    }

    public Result MarkPaymentSucceeded(Guid paymentAttemptId, DateTime completedAtUtc)
    {
        var attemptResult = FindPaymentAttempt(paymentAttemptId);
        if (attemptResult.IsFailure)
            return Result.Failure(attemptResult.Error);

        var result = attemptResult.Value.MarkSucceeded(completedAtUtc);
        if (result.IsFailure)
            return result;

        PaymentStatus = PaymentStatus.Completed;
        if (Status == OrderStatus.PendingPayment)
            Status = OrderStatus.Confirmed;
        UpdatedAt = completedAtUtc;
        return Result.Success();
    }

    public Result MarkPaymentFailed(Guid paymentAttemptId, string failureCode, DateTime failedAtUtc)
    {
        var attemptResult = FindPaymentAttempt(paymentAttemptId);
        if (attemptResult.IsFailure)
            return Result.Failure(attemptResult.Error);

        var result = attemptResult.Value.MarkFailed(failureCode, failedAtUtc);
        if (result.IsFailure)
            return result;

        PaymentStatus = PaymentStatus.Failed;
        UpdatedAt = failedAtUtc;
        return Result.Success();
    }

    public Result ExpirePaymentAttempt(Guid paymentAttemptId, DateTime expiredAtUtc)
    {
        var attemptResult = FindPaymentAttempt(paymentAttemptId);
        if (attemptResult.IsFailure)
            return Result.Failure(attemptResult.Error);

        var attempt = attemptResult.Value;
        if (attempt.Method != PaymentMethod.Stripe)
            return Result.Failure(Error.Conflict("Payment.InvalidMethod", "Only Stripe payment attempts can be expired via this method."));

        // If attempt is already expired and order is already cancelled, idempotent success
        if (attempt.Status == PaymentAttemptStatus.Expired && Status == OrderStatus.Cancelled)
            return Result.Success();

        if (Status != OrderStatus.PendingPayment)
            return Result.Failure(Error.Conflict("Order.InvalidStatusForExpiry", "Order must be in PendingPayment status to expire."));

        var result = attempt.MarkExpired(expiredAtUtc);
        if (result.IsFailure)
            return result;

        Status = OrderStatus.Cancelled;
        PaymentStatus = PaymentStatus.Failed;
        UpdatedAt = expiredAtUtc;
        return Result.Success();
    }

    private Result<PaymentAttempt> FindPaymentAttempt(Guid paymentAttemptId)
    {
        if (paymentAttemptId == Guid.Empty)
            return Result.Failure<PaymentAttempt>(Error.Validation(
                "Payment.InvalidAttemptId",
                "Payment attempt id is invalid."));

        var attempt = _paymentAttempts.SingleOrDefault(p => p.Id == paymentAttemptId);
        return attempt is null
            ? Result.Failure<PaymentAttempt>(Error.NotFound(
                "Payment.AttemptNotFound",
                "Payment attempt was not found for this order."))
            : Result.Success(attempt);
    }

    public Result SetDiscount(decimal discountAmount)
    {
        if (discountAmount < 0 || discountAmount > TotalAmount)
            return Result.Failure(Error.Validation(
                "Order.InvalidDiscount",
                "Discount must be between zero and the order total."));

        DiscountAmount = discountAmount;
        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    private void RecalculateTotals()
    {
        TotalAmount = Items.Sum(i => i.TotalPrice);
        FinalAmount = Math.Max(0, TotalAmount - DiscountAmount);
    }
}

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? PCBuildId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string ProductSku { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; } = 1;
    public decimal TotalPrice { get; private set; }

    // Navigation properties
    public Order Order { get; private set; } = null!;
    public Product Product { get; private set; } = null!;
    public PCBuild? PCBuild { get; private set; }

    protected OrderItem() { }

    public OrderItem(
        Guid orderId,
        Guid productId,
        string productName,
        string productSku,
        decimal unitPrice,
        int quantity = 1,
        Guid? pcBuildId = null)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        ProductSku = productSku;
        UnitPrice = unitPrice;
        Quantity = quantity;
        TotalPrice = unitPrice * quantity;
        PCBuildId = pcBuildId;
    }
}

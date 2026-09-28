using FluentAssertions;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Domain;

public class OrderPaymentDomainTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewOrder_WhenUserIdIsEmpty_ThrowsArgumentException()
    {
        var act = () => new Order(
            "ORD-1",
            Guid.Empty,
            "checkout-1",
            "Customer",
            "customer@riutek.test",
            "0900000000",
            "Ho Chi Minh City",
            PaymentMethod.COD);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public void NewOrder_WhenCheckoutIdempotencyKeyIsEmpty_ThrowsArgumentException()
    {
        var act = () => new Order(
            "ORD-1",
            Guid.NewGuid(),
            " ",
            "Customer",
            "customer@riutek.test",
            "0900000000",
            "Ho Chi Minh City",
            PaymentMethod.COD);

        act.Should().Throw<ArgumentException>().WithParameterName("checkoutIdempotencyKey");
    }

    [Theory]
    [InlineData(PaymentMethod.COD, OrderStatus.Confirmed)]
    [InlineData(PaymentMethod.Stripe, OrderStatus.PendingPayment)]
    [InlineData(PaymentMethod.VNPay, OrderStatus.PendingPayment)]
    public void NewOrder_StartsWithStatusRequiredByPaymentMethod(
        PaymentMethod paymentMethod,
        OrderStatus expectedStatus)
    {
        var order = CreateOrder(paymentMethod);

        order.Status.Should().Be(expectedStatus);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        order.Currency.Should().Be("VND");
        order.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public void AddItem_SnapshotsProductDataAndRecalculatesTotals()
    {
        var order = CreateOrder(PaymentMethod.COD);
        var productId = Guid.NewGuid();

        var addResult = order.AddItem(productId, "  RTX 5090  ", " gpu-5090 ", 50_000_000m, 2);
        var discountResult = order.SetDiscount(5_000_000m);

        addResult.IsSuccess.Should().BeTrue();
        discountResult.IsSuccess.Should().BeTrue();
        order.Items.Should().ContainSingle();
        order.Items.Single().Should().Match<OrderItem>(item =>
            item.ProductId == productId &&
            item.ProductName == "RTX 5090" &&
            item.ProductSku == "GPU-5090" &&
            item.UnitPrice == 50_000_000m &&
            item.Quantity == 2 &&
            item.TotalPrice == 100_000_000m);
        order.TotalAmount.Should().Be(100_000_000m);
        order.DiscountAmount.Should().Be(5_000_000m);
        order.FinalAmount.Should().Be(95_000_000m);
    }

    [Fact]
    public void AddItem_WhenProductAlreadyExists_RejectsDuplicateWithoutChangingTotals()
    {
        var order = CreateOrder(PaymentMethod.COD);
        var productId = Guid.NewGuid();
        order.AddItem(productId, "CPU", "CPU-1", 10_000_000m, 1);

        var result = order.AddItem(productId, "Changed name", "CHANGED", 1m, 3);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.DuplicateProduct");
        order.Items.Should().ContainSingle();
        order.TotalAmount.Should().Be(10_000_000m);
    }

    [Theory]
    [InlineData(0, 1, "Order.InvalidUnitPrice")]
    [InlineData(-1, 1, "Order.InvalidUnitPrice")]
    [InlineData(1000, 0, "Order.InvalidQuantity")]
    [InlineData(1000, 100, "Order.InvalidQuantity")]
    public void AddItem_WhenMoneyOrQuantityIsInvalid_LeavesOrderUntouched(
        decimal unitPrice,
        int quantity,
        string expectedCode)
    {
        var order = CreateOrder(PaymentMethod.COD);

        var result = order.AddItem(Guid.NewGuid(), "Product", "SKU-1", unitPrice, quantity);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        order.Items.Should().BeEmpty();
        order.TotalAmount.Should().Be(0);
    }

    [Fact]
    public void CreatePaymentAttempt_ForCodOrder_IsRejected()
    {
        var order = CreatePayableOrder(PaymentMethod.COD);

        var result = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.CodDoesNotUsePaymentAttempt");
        order.PaymentAttempts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PaymentMethod.COD, 1000, "key", "VND")]
    [InlineData(PaymentMethod.Stripe, 0, "key", "VND")]
    [InlineData(PaymentMethod.Stripe, 1000, " ", "VND")]
    [InlineData(PaymentMethod.Stripe, 1000, "key", "VN")]
    public void PaymentAttempt_WhenCoreInputIsInvalid_ThrowsArgumentException(
        PaymentMethod method,
        decimal amount,
        string idempotencyKey,
        string currency)
    {
        var act = () => new PaymentAttempt(
            Guid.NewGuid(),
            method,
            amount,
            idempotencyKey,
            Now.AddMinutes(15),
            currency,
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(PaymentMethod.Stripe)]
    [InlineData(PaymentMethod.VNPay)]
    public void CreatePaymentAttempt_ForOnlineOrder_SnapshotsPayableAmount(PaymentMethod method)
    {
        var order = CreatePayableOrder(method);

        var result = order.CreatePaymentAttempt(" attempt-1 ", Now.AddMinutes(15), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(order.Id);
        result.Value.Method.Should().Be(method);
        result.Value.Amount.Should().Be(order.FinalAmount);
        result.Value.Currency.Should().Be("VND");
        result.Value.IdempotencyKey.Should().Be("attempt-1");
        result.Value.Status.Should().Be(PaymentAttemptStatus.Pending);
        result.Value.CreatedAt.Should().Be(Now);
        order.PaymentAttempts.Should().ContainSingle();
    }

    [Fact]
    public void CreatePaymentAttempt_ReplayedKeyReturnsSameAttempt_AndDifferentKeyIsBlockedWhilePending()
    {
        var order = CreatePayableOrder(PaymentMethod.Stripe);
        var first = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now);

        var replay = order.CreatePaymentAttempt(" attempt-1 ", Now.AddMinutes(20), Now.AddMinutes(1));
        var concurrent = order.CreatePaymentAttempt("attempt-2", Now.AddMinutes(20), Now.AddMinutes(1));

        replay.IsSuccess.Should().BeTrue();
        replay.Value.Should().BeSameAs(first.Value);
        concurrent.IsFailure.Should().BeTrue();
        concurrent.Error.Code.Should().Be("Payment.PendingAttemptExists");
        order.PaymentAttempts.Should().ContainSingle();
    }

    [Fact]
    public void FailedAttempt_AllowsRetryWithoutOverwritingPaymentHistory()
    {
        var order = CreatePayableOrder(PaymentMethod.VNPay);
        var first = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now).Value;

        var failResult = order.MarkPaymentFailed(first.Id, "DECLINED", Now.AddMinutes(1));
        var retryResult = order.CreatePaymentAttempt("attempt-2", Now.AddMinutes(30), Now.AddMinutes(2));

        failResult.IsSuccess.Should().BeTrue();
        first.Status.Should().Be(PaymentAttemptStatus.Failed);
        first.FailureCode.Should().Be("DECLINED");
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        retryResult.IsSuccess.Should().BeTrue();
        order.PaymentAttempts.Should().HaveCount(2);
    }

    [Fact]
    public void SuccessfulOnlinePayment_ConfirmsOrderAndRejectsLaterTerminalTransition()
    {
        var order = CreatePayableOrder(PaymentMethod.Stripe);
        var attempt = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now).Value;
        attempt.SetProviderReference("pi_test_123");

        var successResult = order.MarkPaymentSucceeded(attempt.Id, Now.AddMinutes(1));
        var lateFailureResult = attempt.MarkFailed("LATE_FAILURE", Now.AddMinutes(2));

        successResult.IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
        attempt.Status.Should().Be(PaymentAttemptStatus.Succeeded);
        lateFailureResult.IsFailure.Should().BeTrue();
        lateFailureResult.Error.Code.Should().Be("Payment.InvalidStatusTransition");
    }

    [Fact]
    public void SuccessfulOnlinePayment_WithoutProviderReference_IsRejected()
    {
        var order = CreatePayableOrder(PaymentMethod.Stripe);
        var attempt = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now).Value;

        var result = order.MarkPaymentSucceeded(attempt.Id, Now.AddMinutes(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.ProviderReferenceRequired");
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.PaymentStatus.Should().Be(PaymentStatus.Pending);
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
    }

    [Fact]
    public void ExpiryTransition_BeforeConfiguredExpiry_IsRejectedWithoutMutation()
    {
        var order = CreatePayableOrder(PaymentMethod.Stripe);
        var attempt = order.CreatePaymentAttempt("attempt-1", Now.AddMinutes(15), Now).Value;

        var result = attempt.MarkExpired(Now.AddMinutes(14));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payment.InvalidTimestamp");
        attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
        attempt.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Order_DoesNotStoreProviderSpecificIdentifiers()
    {
        var propertyNames = typeof(Order).GetProperties().Select(property => property.Name);

        propertyNames.Should().NotContain("StripePaymentIntentId");
        propertyNames.Should().NotContain("VNPayTransactionNo");
        typeof(PaymentAttempt).GetProperty(nameof(PaymentAttempt.ProviderReference)).Should().NotBeNull();
    }

    private static Order CreatePayableOrder(PaymentMethod method)
    {
        var order = CreateOrder(method);
        order.AddItem(Guid.NewGuid(), "Product", "SKU-1", 10_000_000m, 1);
        return order;
    }

    private static Order CreateOrder(PaymentMethod method) => new(
        $"ORD-{Guid.NewGuid():N}",
        Guid.NewGuid(),
        $"checkout-{Guid.NewGuid():N}",
        "Nguyen Van A",
        "CUSTOMER@EXAMPLE.COM",
        "0900000000",
        "Ho Chi Minh City",
        method,
        "Giao gio hanh chinh");
}

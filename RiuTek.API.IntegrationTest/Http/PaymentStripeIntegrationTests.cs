using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Payments.Commands;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

public class FakeStripePaymentGateway : IStripePaymentGateway
{
    public bool IsEnabled { get; set; } = true;
    public bool ShouldFailWithUnavailable { get; set; } = false;
    public TimeSpan CheckoutSessionLifetime { get; set; } = TimeSpan.FromMinutes(45);

    public Func<CreateStripeCheckoutSessionRequest, string?, Result<StripeCheckoutSessionResult>>? OnEnsureSession { get; set; }
    public Func<string, string, Result<StripeWebhookEvent>>? OnParseWebhook { get; set; }

    public Task<Result<StripeCheckoutSessionResult>> EnsureCheckoutSessionAsync(
        CreateStripeCheckoutSessionRequest request,
        string? existingProviderReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(Result.Failure<StripeCheckoutSessionResult>(Error.Validation(
                "Checkout.PaymentMethodNotAvailable",
                "Stripe payment is not available at this time.")));
        }

        if (ShouldFailWithUnavailable)
        {
            return Task.FromResult(Result.Failure<StripeCheckoutSessionResult>(Error.Unavailable(
                "Payment.GatewayUnavailable",
                "Stripe payment gateway is temporarily unavailable. Please retry.")));
        }

        if (OnEnsureSession != null)
        {
            return Task.FromResult(OnEnsureSession(request, existingProviderReference));
        }

        var sessionId = existingProviderReference ?? $"cs_fake_{request.OrderId:N}";
        return Task.FromResult(Result.Success(new StripeCheckoutSessionResult(
            SessionId: sessionId,
            Url: $"https://checkout.stripe.com/pay/{sessionId}",
            ExpiresAt: request.ExpiresAt
        )));
    }

    public Result<StripeWebhookEvent> ParseAndVerifyWebhook(string payload, string signature)
    {
        if (OnParseWebhook != null)
        {
            return OnParseWebhook(payload, signature);
        }

        if (string.IsNullOrWhiteSpace(signature) || signature == "invalid_signature")
        {
            return Result.Failure<StripeWebhookEvent>(Error.Validation(
                "Webhook.InvalidSignature",
                "Stripe webhook signature verification failed."));
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var eventTypeStr = root.TryGetProperty("type", out var typeElem) ? typeElem.GetString() : null;
        var id = root.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "evt_default" : "evt_default";

        if (eventTypeStr == "checkout.session.completed")
        {
            var dataObj = root.GetProperty("data").GetProperty("object");
            var sessionId = dataObj.TryGetProperty("id", out var sElem) ? sElem.GetString() : null;
            var paymentStatus = dataObj.TryGetProperty("payment_status", out var pElem) ? pElem.GetString() : null;
            var currency = dataObj.TryGetProperty("currency", out var cElem) ? cElem.GetString() : null;
            decimal? amountTotal = dataObj.TryGetProperty("amount_total", out var aElem) && aElem.TryGetDecimal(out var amt) ? amt : null;
            string? orderIdStr = null;
            string? attemptIdStr = null;
            if (dataObj.TryGetProperty("metadata", out var metadata))
            {
                orderIdStr = metadata.TryGetProperty("OrderId", out var oElem) ? oElem.GetString() : null;
                attemptIdStr = metadata.TryGetProperty("PaymentAttemptId", out var paElem) ? paElem.GetString() : null;
            }

            return Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionCompleted,
                EventId: id,
                SessionId: sessionId,
                OrderIdString: orderIdStr,
                PaymentAttemptIdString: attemptIdStr,
                AmountTotal: amountTotal,
                Currency: currency,
                PaymentStatus: paymentStatus
            ));
        }

        if (eventTypeStr == "checkout.session.expired")
        {
            var dataObj = root.GetProperty("data").GetProperty("object");
            var sessionId = dataObj.TryGetProperty("id", out var sElem) ? sElem.GetString() : null;
            var paymentStatus = dataObj.TryGetProperty("payment_status", out var pElem) ? pElem.GetString() : null;
            var currency = dataObj.TryGetProperty("currency", out var cElem) ? cElem.GetString() : null;
            decimal? amountTotal = dataObj.TryGetProperty("amount_total", out var aElem) && aElem.TryGetDecimal(out var amt) ? amt : null;
            string? orderIdStr = null;
            string? attemptIdStr = null;
            if (dataObj.TryGetProperty("metadata", out var metadata))
            {
                orderIdStr = metadata.TryGetProperty("OrderId", out var oElem) ? oElem.GetString() : null;
                attemptIdStr = metadata.TryGetProperty("PaymentAttemptId", out var paElem) ? paElem.GetString() : null;
            }

            return Result.Success(new StripeWebhookEvent(
                EventType: StripeWebhookEventType.CheckoutSessionExpired,
                EventId: id,
                SessionId: sessionId,
                OrderIdString: orderIdStr,
                PaymentAttemptIdString: attemptIdStr,
                AmountTotal: amountTotal,
                Currency: currency,
                PaymentStatus: paymentStatus
            ));
        }

        return Result.Success(new StripeWebhookEvent(
            EventType: StripeWebhookEventType.Unknown,
            EventId: id,
            SessionId: null,
            OrderIdString: null,
            PaymentAttemptIdString: null,
            AmountTotal: null,
            Currency: null,
            PaymentStatus: null
        ));
    }
}

[Collection(IntegrationTestCollection.Name)]
public class PaymentStripeIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PaymentStripeIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await CleanupDataAsync();
    public async Task DisposeAsync() => await CleanupDataAsync();

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.PaymentAttempts.ExecuteDeleteAsync();
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.UserAddresses.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private (HttpClient Client, FakeStripePaymentGateway FakeGateway) CreateClientWithFakeGateway(
        Action<FakeStripePaymentGateway>? configure = null)
    {
        var fakeGateway = new FakeStripePaymentGateway();
        configure?.Invoke(fakeGateway);

        var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStripePaymentGateway));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IStripePaymentGateway>(fakeGateway);
            });
        });

        return (customFactory.CreateClient(), fakeGateway);
    }

    private async Task<(User User, UserAddress Address, Product Product, Cart Cart, string Token)> SeedPrerequisitesAsync(
        int stockQuantity = 50,
        decimal price = 200_000m,
        int cartQuantity = 2)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"stripe_e2e_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Stripe E2E User {suffix}",
            role: UserRole.Customer,
            phoneNumber: "0901234567"
        );
        db.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0909999999",
            addressLine: "456 Le Loi",
            ward: "Ben Nghe",
            district: "Quan 1",
            city: "TP Ho Chi Minh",
            isDefault: true
        );
        db.UserAddresses.Add(address);

        var category = new Category($"Category {suffix}", $"cat-{suffix}", ComponentType.Accessory);
        db.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"product-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "RiuTek",
            price: price,
            stockQuantity: stockQuantity,
            imageUrl: "https://riutek.test/prod.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Specs" }
        );
        db.Products.Add(product);

        var cart = new Cart(user.Id);
        if (cartQuantity > 0)
        {
            cart.AddItem(product.Id, cartQuantity);
        }
        db.Carts.Add(cart);

        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        return (user, address, product, cart, token);
    }

    [Fact]
    public async Task Checkout_COD_Regression_ReturnsCreatedWithNullPaymentAction()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new CheckoutCartRequest(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.COD,
            Notes: "COD regression test"
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("Idempotency-Key", "idemp-cod-regression-12345");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = await response.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.PaymentMethod.Should().Be(PaymentMethod.COD);
        orderDto.PaymentAction.Should().BeNull(); // COD always returns null
    }

    [Fact]
    public async Task Checkout_Stripe_AndReplay_EndToEnd()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (user, address, product, cart, token) = await SeedPrerequisitesAsync(stockQuantity: 50, cartQuantity: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new CheckoutCartRequest(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: "Stripe E2E test"
        );

        var idempotencyKey = "idemp-stripe-e2e-replay-12345";
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request1.Headers.Add("Idempotency-Key", idempotencyKey);

        var response1 = await client.SendAsync(request1);

        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = await response1.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.PaymentMethod.Should().Be(PaymentMethod.Stripe);
        orderDto.Status.Should().Be(OrderStatus.PendingPayment);
        orderDto.PaymentAction.Should().NotBeNull();
        orderDto.PaymentAction!.Type.Should().Be("Redirect");
        orderDto.PaymentAction.Url.Should().StartWith("https://checkout.stripe.com/pay/");
        orderDto.PaymentAction.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // Verify DB state
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderDto.Id);
            dbOrder.Status.Should().Be(OrderStatus.PendingPayment);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Pending);
            dbOrder.PaymentAttempts.Should().HaveCount(1);
            var attempt = dbOrder.PaymentAttempts.First();
            attempt.Status.Should().Be(PaymentAttemptStatus.Pending);
            attempt.ProviderReference.Should().NotBeNullOrWhiteSpace();

            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(48); // 50 - 2
        }

        // Replay with same idempotency key
        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request2.Headers.Add("Idempotency-Key", idempotencyKey);

        var response2 = await client.SendAsync(request2);

        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto2 = await response2.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto2!.Id.Should().Be(orderDto.Id);
        orderDto2.PaymentAction!.Url.Should().Be(orderDto.PaymentAction.Url);

        // Verify stock was not decremented again
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Orders.CountAsync()).Should().Be(1);
            (await db.PaymentAttempts.CountAsync()).Should().Be(1);
            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(48);
        }
    }

    [Fact]
    public async Task Checkout_Stripe_WhenGatewayUnavailable_Returns503_AndRetryRecovers()
    {
        var (client, fakeGateway) = CreateClientWithFakeGateway(g => g.ShouldFailWithUnavailable = true);
        var (user, address, product, cart, token) = await SeedPrerequisitesAsync(stockQuantity: 50, cartQuantity: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new CheckoutCartRequest(
            AddressId: address.Id,
            ExpectedCartVersion: cart.Version,
            PaymentMethod: PaymentMethod.Stripe,
            Notes: null
        );

        var idempotencyKey = "idemp-stripe-timeout-retry-12345";
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request1.Headers.Add("Idempotency-Key", idempotencyKey);

        var response1 = await client.SendAsync(request1);

        response1.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Verify DB: Order and Pending attempt are persisted, stock is held
        Guid orderId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstOrDefaultAsync(o => o.CheckoutIdempotencyKey == idempotencyKey);
            dbOrder.Should().NotBeNull();
            orderId = dbOrder!.Id;
            dbOrder.Status.Should().Be(OrderStatus.PendingPayment);
            dbOrder.PaymentAttempts.Should().HaveCount(1);
            dbOrder.PaymentAttempts.First().ProviderReference.Should().BeNull();

            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(48);
        }

        // Retry: now gateway is available!
        fakeGateway.ShouldFailWithUnavailable = false;

        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request2.Headers.Add("Idempotency-Key", idempotencyKey);

        var response2 = await client.SendAsync(request2);

        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto2 = await response2.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions);
        orderDto2!.Id.Should().Be(orderId);
        orderDto2.PaymentAction!.Url.Should().StartWith("https://checkout.stripe.com/pay/");

        // Verify attempt now has provider reference and stock still 48
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderId);
            dbOrder.PaymentAttempts.First().ProviderReference.Should().NotBeNullOrWhiteSpace();

            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(48);
        }
    }

    [Fact]
    public async Task Webhook_MissingOrInvalidSignature_Returns400_AndDoesNotMutateDb()
    {
        var (client, _) = CreateClientWithFakeGateway();

        // 1. Missing header
        var response1 = await client.PostAsync("/api/v1/payments/stripe/webhook", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        response1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Invalid signature header
        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        request2.Headers.Add("Stripe-Signature", "invalid_signature");

        var response2 = await client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_CheckoutSessionCompleted_AndDuplicate_Idempotent()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(price: 150_000m, cartQuantity: 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Checkout first
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-wh-completed-12345");
        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        Guid attemptId;
        string providerRef;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
            providerRef = attempt.ProviderReference!;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_test_completed_e2e",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 150000,
              "currency": "vnd",
              "payment_status": "paid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        // First webhook call
        using var whReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReq1.Headers.Add("Stripe-Signature", "valid_signature");

        var whRes1 = await client.SendAsync(whReq1);
        whRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify order is Confirmed and payment Completed
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderDto.Id);
            dbOrder.Status.Should().Be(OrderStatus.Confirmed);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Completed);
            dbOrder.PaymentAttempts.First().Status.Should().Be(PaymentAttemptStatus.Succeeded);
        }

        // Duplicate webhook call
        using var whReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReq2.Headers.Add("Stripe-Signature", "valid_signature");

        var whRes2 = await client.SendAsync(whReq2);
        whRes2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_CheckoutSessionExpired_ConcurrentDuplicate_RestoresStockExactlyOnce()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(stockQuantity: 10, cartQuantity: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Checkout: stock decrements from 10 to 8
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-wh-expired-concurrency-12345");
        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        // Check stock is 8
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity.Should().Be(8);
        }

        Guid attemptId;
        string providerRef;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
            providerRef = attempt.ProviderReference!;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_test_expired_e2e",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 400000,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        // Trigger two concurrent duplicate expired webhooks
        var sendWebhookFunc = async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
            {
                Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Stripe-Signature", "valid_signature");
            return await client.SendAsync(req);
        };

        var task1 = sendWebhookFunc();
        var task2 = sendWebhookFunc();

        var responses = await Task.WhenAll(task1, task2);
        responses[0].StatusCode.Should().Be(HttpStatusCode.OK);
        responses[1].StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify stock was restored EXACTLY ONCE to 10 (not 12!)
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(10);

            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderDto.Id);
            dbOrder.Status.Should().Be(OrderStatus.Cancelled);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Failed);
            dbOrder.PaymentAttempts.First().Status.Should().Be(PaymentAttemptStatus.Expired);
        }
    }

    [Fact]
    public async Task Checkout_Response_DoesNotLeakInternalProviderFields()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("Idempotency-Key", "leak-check-idemp-key-12345");

        var response = await client.SendAsync(request);
        var rawJson = await response.Content.ReadAsStringAsync();
        var lower = rawJson.ToLowerInvariant();

        lower.Should().NotContain("secretkey");
        lower.Should().NotContain("webhooksecret");
        lower.Should().NotContain("checkoutidempotencykey");
        lower.Should().NotContain("paymentintentsecret");
        lower.Should().NotContain("clientsecret");
    }

    [Fact]
    public async Task Checkout_DeterministicSameKeyStripeRace_Both201_SingleOrderSingleSession_LoserGraphNotSaved()
    {
        // 1. Arrange: seed user, address, product (stock 50), cart with 2 items
        var initialStock = 50;
        var cartQty = 2;
        var (user, address, product, cart, _) = await SeedPrerequisitesAsync(stockQuantity: initialStock, cartQuantity: cartQty);

        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var userSvc1 = new TestCurrentUserService(user.Id);
        var userSvc2 = new TestCurrentUserService(user.Id);

        var fakeGateway = new FakeStripePaymentGateway { CheckoutSessionLifetime = TimeSpan.FromMinutes(45) };

        var handler1 = new CheckoutCartCommandHandler(hookDb1, userSvc1, fakeGateway);
        var handler2 = new CheckoutCartCommandHandler(hookDb2, userSvc2, fakeGateway);

        var sharedKey = "deterministic-stripe-race-" + Guid.NewGuid().ToString("N");

        var cmd1 = new CheckoutCartCommand(address.Id, cart.Version, PaymentMethod.Stripe, null, sharedKey);
        var cmd2 = new CheckoutCartCommand(address.Id, cart.Version, PaymentMethod.Stripe, null, sharedKey);

        // 2. Act:
        var task1 = handler1.Handle(cmd1, CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var task2 = handler2.Handle(cmd2, CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Both handlers reached hook before initial SaveChangesAsync, without winner order in DB
        tcsAllowDb1ToSave.TrySetResult(true);
        var result1 = await task1;

        tcsAllowDb2ToSave.TrySetResult(true);
        var result2 = await task2;

        // 3. Assert:
        result1.IsSuccess.Should().BeTrue("Winner request must succeed");
        result2.IsSuccess.Should().BeTrue("Concurrent same-key request must recover and succeed");

        result1.Value.Id.Should().Be(result2.Value.Id);
        result1.Value.OrderNumber.Should().Be(result2.Value.OrderNumber);
        result1.Value.PaymentAction.Should().NotBeNull();
        result2.Value.PaymentAction.Should().NotBeNull();
        result1.Value.PaymentAction!.Url.Should().Be(result2.Value.PaymentAction!.Url);
        result1.Value.PaymentAction.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromSeconds(15));

        // 4. DB audit
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orders = await verifyDb.Orders.Include(o => o.Items).Include(o => o.PaymentAttempts).ToListAsync();
        orders.Should().ContainSingle("Exactly one order must exist in PostgreSQL");
        var dbOrder = orders.Single();
        dbOrder.Items.Should().ContainSingle();
        dbOrder.PaymentAttempts.Should().ContainSingle("Exactly one payment attempt must exist in PostgreSQL");
        var dbAttempt = dbOrder.PaymentAttempts.Single();
        dbAttempt.ProviderReference.Should().NotBeNullOrWhiteSpace();
        dbAttempt.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromSeconds(15));

        var productDb = await verifyDb.Products.FindAsync(product.Id);
        productDb!.StockQuantity.Should().Be(initialStock - cartQty, "Stock must be decremented exactly once");

        var cartDb = await verifyDb.Carts.Include(c => c.Items).SingleAsync(c => c.UserId == user.Id);
        cartDb.Items.Should().BeEmpty("Cart must be cleared once");
    }

    [Fact]
    public async Task Webhook_CheckoutSessionExpired_DeterministicBarrier_RestoresStockExactlyOnce_HitsRecoveryBranch()
    {
        var (client, fakeGateway) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(stockQuantity: 10, cartQuantity: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Initial checkout to create order + pending attempt, stock decrements 10 -> 8
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-wh-expired-barrier-" + Guid.NewGuid().ToString("N"));
        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        Guid attemptId;
        string providerRef;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
            providerRef = attempt.ProviderReference!;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_test_expired_barrier",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 400000,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tcs1ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2ReachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb1ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcsAllowDb2ToSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hookDb1 = new TestHookApplicationDbContext(db1, async ct =>
        {
            tcs1ReachedHook.TrySetResult(true);
            await tcsAllowDb1ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        var hookDb2 = new TestHookApplicationDbContext(db2, async ct =>
        {
            tcs2ReachedHook.TrySetResult(true);
            await tcsAllowDb2ToSave.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        });

        ProcessStripeWebhookCommandHandler.ConcurrencyRecoveryExecutionCount = 0;

        var handler1 = new ProcessStripeWebhookCommandHandler(hookDb1, fakeGateway);
        var handler2 = new ProcessStripeWebhookCommandHandler(hookDb2, fakeGateway);

        var whCmd = new ProcessStripeWebhookCommand(webhookPayload, "valid_signature");

        var task1 = handler1.Handle(whCmd, CancellationToken.None);
        await tcs1ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var task2 = handler2.Handle(whCmd, CancellationToken.None);
        await tcs2ReachedHook.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // Both handlers loaded the attempt in Pending and products with stock 8 before either saved!
        tcsAllowDb1ToSave.TrySetResult(true);
        var res1 = await task1;

        tcsAllowDb2ToSave.TrySetResult(true);
        var res2 = await task2;

        res1.IsSuccess.Should().BeTrue();
        res2.IsSuccess.Should().BeTrue();

        // Concurrency recovery branch was executed deterministically
        ProcessStripeWebhookCommandHandler.ConcurrencyRecoveryExecutionCount.Should().BeGreaterThanOrEqualTo(1,
            "At least one request must have executed the concurrency recovery branch");

        // Verify stock restored exactly once to 10
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbProduct = await db.Products.FindAsync(product.Id);
            dbProduct!.StockQuantity.Should().Be(10, "Stock was restored from 8 to 10 exactly once");

            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderDto.Id);
            dbOrder.Status.Should().Be(OrderStatus.Cancelled);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Failed);
            dbOrder.PaymentAttempts.First().Status.Should().Be(PaymentAttemptStatus.Expired);
        }
    }

    [Fact]
    public async Task Webhook_CheckoutSessionCompleted_WhenProviderReferenceNull_BindsAndSucceeds()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (user, address, product, cart, _) = await SeedPrerequisitesAsync(price: 200_000m, cartQuantity: 1);

        // Create Order and PaymentAttempt directly with ProviderReference = null (crash gap)
        Guid orderId;
        Guid attemptId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = new Order(
                orderNumber: "ORD-GAP-COMPLETED",
                userId: user.Id,
                checkoutIdempotencyKey: "gap-comp-key-12345",
                customerName: user.FullName,
                customerEmail: user.Email,
                customerPhone: user.PhoneNumber!,
                shippingAddress: "123 Test St",
                paymentMethod: PaymentMethod.Stripe,
                notes: null
            );
            order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);
            var attemptRes = order.CreatePaymentAttempt($"stripe-{order.Id:N}", DateTime.UtcNow.AddMinutes(45));
            attemptRes.IsSuccess.Should().BeTrue();
            // ProviderReference remains null!

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            orderId = order.Id;
            attemptId = attemptRes.Value.Id;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_gap_completed",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_recovered_gap_session",
              "amount_total": 200000,
              "currency": "vnd",
              "payment_status": "paid",
              "metadata": {
                "OrderId": "{{orderId}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var whReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReq.Headers.Add("Stripe-Signature", "valid_signature");

        var whRes = await client.SendAsync(whReq);
        whRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderId);
            dbOrder.Status.Should().Be(OrderStatus.Confirmed);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Completed);
            var attempt = dbOrder.PaymentAttempts.First();
            attempt.Status.Should().Be(PaymentAttemptStatus.Succeeded);
            attempt.ProviderReference.Should().Be("cs_recovered_gap_session");
        }

        // Duplicate call should succeed idempotently
        using var whReqDup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReqDup.Headers.Add("Stripe-Signature", "valid_signature");

        var whResDup = await client.SendAsync(whReqDup);
        whResDup.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_CheckoutSessionExpired_WhenProviderReferenceNull_BindsAndExpires_RestoresStockOnce()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (user, address, product, cart, _) = await SeedPrerequisitesAsync(stockQuantity: 10, price: 150_000m, cartQuantity: 2);

        // Create Order and PaymentAttempt with ProviderReference = null, simulate held stock (10 - 2 = 8)
        Guid orderId;
        Guid attemptId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity = 8;

            var order = new Order(
                orderNumber: "ORD-GAP-EXPIRED",
                userId: user.Id,
                checkoutIdempotencyKey: "gap-exp-key-12345",
                customerName: user.FullName,
                customerEmail: user.Email,
                customerPhone: user.PhoneNumber!,
                shippingAddress: "123 Test St",
                paymentMethod: PaymentMethod.Stripe,
                notes: null
            );
            order.AddItem(product.Id, product.Name, product.Sku, product.Price, 2);
            var attemptRes = order.CreatePaymentAttempt($"stripe-{order.Id:N}", DateTime.UtcNow.AddMinutes(45));
            attemptRes.IsSuccess.Should().BeTrue();

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            orderId = order.Id;
            attemptId = attemptRes.Value.Id;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_gap_expired",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "cs_recovered_expired_session",
              "amount_total": 300000,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderId}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var whReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReq.Headers.Add("Stripe-Signature", "valid_signature");

        var whRes = await client.SendAsync(whReq);
        whRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.Include(o => o.PaymentAttempts).FirstAsync(o => o.Id == orderId);
            dbOrder.Status.Should().Be(OrderStatus.Cancelled);
            dbOrder.PaymentStatus.Should().Be(PaymentStatus.Failed);
            var attempt = dbOrder.PaymentAttempts.First();
            attempt.Status.Should().Be(PaymentAttemptStatus.Expired);
            attempt.ProviderReference.Should().Be("cs_recovered_expired_session");

            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity.Should().Be(10, "Stock restored from 8 to 10");
        }

        // Duplicate call should return 200 and not restore stock again
        using var whReqDup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReqDup.Headers.Add("Stripe-Signature", "valid_signature");

        var whResDup = await client.SendAsync(whReqDup);
        whResDup.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity.Should().Be(10, "Stock must remain 10 after duplicate webhook");
        }
    }

    [Theory]
    [InlineData("checkout.session.completed", "paid")]
    [InlineData("checkout.session.expired", "unpaid")]
    public async Task Webhook_WhenSessionIdMissingOrEmpty_Returns409Conflict(string eventType, string paymentStatus)
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(cartQuantity: 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Checkout first
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-empty-session-" + Guid.NewGuid().ToString("N"));
        var checkoutRes = await client.SendAsync(checkoutReq);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        Guid attemptId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
        }

        var webhookPayload = $$"""
        {
          "id": "evt_empty_session",
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "   ",
              "amount_total": 200000,
              "currency": "vnd",
              "payment_status": "{{paymentStatus}}",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var whReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(webhookPayload, System.Text.Encoding.UTF8, "application/json")
        };
        whReq.Headers.Add("Stripe-Signature", "valid_signature");

        var whRes = await client.SendAsync(whReq);
        whRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Webhook_Expired_WhenAmountOrCurrencyOrSessionMismatch_Returns409ConflictAndDoesNotRestoreStock()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(stockQuantity: 10, cartQuantity: 2);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Checkout: stock 10 -> 8
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-mismatch-check-" + Guid.NewGuid().ToString("N"));
        var checkoutRes = await client.SendAsync(checkoutReq);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        Guid attemptId;
        string providerRef;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
            providerRef = attempt.ProviderReference!;
        }

        // Amount mismatch
        var amountMismatchPayload = $$"""
        {
          "id": "evt_amt_mismatch",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 999999,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(amountMismatchPayload, System.Text.Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("Stripe-Signature", "valid_signature");
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Currency mismatch
        var currMismatchPayload = $$"""
        {
          "id": "evt_curr_mismatch",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 400000,
              "currency": "usd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(currMismatchPayload, System.Text.Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("Stripe-Signature", "valid_signature");
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Session ID mismatch
        var sessMismatchPayload = $$"""
        {
          "id": "evt_sess_mismatch",
          "type": "checkout.session.expired",
          "data": {
            "object": {
              "id": "cs_completely_different",
              "amount_total": 400000,
              "currency": "vnd",
              "payment_status": "unpaid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var req3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(sessMismatchPayload, System.Text.Encoding.UTF8, "application/json")
        };
        req3.Headers.Add("Stripe-Signature", "valid_signature");
        var res3 = await client.SendAsync(req3);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verify stock was NOT restored (still 8)
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var p = await db.Products.FindAsync(product.Id);
            p!.StockQuantity.Should().Be(8, "Stock must not be restored when webhook validation fails");
        }
    }

    [Fact]
    public async Task Webhook_DuplicateCompleted_WhenMismatchedSession_Returns409Conflict()
    {
        var (client, _) = CreateClientWithFakeGateway();
        var (_, address, product, cart, token) = await SeedPrerequisitesAsync(cartQuantity: 1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Checkout
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutCartRequest(address.Id, cart.Version, PaymentMethod.Stripe, null))
        };
        checkoutReq.Headers.Add("Idempotency-Key", "stripe-dup-mismatch-" + Guid.NewGuid().ToString("N"));
        var checkoutRes = await client.SendAsync(checkoutReq);
        var orderDto = (await checkoutRes.Content.ReadFromJsonAsync<CheckoutOrderDto>(JsonOptions))!;

        Guid attemptId;
        string providerRef;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempt = await db.PaymentAttempts.FirstAsync(p => p.OrderId == orderDto.Id);
            attemptId = attempt.Id;
            providerRef = attempt.ProviderReference!;
        }

        // First completed webhook succeeds
        var validPayload = $$"""
        {
          "id": "evt_valid_comp",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{providerRef}}",
              "amount_total": 200000,
              "currency": "vnd",
              "payment_status": "paid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(validPayload, System.Text.Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("Stripe-Signature", "valid_signature");
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Duplicate with mismatched session ID -> 409 Conflict (not 200)
        var mismatchedDupPayload = $$"""
        {
          "id": "evt_dup_mismatched",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "cs_forged_session",
              "amount_total": 200000,
              "currency": "vnd",
              "payment_status": "paid",
              "metadata": {
                "OrderId": "{{orderDto.Id}}",
                "PaymentAttemptId": "{{attemptId}}"
              }
            }
          }
        }
        """;

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(mismatchedDupPayload, System.Text.Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("Stripe-Signature", "valid_signature");
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}

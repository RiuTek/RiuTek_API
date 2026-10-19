using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class ReviewIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record BusinessErrorResponse(string Code, string Description);

    public ReviewIntegrationTests(PostgreSqlContainerFixture fixture)
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
            await db.StockMovements.ExecuteDeleteAsync();
            await db.Reviews.ExecuteDeleteAsync();
            await db.PaymentAttempts.ExecuteDeleteAsync();
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.PCBuildItems.ExecuteDeleteAsync();
            await db.PCBuilds.ExecuteDeleteAsync();
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

    private async Task<(HttpClient Client, User User)> CreateUserClientAsync(UserRole role = UserRole.Customer, bool isActive = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"user_{suffix}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"User {suffix}",
            role: role)
        {
            IsActive = isActive
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = jwtGenerator.GenerateAccessToken(user);
        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, user);
    }

    private async Task<(Category Category, Product Product)> SeedProductAsync(decimal price = 500_000m)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(
            name: $"Category {suffix}",
            slug: $"category-{suffix}",
            componentType: ComponentType.Accessory);
        db.Categories.Add(category);

        var product = new Product(
            categoryId: category.Id,
            name: $"Product {suffix}",
            slug: $"product-{suffix}",
            sku: $"SKU-{suffix}",
            brand: "RiuTek",
            price: price,
            stockQuantity: 50,
            imageUrl: "https://riutek.test/img.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification { Details = "Sample spec" });
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (category, product);
    }

    private async Task<Order> SeedOrderAsync(
        User user,
        Product product,
        PaymentMethod paymentMethod = PaymentMethod.COD,
        OrderStatus orderStatus = OrderStatus.Completed)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var order = new Order(
            orderNumber: $"ORD-{suffix}",
            userId: user.Id,
            checkoutIdempotencyKey: $"key-{suffix}",
            customerName: user.FullName,
            customerEmail: user.Email,
            customerPhone: "0901234567",
            shippingAddress: "123 Street",
            paymentMethod: paymentMethod);

        order.AddItem(product.Id, product.Name, product.Sku, product.Price, 1);

        if (paymentMethod == PaymentMethod.COD)
        {
            if (orderStatus != OrderStatus.Confirmed)
            {
                if (orderStatus == OrderStatus.Cancelled)
                {
                    order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Cancelled);
                }
                else
                {
                    order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
                    if (orderStatus != OrderStatus.Processing)
                    {
                        order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
                        if (orderStatus != OrderStatus.Shipping)
                        {
                            order.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);
                        }
                    }
                }
            }
        }
        else if (paymentMethod == PaymentMethod.Stripe)
        {
            var attemptResult = order.CreatePaymentAttempt($"stripe-{suffix}", DateTime.UtcNow.AddMinutes(30));
            if (orderStatus == OrderStatus.Completed)
            {
                attemptResult.Value.SetProviderReference($"pi_{suffix}");
                order.MarkPaymentSucceeded(attemptResult.Value.Id, DateTime.UtcNow);
                order.ChangeFulfillmentStatus(OrderStatus.Confirmed, OrderStatus.Processing);
                order.ChangeFulfillmentStatus(OrderStatus.Processing, OrderStatus.Shipping);
                order.ChangeFulfillmentStatus(OrderStatus.Shipping, OrderStatus.Completed);
            }
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return order;
    }

    // 1. Check Constraint test
    [Fact]
    public async Task Database_RatingCheckConstraint_RejectsInvalidRatings()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (_, user) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(user, product);

        // Attempt raw SQL insert violating CK_Reviews_Rating_Range
        var invalidInsertAct = async () =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Reviews" ("Id", "ProductId", "UserId", "OrderId", "Rating", "CreatedAt", "Images")
                VALUES ({0}, {1}, {2}, {3}, 6, NOW(), '[]'::jsonb);
                """,
                Guid.NewGuid(), product.Id, user.Id, order.Id);
        };

        await invalidInsertAct.Should().ThrowAsync<Exception>();
    }

    // 2. Full HTTP Auth Matrix
    [Fact]
    public async Task HttpAuthMatrix_EnforcesCorrectAccessControl()
    {
        var anonymousClient = _fixture.Factory.CreateClient();
        var (customerClient, customer) = await CreateUserClientAsync(UserRole.Customer);
        var (staffClient, _) = await CreateUserClientAsync(UserRole.Staff);
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(customer, product);

        // Public GET: Anonymous allowed
        var publicRes = await anonymousClient.GetAsync($"/api/v1/products/{product.Id}/reviews");
        publicRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Customer GET mine: Anonymous -> 401; Customer -> 200
        var anonMineRes = await anonymousClient.GetAsync("/api/v1/reviews/mine");
        anonMineRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var custMineRes = await customerClient.GetAsync("/api/v1/reviews/mine");
        custMineRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // POST review: Anonymous -> 401; Staff -> 403; Customer -> 201
        var req = new CreateReviewRequest(order.Id, product.Id, 5, "Great", "Content");
        var anonPostRes = await anonymousClient.PostAsJsonAsync("/api/v1/reviews", req);
        anonPostRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var staffPostRes = await staffClient.PostAsJsonAsync("/api/v1/reviews", req);
        staffPostRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var custPostRes = await customerClient.PostAsJsonAsync("/api/v1/reviews", req);
        custPostRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdReview = await custPostRes.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        var reviewId = createdReview!.Id;

        // Admin endpoints: Customer -> 403; Staff -> 200/204
        var custAdminList = await customerClient.GetAsync("/api/v1/reviews/admin");
        custAdminList.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staffAdminList = await staffClient.GetAsync("/api/v1/reviews/admin");
        staffAdminList.StatusCode.Should().Be(HttpStatusCode.OK);

        // Staff reply: Customer -> 403; Staff -> 200
        var custReplyRes = await customerClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new SetReviewReplyRequest("Cust reply"));
        custReplyRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staffReplyRes = await staffClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new SetReviewReplyRequest("Staff reply"));
        staffReplyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Staff delete reply: Customer -> 403; Staff -> 204
        var custDeleteReply = await customerClient.DeleteAsync($"/api/v1/reviews/{reviewId}/reply");
        custDeleteReply.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staffDeleteReply = await staffClient.DeleteAsync($"/api/v1/reviews/{reviewId}/reply");
        staffDeleteReply.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Staff admin delete review: Customer -> 403; Staff -> 204
        var custAdminDel = await customerClient.DeleteAsync($"/api/v1/reviews/admin/{reviewId}");
        custAdminDel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var staffAdminDel = await staffClient.DeleteAsync($"/api/v1/reviews/admin/{reviewId}");
        staffAdminDel.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // 3. Completed COD order -> create review 201; order not completed -> 409
    [Fact]
    public async Task CreateReview_WithCodOrder_RequiresCompletedStatus()
    {
        var (customerClient, customer) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();

        // Seed order in Confirmed status (not completed)
        var confirmedOrder = await SeedOrderAsync(customer, product, PaymentMethod.COD, OrderStatus.Confirmed);

        var uncompletedReq = new CreateReviewRequest(confirmedOrder.Id, product.Id, 5, "Nice", "Product");
        var failRes = await customerClient.PostAsJsonAsync("/api/v1/reviews", uncompletedReq);
        failRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var failBody = await failRes.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        failBody!.Code.Should().Be("Review.OrderNotCompleted");

        // Seed order in Completed status
        var completedOrder = await SeedOrderAsync(customer, product, PaymentMethod.COD, OrderStatus.Completed);
        var successReq = new CreateReviewRequest(completedOrder.Id, product.Id, 5, "Nice", "Product");
        var successRes = await customerClient.PostAsJsonAsync("/api/v1/reviews", successReq);
        successRes.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // 4. Completed Stripe order also can review
    [Fact]
    public async Task CreateReview_WithCompletedStripeOrder_Succeeds()
    {
        var (customerClient, customer) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();

        var stripeOrder = await SeedOrderAsync(customer, product, PaymentMethod.Stripe, OrderStatus.Completed);

        var req = new CreateReviewRequest(stripeOrder.Id, product.Id, 5, "Stripe Review", "Smooth checkout");
        var res = await customerClient.PostAsJsonAsync("/api/v1/reviews", req);

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        body!.Rating.Should().Be(5);
        body.Title.Should().Be("Stripe Review");
    }

    // 5. Same Order/Product concurrent create -> exactly one 201, one 409, exactly one row
    [Fact]
    public async Task CreateReview_ConcurrentRequestsSameOrderProduct_ResultsInOneSuccessAndOneConflict()
    {
        var (customerClient, customer) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(customer, product);

        var req = new CreateReviewRequest(order.Id, product.Id, 5, "Concurrent", "Test");

        var task1 = customerClient.PostAsJsonAsync("/api/v1/reviews", req);
        var task2 = customerClient.PostAsJsonAsync("/api/v1/reviews", req);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Created);
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Conflict);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reviewCount = await db.Reviews.CountAsync(r => r.OrderId == order.Id && r.ProductId == product.Id);
        reviewCount.Should().Be(1);
    }

    // 6. Two distinct Completed Orders containing same Product -> two valid reviews
    [Fact]
    public async Task CreateReview_TwoDistinctOrdersSameProduct_AllowsTwoReviews()
    {
        var (customerClient, customer) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();

        var order1 = await SeedOrderAsync(customer, product);
        var order2 = await SeedOrderAsync(customer, product);

        var res1 = await customerClient.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(order1.Id, product.Id, 4, "Order 1", "Review 1"));
        var res2 = await customerClient.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(order2.Id, product.Id, 5, "Order 2", "Review 2"));

        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reviews = await db.Reviews.Where(r => r.UserId == customer.Id && r.ProductId == product.Id).ToListAsync();
        reviews.Should().HaveCount(2);
    }

    // 7. Customer A cannot edit/delete Customer B's review (returns 404)
    [Fact]
    public async Task CustomerIsolation_CustomerCannotEditOrDeleteAnotherCustomersReview()
    {
        var (clientA, userA) = await CreateUserClientAsync();
        var (clientB, _) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();

        var orderA = await SeedOrderAsync(userA, product);
        var createRes = await clientA.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(orderA.Id, product.Id, 5, "A's Review", "Content A"));
        var created = await createRes.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        var reviewId = created!.Id;

        // B attempts to update A's review
        var updateRes = await clientB.PutAsJsonAsync($"/api/v1/reviews/{reviewId}", new UpdateReviewRequest(1, "Hacked", "Hacked"));
        updateRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B attempts to delete A's review
        var deleteRes = await clientB.DeleteAsync($"/api/v1/reviews/{reviewId}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify A's review is completely intact
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var review = await db.Reviews.FindAsync(reviewId);
        review.Should().NotBeNull();
        review!.Rating.Should().Be(5);
        review.Title.Should().Be("A's Review");
    }

    // 8. Public response hides UserId/OrderId and summary/distribution is accurate
    [Fact]
    public async Task PublicReviews_NoSensitiveIdsExposed_SummaryAndDistributionAccurate()
    {
        var anonymousClient = _fixture.Factory.CreateClient();
        var (clientA, userA) = await CreateUserClientAsync();
        var (clientB, userB) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();

        var orderA = await SeedOrderAsync(userA, product);
        var orderB = await SeedOrderAsync(userB, product);

        // Review A: 5 stars
        await clientA.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(orderA.Id, product.Id, 5, "Five star", "Content"));
        // Review B: 4 stars
        var createBRes = await clientB.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(orderB.Id, product.Id, 4, "Four star", "Content"));
        var reviewB = await createBRes.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        var reviewBId = reviewB!.Id;

        // Query public endpoint
        var rawResponse = await anonymousClient.GetAsync($"/api/v1/products/{product.Id}/reviews");
        rawResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await rawResponse.Content.ReadAsStringAsync();

        // Must not contain "userId" or "orderId" in json string
        rawJson.Should().NotContain("\"userId\"");
        rawJson.Should().NotContain("\"orderId\"");

        var parsed = await rawResponse.Content.ReadFromJsonAsync<ProductReviewsResultDto>(JsonOptions);
        parsed!.TotalReviews.Should().Be(2);
        parsed.AverageRating.Should().Be(4.50m);
        parsed.RatingDistribution.FiveStar.Should().Be(1);
        parsed.RatingDistribution.FourStar.Should().Be(1);
        parsed.RatingDistribution.ThreeStar.Should().Be(0);

        // Update Review B from 4 to 2 stars
        await clientB.PutAsJsonAsync($"/api/v1/reviews/{reviewBId}", new UpdateReviewRequest(2, "Two star", "Updated"));

        var updatedResponse = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        updatedResponse!.TotalReviews.Should().Be(2);
        updatedResponse.AverageRating.Should().Be(3.50m);
        updatedResponse.RatingDistribution.FourStar.Should().Be(0);
        updatedResponse.RatingDistribution.TwoStar.Should().Be(1);

        // Delete Review B
        await clientB.DeleteAsync($"/api/v1/reviews/{reviewBId}");

        var deletedResponse = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        deletedResponse!.TotalReviews.Should().Be(1);
        deletedResponse.AverageRating.Should().Be(5.00m);
        deletedResponse.RatingDistribution.TwoStar.Should().Be(0);
        deletedResponse.RatingDistribution.FiveStar.Should().Be(1);
    }

    // 9. Staff reply lifecycle and admin deletion update public result correctly
    [Fact]
    public async Task StaffReplyLifecycle_AndAdminDeletion_UpdatesPublicResult()
    {
        var anonymousClient = _fixture.Factory.CreateClient();
        var (custClient, user) = await CreateUserClientAsync();
        var (staffClient, _) = await CreateUserClientAsync(UserRole.Staff);
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(user, product);

        var createRes = await custClient.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(order.Id, product.Id, 5, "Product feedback", "Love it"));
        var review = await createRes.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        var reviewId = review!.Id;

        // 1. Staff adds reply
        var addReplyRes = await staffClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new SetReviewReplyRequest("Thank you!"));
        addReplyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicWithReply = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        publicWithReply!.Reviews.Items[0].StaffReply.Should().Be("Thank you!");
        publicWithReply.Reviews.Items[0].StaffReplyAt.Should().NotBeNull();

        // 2. Staff updates reply
        var updateReplyRes = await staffClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new SetReviewReplyRequest("Updated reply from team"));
        updateReplyRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicWithUpdatedReply = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        publicWithUpdatedReply!.Reviews.Items[0].StaffReply.Should().Be("Updated reply from team");

        // 3. Staff deletes reply
        var deleteReplyRes = await staffClient.DeleteAsync($"/api/v1/reviews/{reviewId}/reply");
        deleteReplyRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var publicClearedReply = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        publicClearedReply!.Reviews.Items[0].StaffReply.Should().BeNull();
        publicClearedReply.Reviews.Items[0].StaffReplyAt.Should().BeNull();

        // 4. Staff admin deletes review
        var adminDeleteRes = await staffClient.DeleteAsync($"/api/v1/reviews/admin/{reviewId}");
        adminDeleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var publicAfterAdminDelete = await anonymousClient.GetFromJsonAsync<ProductReviewsResultDto>($"/api/v1/products/{product.Id}/reviews", JsonOptions);
        publicAfterAdminDelete!.TotalReviews.Should().Be(0);
        publicAfterAdminDelete.Reviews.Items.Should().BeEmpty();
    }

    // 10. Concurrency conflict: PostgreSQL concurrency token detects concurrent updates
    [Fact]
    public async Task Concurrency_WhenConcurrentModificationsInPostgreSql_ThrowsDbUpdateConcurrencyException()
    {
        var (_, user) = await CreateUserClientAsync();
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(user, product);

        Guid reviewId;
        using (var setupScope = _fixture.Factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var initialReview = new Review(product.Id, user.Id, order.Id, 4, "Initial Title", "Initial Content");
            setupDb.Reviews.Add(initialReview);
            await setupDb.SaveChangesAsync();
            reviewId = initialReview.Id;
        }

        // Two independent scopes loading the exact same review at the same version
        using var scope1 = _fixture.Factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        using var scope2 = _fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var review1 = await db1.Reviews.FindAsync(reviewId);
        var review2 = await db2.Reviews.FindAsync(reviewId);

        review1.Should().NotBeNull();
        review2.Should().NotBeNull();
        review1!.UpdatedAt.Should().Be(review2!.UpdatedAt);

        // Mutate both in memory
        review1.UpdateReview(5, "Updated by customer", "New content");
        review2.SetStaffReply("Updated by staff");

        // Save first context - succeeds and updates UpdatedAt concurrency token in PostgreSQL
        await db1.SaveChangesAsync();

        // Save second context - must throw DbUpdateConcurrencyException because UpdatedAt in DB changed
        var save2Act = async () => await db2.SaveChangesAsync();
        await save2Act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Entity in database remains valid from the first commit and not partially mixed
        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalDbReview = await verifyDb.Reviews.FindAsync(reviewId);
        finalDbReview.Should().NotBeNull();
        finalDbReview!.Rating.Should().Be(5);
        finalDbReview.Title.Should().Be("Updated by customer");
        finalDbReview.StaffReply.Should().BeNull(); // Staff reply was not committed
    }

    [Fact]
    public async Task ConcurrencyConflict_WhenConcurrentCustomerUpdateAndStaffReply_ThrowsConflict()
    {
        var (custClient, user) = await CreateUserClientAsync();
        var (staffClient, _) = await CreateUserClientAsync(UserRole.Staff);
        var (_, product) = await SeedProductAsync();
        var order = await SeedOrderAsync(user, product);

        var createRes = await custClient.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(order.Id, product.Id, 4, "Initial Title", "Initial Content"));
        var review = await createRes.Content.ReadFromJsonAsync<MyReviewDto>(JsonOptions);
        var reviewId = review!.Id;

        // Use TaskCompletionSource to release all concurrent requests simultaneously
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new List<Task<HttpResponseMessage>>();

        for (var i = 0; i < 10; i++)
        {
            var idx = i;
            var rating = (i % 5) + 1;
            tasks.Add(Task.Run(async () =>
            {
                await tcs.Task;
                return await custClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}", new UpdateReviewRequest(rating, $"Update {idx}", $"Content {idx}"));
            }));
            tasks.Add(Task.Run(async () =>
            {
                await tcs.Task;
                return await staffClient.PutAsJsonAsync($"/api/v1/reviews/{reviewId}/reply", new SetReviewReplyRequest($"Staff reply {idx}"));
            }));
        }

        // Fire all requests at once
        tcs.SetResult();
        var responses = await Task.WhenAll(tasks);

        var okResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        // Must have at least one OK (200)
        okResponses.Should().NotBeEmpty("at least one concurrent request must succeed");

        // Must have at least one Conflict (409)
        conflictResponses.Should().NotBeEmpty("at least one concurrent request must conflict");

        // All responses must be either 200 or 409 (no 500 internal server errors)
        var validStatuses = new[] { HttpStatusCode.OK, HttpStatusCode.Conflict };
        responses.All(r => validStatuses.Contains(r.StatusCode)).Should().BeTrue("no 500 internal server errors allowed");

        // All 409 Conflict responses must return Review.ConcurrencyConflict error code
        foreach (var conflictRes in conflictResponses)
        {
            var err = await conflictRes.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
            err.Should().NotBeNull();
            err!.Code.Should().Be("Review.ConcurrencyConflict");
        }

        // The entity in the database remains consistent, valid and non-corrupted
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finalReview = await db.Reviews.FindAsync(reviewId);
        finalReview.Should().NotBeNull();
        finalReview!.Rating.Should().BeInRange(1, 5);
        finalReview.UpdatedAt.Should().NotBeNull();
    }
}

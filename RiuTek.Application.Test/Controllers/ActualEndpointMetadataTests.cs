using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Controllers;
using RiuTek.Core.Constants;

namespace RiuTek.Application.Test.Controllers;

public class ActualEndpointMetadataTests
{
    [Fact]
    public async Task EndpointDataSource_ContainsAll27Endpoints_WithCorrectRouteAndAuthorizationMetadata()
    {
        // Arrange: Build a lightweight WebApplication with in-memory test host & API controllers
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0"); // Dynamic ephemeral port

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ApiControllerBase).Assembly);
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.MapControllers();

        await app.StartAsync();

        try
        {
            var endpointDataSources = app.Services.GetServices<EndpointDataSource>();
            var endpoints = endpointDataSources.SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>().ToList();

            endpoints.Should().NotBeEmpty();

            // Total controller actions: 6 Posts + 6 Comments + 7 Products + 5 Categories + 5 Carts + 4 Auth + 8 Orders + 1 Payments + 6 Users + 3 ProductMedia + 3 ProductImports + 9 Reviews = 63
            var controllerEndpoints = endpoints.Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() != null).ToList();
            controllerEndpoints.Should().HaveCount(63);

            // Helper to get action descriptor
            ControllerActionDescriptor GetDescriptor(RouteEndpoint e) => e.Metadata.GetMetadata<ControllerActionDescriptor>()!;

            #region Posts Endpoints (6)

            var postEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(PostsController))
                .ToList();

            postEndpoints.Should().HaveCount(6);

            // 1. GET api/v1/posts (AllowAnonymous) - Action: GetPosts
            var getPosts = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetPosts) && GetHttpMethods(e).Contains("GET"));
            getPosts.Should().NotBeNull();
            GetFullRoutePattern(getPosts!).Should().BeEquivalentTo("api/v1/posts");
            AssertPublicEndpoint(getPosts!);

            // 2. GET api/v1/posts/slug/{slug} (AllowAnonymous) - Action: GetBySlug
            var getBySlug = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetBySlug) && GetHttpMethods(e).Contains("GET"));
            getBySlug.Should().NotBeNull();
            GetFullRoutePattern(getBySlug!).Should().BeEquivalentTo("api/v1/posts/slug/{slug}");
            AssertPublicEndpoint(getBySlug!);

            // 3. GET api/v1/posts/{id:guid} (ContentManager policy) - Action: GetById
            var getPostById = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetById) && GetHttpMethods(e).Contains("GET"));
            getPostById.Should().NotBeNull();
            GetFullRoutePattern(getPostById!).Should().BeEquivalentTo("api/v1/posts/{id:guid}");
            AssertAuthorizePolicy(getPostById!, Policies.ContentManager);

            // 4. POST api/v1/posts (ContentManager policy) - Action: Create
            var createPost = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.Create) && GetHttpMethods(e).Contains("POST"));
            createPost.Should().NotBeNull();
            GetFullRoutePattern(createPost!).Should().BeEquivalentTo("api/v1/posts");
            AssertAuthorizePolicy(createPost!, Policies.ContentManager);

            // 5. PUT api/v1/posts/{id:guid} (ContentManager policy) - Action: Update
            var updatePost = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.Update) && GetHttpMethods(e).Contains("PUT"));
            updatePost.Should().NotBeNull();
            GetFullRoutePattern(updatePost!).Should().BeEquivalentTo("api/v1/posts/{id:guid}");
            AssertAuthorizePolicy(updatePost!, Policies.ContentManager);

            // 6. DELETE api/v1/posts/{id:guid} (ContentManager policy) - Action: Delete
            var deletePost = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.Delete) && GetHttpMethods(e).Contains("DELETE"));
            deletePost.Should().NotBeNull();
            GetFullRoutePattern(deletePost!).Should().BeEquivalentTo("api/v1/posts/{id:guid}");
            AssertAuthorizePolicy(deletePost!, Policies.ContentManager);

            #endregion

            #region Comments Endpoints (6)

            var commentEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(CommentsController))
                .ToList();

            commentEndpoints.Should().HaveCount(6);

            // 7. GET api/v1/comments/posts/{postId:guid} (AllowAnonymous) - Action: GetPostComments
            var getPostComments = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.GetPostComments) && GetHttpMethods(e).Contains("GET"));
            getPostComments.Should().NotBeNull();
            GetFullRoutePattern(getPostComments!).Should().BeEquivalentTo("api/v1/comments/posts/{postId:guid}");
            AssertPublicEndpoint(getPostComments!);

            // 8. POST api/v1/comments/posts/{postId:guid} (Authorize) - Action: CreatePostComment
            var createPostComment = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.CreatePostComment) && GetHttpMethods(e).Contains("POST"));
            createPostComment.Should().NotBeNull();
            GetFullRoutePattern(createPostComment!).Should().BeEquivalentTo("api/v1/comments/posts/{postId:guid}");
            AssertAuthorized(createPostComment!);

            // 9. DELETE api/v1/comments/posts/{commentId:guid} (Authorize) - Action: DeletePostComment
            var deletePostComment = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.DeletePostComment) && GetHttpMethods(e).Contains("DELETE"));
            deletePostComment.Should().NotBeNull();
            GetFullRoutePattern(deletePostComment!).Should().BeEquivalentTo("api/v1/comments/posts/{commentId:guid}");
            AssertAuthorized(deletePostComment!);

            // 10. GET api/v1/comments/products/{productId:guid} (AllowAnonymous) - Action: GetProductComments
            var getProdComments = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.GetProductComments) && GetHttpMethods(e).Contains("GET"));
            getProdComments.Should().NotBeNull();
            GetFullRoutePattern(getProdComments!).Should().BeEquivalentTo("api/v1/comments/products/{productId:guid}");
            AssertPublicEndpoint(getProdComments!);

            // 11. POST api/v1/comments/products/{productId:guid} (Authorize) - Action: CreateProductComment
            var createProdComment = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.CreateProductComment) && GetHttpMethods(e).Contains("POST"));
            createProdComment.Should().NotBeNull();
            GetFullRoutePattern(createProdComment!).Should().BeEquivalentTo("api/v1/comments/products/{productId:guid}");
            AssertAuthorized(createProdComment!);

            // 12. DELETE api/v1/comments/products/{commentId:guid} (Authorize) - Action: DeleteProductComment
            var deleteProdComment = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.DeleteProductComment) && GetHttpMethods(e).Contains("DELETE"));
            deleteProdComment.Should().NotBeNull();
            GetFullRoutePattern(deleteProdComment!).Should().BeEquivalentTo("api/v1/comments/products/{commentId:guid}");
            AssertAuthorized(deleteProdComment!);

            #endregion

            #region Products Endpoints (7)

            var productEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ProductsController))
                .ToList();

            productEndpoints.Should().HaveCount(7);

            // 13. GET api/v1/products (AllowAnonymous) - Action: GetProducts
            var getProducts = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.GetProducts) && GetHttpMethods(e).Contains("GET"));
            getProducts.Should().NotBeNull();
            GetFullRoutePattern(getProducts!).Should().BeEquivalentTo("api/v1/products");
            AssertPublicEndpoint(getProducts!);

            // 14. GET api/v1/products/slug/{slug} (AllowAnonymous) - Action: GetBySlug
            var getProductBySlug = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.GetBySlug) && GetHttpMethods(e).Contains("GET"));
            getProductBySlug.Should().NotBeNull();
            GetFullRoutePattern(getProductBySlug!).Should().BeEquivalentTo("api/v1/products/slug/{slug}");
            AssertPublicEndpoint(getProductBySlug!);

            // 15. GET api/v1/products/{id:guid} (ContentManager policy) - Action: GetById
            var getProductById = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.GetById) && GetHttpMethods(e).Contains("GET"));
            getProductById.Should().NotBeNull();
            GetFullRoutePattern(getProductById!).Should().BeEquivalentTo("api/v1/products/{id:guid}");
            AssertAuthorizePolicy(getProductById!, Policies.ContentManager);

            // 16. POST api/v1/products (ContentManager policy) - Action: Create
            var createProduct = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.Create) && GetHttpMethods(e).Contains("POST"));
            createProduct.Should().NotBeNull();
            GetFullRoutePattern(createProduct!).Should().BeEquivalentTo("api/v1/products");
            AssertAuthorizePolicy(createProduct!, Policies.ContentManager);

            // 17. PUT api/v1/products/{id:guid} (ContentManager policy) - Action: Update
            var updateProduct = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.Update) && GetHttpMethods(e).Contains("PUT"));
            updateProduct.Should().NotBeNull();
            GetFullRoutePattern(updateProduct!).Should().BeEquivalentTo("api/v1/products/{id:guid}");
            AssertAuthorizePolicy(updateProduct!, Policies.ContentManager);

            // 18. POST api/v1/products/{id:guid}/stock-adjustments (ContentManager policy) - Action: AdjustStock
            var adjustStock = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.AdjustStock) && GetHttpMethods(e).Contains("POST"));
            adjustStock.Should().NotBeNull();
            GetFullRoutePattern(adjustStock!).Should().BeEquivalentTo("api/v1/products/{id:guid}/stock-adjustments");
            AssertAuthorizePolicy(adjustStock!, Policies.ContentManager);

            // 19. GET api/v1/products/{id:guid}/stock-movements (ContentManager policy) - Action: GetStockMovements
            var getStockMovements = productEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductsController.GetStockMovements) && GetHttpMethods(e).Contains("GET"));
            getStockMovements.Should().NotBeNull();
            GetFullRoutePattern(getStockMovements!).Should().BeEquivalentTo("api/v1/products/{id:guid}/stock-movements");
            AssertAuthorizePolicy(getStockMovements!, Policies.ContentManager);

            #endregion

            #region Categories Endpoints (5)

            var categoryEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(CategoriesController))
                .ToList();

            categoryEndpoints.Should().HaveCount(5);

            // 18. GET api/v1/categories (AllowAnonymous) - Action: GetTree
            var getCategoryTree = categoryEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CategoriesController.GetTree) && GetHttpMethods(e).Contains("GET"));
            getCategoryTree.Should().NotBeNull();
            GetFullRoutePattern(getCategoryTree!).Should().BeEquivalentTo("api/v1/categories");
            AssertPublicEndpoint(getCategoryTree!);

            // 19. GET api/v1/categories/{id:guid} (AllowAnonymous) - Action: GetById
            var getCategoryById = categoryEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CategoriesController.GetById) && GetHttpMethods(e).Contains("GET"));
            getCategoryById.Should().NotBeNull();
            GetFullRoutePattern(getCategoryById!).Should().BeEquivalentTo("api/v1/categories/{id:guid}");
            AssertPublicEndpoint(getCategoryById!);

            // 20. POST api/v1/categories (ContentManager policy) - Action: Create
            var createCategory = categoryEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CategoriesController.Create) && GetHttpMethods(e).Contains("POST"));
            createCategory.Should().NotBeNull();
            GetFullRoutePattern(createCategory!).Should().BeEquivalentTo("api/v1/categories");
            AssertAuthorizePolicy(createCategory!, Policies.ContentManager);

            // 21. PUT api/v1/categories/{id:guid} (ContentManager policy) - Action: Update
            var updateCategory = categoryEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CategoriesController.Update) && GetHttpMethods(e).Contains("PUT"));
            updateCategory.Should().NotBeNull();
            GetFullRoutePattern(updateCategory!).Should().BeEquivalentTo("api/v1/categories/{id:guid}");
            AssertAuthorizePolicy(updateCategory!, Policies.ContentManager);

            // 22. DELETE api/v1/categories/{id:guid} (ContentManager policy) - Action: Delete
            var deleteCategory = categoryEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CategoriesController.Delete) && GetHttpMethods(e).Contains("DELETE"));
            deleteCategory.Should().NotBeNull();
            GetFullRoutePattern(deleteCategory!).Should().BeEquivalentTo("api/v1/categories/{id:guid}");
            AssertAuthorizePolicy(deleteCategory!, Policies.ContentManager);

            #endregion

            #region Carts Endpoints (5)

            var cartEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(CartsController))
                .ToList();

            cartEndpoints.Should().HaveCount(5);

            // 23. GET api/v1/carts (Authorize) - Action: GetCart
            var getCart = cartEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CartsController.GetCart) && GetHttpMethods(e).Contains("GET"));
            getCart.Should().NotBeNull();
            GetFullRoutePattern(getCart!).Should().BeEquivalentTo("api/v1/carts");
            AssertAuthorized(getCart!);

            // 24. POST api/v1/carts/items (Authorize) - Action: AddItem
            var addItem = cartEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CartsController.AddItem) && GetHttpMethods(e).Contains("POST"));
            addItem.Should().NotBeNull();
            GetFullRoutePattern(addItem!).Should().BeEquivalentTo("api/v1/carts/items");
            AssertAuthorized(addItem!);

            // 25. PUT api/v1/carts/items/{productId:guid} (Authorize) - Action: SetItemQuantity
            var setItemQuantity = cartEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CartsController.SetItemQuantity) && GetHttpMethods(e).Contains("PUT"));
            setItemQuantity.Should().NotBeNull();
            GetFullRoutePattern(setItemQuantity!).Should().BeEquivalentTo("api/v1/carts/items/{productId:guid}");
            AssertAuthorized(setItemQuantity!);

            // 26. DELETE api/v1/carts/items/{productId:guid} (Authorize) - Action: RemoveItem
            var removeItem = cartEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CartsController.RemoveItem) && GetHttpMethods(e).Contains("DELETE"));
            removeItem.Should().NotBeNull();
            GetFullRoutePattern(removeItem!).Should().BeEquivalentTo("api/v1/carts/items/{productId:guid}");
            AssertAuthorized(removeItem!);

            // 27. DELETE api/v1/carts/items (Authorize) - Action: ClearCart
            var clearCart = cartEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CartsController.ClearCart) && GetHttpMethods(e).Contains("DELETE"));
            clearCart.Should().NotBeNull();
            GetFullRoutePattern(clearCart!).Should().BeEquivalentTo("api/v1/carts/items");
            AssertAuthorized(clearCart!);

            #endregion

            #region Auth Endpoints (4)

            var authEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(AuthController))
                .ToList();

            authEndpoints.Should().HaveCount(4);

            // 28. POST api/v1/auth/register (AllowAnonymous) - Action: Register
            var registerEndpoint = authEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(AuthController.Register) && GetHttpMethods(e).Contains("POST"));
            registerEndpoint.Should().NotBeNull();
            GetFullRoutePattern(registerEndpoint!).Should().BeEquivalentTo("api/v1/auth/register");
            AssertPublicEndpoint(registerEndpoint!);

            // 29. POST api/v1/auth/login (AllowAnonymous) - Action: Login
            var loginEndpoint = authEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(AuthController.Login) && GetHttpMethods(e).Contains("POST"));
            loginEndpoint.Should().NotBeNull();
            GetFullRoutePattern(loginEndpoint!).Should().BeEquivalentTo("api/v1/auth/login");
            AssertPublicEndpoint(loginEndpoint!);

            // 30. POST api/v1/auth/refresh (AllowAnonymous) - Action: Refresh
            var refreshEndpoint = authEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(AuthController.Refresh) && GetHttpMethods(e).Contains("POST"));
            refreshEndpoint.Should().NotBeNull();
            GetFullRoutePattern(refreshEndpoint!).Should().BeEquivalentTo("api/v1/auth/refresh");
            AssertPublicEndpoint(refreshEndpoint!);

            // 31. POST api/v1/auth/logout (AllowAnonymous) - Action: Logout
            var logoutEndpoint = authEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(AuthController.Logout) && GetHttpMethods(e).Contains("POST"));
            logoutEndpoint.Should().NotBeNull();
            GetFullRoutePattern(logoutEndpoint!).Should().BeEquivalentTo("api/v1/auth/logout");
            AssertPublicEndpoint(logoutEndpoint!);

            #endregion

            #region Orders Endpoints (8)

            var orderEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(OrdersController))
                .ToList();

            orderEndpoints.Should().HaveCount(8);

            var getManagedOrdersEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.GetManagedOrders) && GetHttpMethods(e).Contains("GET"));
            getManagedOrdersEndpoint.Should().NotBeNull();
            GetFullRoutePattern(getManagedOrdersEndpoint!).Should().BeEquivalentTo("api/v1/orders/admin");
            AssertAuthorizePolicy(getManagedOrdersEndpoint!, Policies.ContentManager);

            var getManagedOrderByIdEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.GetManagedOrderById) && GetHttpMethods(e).Contains("GET"));
            getManagedOrderByIdEndpoint.Should().NotBeNull();
            GetFullRoutePattern(getManagedOrderByIdEndpoint!).Should().BeEquivalentTo("api/v1/orders/admin/{id:guid}");
            AssertAuthorizePolicy(getManagedOrderByIdEndpoint!, Policies.ContentManager);

            var updateManagedOrderStatusEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.UpdateManagedOrderStatus) && GetHttpMethods(e).Contains("PATCH"));
            updateManagedOrderStatusEndpoint.Should().NotBeNull();
            GetFullRoutePattern(updateManagedOrderStatusEndpoint!).Should().BeEquivalentTo("api/v1/orders/admin/{id:guid}/status");
            AssertAuthorizePolicy(updateManagedOrderStatusEndpoint!, Policies.ContentManager);

            // 32. GET api/v1/orders ([Authorize]) - Action: GetMyOrders
            var getMyOrdersEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.GetMyOrders) && GetHttpMethods(e).Contains("GET"));
            getMyOrdersEndpoint.Should().NotBeNull();
            GetFullRoutePattern(getMyOrdersEndpoint!).Should().BeEquivalentTo("api/v1/orders");
            AssertAuthorized(getMyOrdersEndpoint!);

            // 33. GET api/v1/orders/{id:guid} ([Authorize]) - Action: GetMyOrderById
            var getMyOrderByIdEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.GetMyOrderById) && GetHttpMethods(e).Contains("GET"));
            getMyOrderByIdEndpoint.Should().NotBeNull();
            GetFullRoutePattern(getMyOrderByIdEndpoint!).Should().BeEquivalentTo("api/v1/orders/{id:guid}");
            AssertAuthorized(getMyOrderByIdEndpoint!);

            var cancelMyOrderEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.CancelMyOrder) && GetHttpMethods(e).Contains("POST"));
            cancelMyOrderEndpoint.Should().NotBeNull();
            GetFullRoutePattern(cancelMyOrderEndpoint!).Should().BeEquivalentTo("api/v1/orders/{id:guid}/cancel");
            AssertAuthorized(cancelMyOrderEndpoint!);

            // 34. POST api/v1/orders/checkout/quote ([Authorize]) - Action: GetCheckoutQuote
            var quoteEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.GetCheckoutQuote) && GetHttpMethods(e).Contains("POST"));
            quoteEndpoint.Should().NotBeNull();
            GetFullRoutePattern(quoteEndpoint!).Should().BeEquivalentTo("api/v1/orders/checkout/quote");
            AssertAuthorized(quoteEndpoint!);

            // 35. POST api/v1/orders/checkout ([Authorize]) - Action: Checkout
            var checkoutEndpoint = orderEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(OrdersController.Checkout) && GetHttpMethods(e).Contains("POST"));
            checkoutEndpoint.Should().NotBeNull();
            GetFullRoutePattern(checkoutEndpoint!).Should().BeEquivalentTo("api/v1/orders/checkout");
            AssertAuthorized(checkoutEndpoint!);

            #endregion

            #region Payments Endpoints (1)

            var paymentEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(PaymentsController))
                .ToList();

            paymentEndpoints.Should().HaveCount(1);

            // 36. POST api/v1/payments/stripe/webhook (AllowAnonymous) - Action: StripeWebhook
            var webhookEndpoint = paymentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PaymentsController.StripeWebhook) && GetHttpMethods(e).Contains("POST"));
            webhookEndpoint.Should().NotBeNull();
            GetFullRoutePattern(webhookEndpoint!).Should().BeEquivalentTo("api/v1/payments/stripe/webhook");
            AssertPublicEndpoint(webhookEndpoint!);

            #endregion

            #region Users Endpoints (6)

            var userEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(UsersController))
                .ToList();

            userEndpoints.Should().HaveCount(6);

            // 37. PUT api/v1/users/me (Authorize) - Action: UpdateProfile
            var updateProfile = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.UpdateProfile) && GetHttpMethods(e).Contains("PUT"));
            updateProfile.Should().NotBeNull();
            GetFullRoutePattern(updateProfile!).Should().BeEquivalentTo("api/v1/users/me");
            AssertAuthorized(updateProfile!);

            // 38. GET api/v1/users/me/addresses (Authorize) - Action: GetMyAddresses
            var getMyAddresses = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.GetMyAddresses) && GetHttpMethods(e).Contains("GET"));
            getMyAddresses.Should().NotBeNull();
            GetFullRoutePattern(getMyAddresses!).Should().BeEquivalentTo("api/v1/users/me/addresses");
            AssertAuthorized(getMyAddresses!);

            // 39. POST api/v1/users/me/addresses (Authorize) - Action: AddAddress
            var addAddress = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.AddAddress) && GetHttpMethods(e).Contains("POST"));
            addAddress.Should().NotBeNull();
            GetFullRoutePattern(addAddress!).Should().BeEquivalentTo("api/v1/users/me/addresses");
            AssertAuthorized(addAddress!);

            // 40. PUT api/v1/users/me/addresses/{addressId:guid} (Authorize) - Action: UpdateAddress
            var updateAddress = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.UpdateAddress) && GetHttpMethods(e).Contains("PUT"));
            updateAddress.Should().NotBeNull();
            GetFullRoutePattern(updateAddress!).Should().BeEquivalentTo("api/v1/users/me/addresses/{addressId:guid}");
            AssertAuthorized(updateAddress!);

            // 41. PATCH api/v1/users/me/addresses/{addressId:guid}/default (Authorize) - Action: SetDefaultAddress
            var setDefaultAddress = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.SetDefaultAddress) && GetHttpMethods(e).Contains("PATCH"));
            setDefaultAddress.Should().NotBeNull();
            GetFullRoutePattern(setDefaultAddress!).Should().BeEquivalentTo("api/v1/users/me/addresses/{addressId:guid}/default");
            AssertAuthorized(setDefaultAddress!);

            // 42. DELETE api/v1/users/me/addresses/{addressId:guid} (Authorize) - Action: DeleteAddress
            var deleteAddress = userEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(UsersController.DeleteAddress) && GetHttpMethods(e).Contains("DELETE"));
            deleteAddress.Should().NotBeNull();
            GetFullRoutePattern(deleteAddress!).Should().BeEquivalentTo("api/v1/users/me/addresses/{addressId:guid}");
            AssertAuthorized(deleteAddress!);

            #endregion

            #region ProductMedia Endpoints (3)

            var mediaEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ProductMediaController))
                .ToList();

            mediaEndpoints.Should().HaveCount(3);

            // 43. POST api/v1/product-media/upload-requests (Policies.ContentManager) - Action: CreateUploadRequest
            var createUploadRequest = mediaEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductMediaController.CreateUploadRequest) && GetHttpMethods(e).Contains("POST"));
            createUploadRequest.Should().NotBeNull();
            GetFullRoutePattern(createUploadRequest!).Should().BeEquivalentTo("api/v1/product-media/upload-requests");
            AssertAuthorizePolicy(createUploadRequest!, Policies.ContentManager);

            // 44. POST api/v1/product-media/confirmations (Policies.ContentManager) - Action: ConfirmUpload
            var confirmUpload = mediaEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductMediaController.ConfirmUpload) && GetHttpMethods(e).Contains("POST"));
            confirmUpload.Should().NotBeNull();
            GetFullRoutePattern(confirmUpload!).Should().BeEquivalentTo("api/v1/product-media/confirmations");
            AssertAuthorizePolicy(confirmUpload!, Policies.ContentManager);

            // 45. DELETE api/v1/product-media (Policies.ContentManager) - Action: DeleteMedia
            var deleteMedia = mediaEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductMediaController.DeleteMedia) && GetHttpMethods(e).Contains("DELETE"));
            deleteMedia.Should().NotBeNull();
            GetFullRoutePattern(deleteMedia!).Should().BeEquivalentTo("api/v1/product-media");
            AssertAuthorizePolicy(deleteMedia!, Policies.ContentManager);

            #endregion

            #region ProductImports Endpoints (3)

            var importEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ProductImportsController))
                .ToList();

            importEndpoints.Should().HaveCount(3);

            // 46. GET api/v1/product-imports/template (Policies.ContentManager) - Action: DownloadTemplate
            var downloadTemplate = importEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductImportsController.DownloadTemplate) && GetHttpMethods(e).Contains("GET"));
            downloadTemplate.Should().NotBeNull();
            GetFullRoutePattern(downloadTemplate!).Should().BeEquivalentTo("api/v1/product-imports/template");
            AssertAuthorizePolicy(downloadTemplate!, Policies.ContentManager);

            // 47. POST api/v1/product-imports/validate (Policies.ContentManager) - Action: ValidateImport
            var validateImport = importEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductImportsController.ValidateImport) && GetHttpMethods(e).Contains("POST"));
            validateImport.Should().NotBeNull();
            GetFullRoutePattern(validateImport!).Should().BeEquivalentTo("api/v1/product-imports/validate");
            AssertAuthorizePolicy(validateImport!, Policies.ContentManager);

            // 48. POST api/v1/product-imports/import (Policies.ContentManager) - Action: Import
            var commitImport = importEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ProductImportsController.Import) && GetHttpMethods(e).Contains("POST"));
            commitImport.Should().NotBeNull();
            GetFullRoutePattern(commitImport!).Should().BeEquivalentTo("api/v1/product-imports/import");
            AssertAuthorizePolicy(commitImport!, Policies.ContentManager);

            #endregion

            #region Reviews Endpoints (9)

            var reviewEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ReviewsController))
                .ToList();

            reviewEndpoints.Should().HaveCount(9);

            // 1. GET /api/v1/products/{productId:guid}/reviews (AllowAnonymous) - Action: GetProductReviews
            var getProductReviews = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.GetProductReviews) && GetHttpMethods(e).Contains("GET"));
            getProductReviews.Should().NotBeNull();
            GetFullRoutePattern(getProductReviews!).Should().BeEquivalentTo("api/v1/products/{productId:guid}/reviews");
            AssertPublicEndpoint(getProductReviews!);

            // 2. GET api/v1/reviews/mine (Authorized) - Action: GetMyReviews
            var getMyReviews = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.GetMyReviews) && GetHttpMethods(e).Contains("GET"));
            getMyReviews.Should().NotBeNull();
            GetFullRoutePattern(getMyReviews!).Should().BeEquivalentTo("api/v1/reviews/mine");
            AssertAuthorized(getMyReviews!);

            // 3. POST api/v1/reviews (Authorized) - Action: CreateReview
            var createReview = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.CreateReview) && GetHttpMethods(e).Contains("POST"));
            createReview.Should().NotBeNull();
            GetFullRoutePattern(createReview!).Should().BeEquivalentTo("api/v1/reviews");
            AssertAuthorized(createReview!);

            // 4. PUT api/v1/reviews/{id:guid} (Authorized) - Action: UpdateReview
            var updateReview = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.UpdateReview) && GetHttpMethods(e).Contains("PUT"));
            updateReview.Should().NotBeNull();
            GetFullRoutePattern(updateReview!).Should().BeEquivalentTo("api/v1/reviews/{id:guid}");
            AssertAuthorized(updateReview!);

            // 5. DELETE api/v1/reviews/{id:guid} (Authorized) - Action: DeleteReview
            var deleteReview = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.DeleteReview) && GetHttpMethods(e).Contains("DELETE"));
            deleteReview.Should().NotBeNull();
            GetFullRoutePattern(deleteReview!).Should().BeEquivalentTo("api/v1/reviews/{id:guid}");
            AssertAuthorized(deleteReview!);

            // 6. GET api/v1/reviews/admin (Policies.ContentManager) - Action: GetManagedReviews
            var getManagedReviews = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.GetManagedReviews) && GetHttpMethods(e).Contains("GET"));
            getManagedReviews.Should().NotBeNull();
            GetFullRoutePattern(getManagedReviews!).Should().BeEquivalentTo("api/v1/reviews/admin");
            AssertAuthorizePolicy(getManagedReviews!, Policies.ContentManager);

            // 7. PUT api/v1/reviews/{id:guid}/reply (Policies.ContentManager) - Action: SetReviewReply
            var setReviewReply = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.SetReviewReply) && GetHttpMethods(e).Contains("PUT"));
            setReviewReply.Should().NotBeNull();
            GetFullRoutePattern(setReviewReply!).Should().BeEquivalentTo("api/v1/reviews/{id:guid}/reply");
            AssertAuthorizePolicy(setReviewReply!, Policies.ContentManager);

            // 8. DELETE api/v1/reviews/{id:guid}/reply (Policies.ContentManager) - Action: DeleteReviewReply
            var deleteReviewReply = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.DeleteReviewReply) && GetHttpMethods(e).Contains("DELETE"));
            deleteReviewReply.Should().NotBeNull();
            GetFullRoutePattern(deleteReviewReply!).Should().BeEquivalentTo("api/v1/reviews/{id:guid}/reply");
            AssertAuthorizePolicy(deleteReviewReply!, Policies.ContentManager);

            // 9. DELETE api/v1/reviews/admin/{id:guid} (Policies.ContentManager) - Action: DeleteManagedReview
            var deleteManagedReview = reviewEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(ReviewsController.DeleteManagedReview) && GetHttpMethods(e).Contains("DELETE"));
            deleteManagedReview.Should().NotBeNull();
            GetFullRoutePattern(deleteManagedReview!).Should().BeEquivalentTo("api/v1/reviews/admin/{id:guid}");
            AssertAuthorizePolicy(deleteManagedReview!, Policies.ContentManager);

            #endregion
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string GetFullRoutePattern(RouteEndpoint endpoint)
    {
        var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        return descriptor?.AttributeRouteInfo?.Template ?? endpoint.RoutePattern.RawText ?? "";
    }

    private static IReadOnlyList<string> GetHttpMethods(RouteEndpoint endpoint)
    {
        var httpMethodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return httpMethodMetadata?.HttpMethods ?? Array.Empty<string>();
    }

    private static void AssertAuthorizePolicy(RouteEndpoint endpoint, string expectedPolicy)
    {
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            $"Protected endpoint '{endpoint.DisplayName}' must NOT have IAllowAnonymous");

        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        authData.Should().NotBeEmpty();
        authData.Any(a => a.Policy == expectedPolicy).Should().BeTrue(
            $"Expected policy '{expectedPolicy}' on endpoint '{endpoint.DisplayName}'");
    }

    private static void AssertAuthorized(RouteEndpoint endpoint)
    {
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
            $"Protected endpoint '{endpoint.DisplayName}' must NOT have IAllowAnonymous");

        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        authData.Should().NotBeEmpty($"Expected authorization on endpoint '{endpoint.DisplayName}'");
    }

    private static void AssertPublicEndpoint(RouteEndpoint endpoint)
    {
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull(
            $"Public endpoint '{endpoint.DisplayName}' must have IAllowAnonymous");

        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        authData.Should().BeEmpty(
            $"Public endpoint '{endpoint.DisplayName}' must not have conflicting IAuthorizeData");
    }
}

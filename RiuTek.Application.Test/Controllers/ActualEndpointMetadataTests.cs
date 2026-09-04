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
    public async Task EndpointDataSource_ContainsAll22Endpoints_WithCorrectRouteAndAuthorizationMetadata()
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

            // Total controller actions: 6 Posts + 6 Comments + 5 Products + 5 Categories = 22
            var controllerEndpoints = endpoints.Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() != null).ToList();
            controllerEndpoints.Should().HaveCount(22);

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

            #region Products Endpoints (5)

            var productEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(ProductsController))
                .ToList();

            productEndpoints.Should().HaveCount(5);

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

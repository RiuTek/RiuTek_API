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
    public async Task EndpointDataSource_ContainsAll12Phase32Endpoints_WithCorrectRouteAndAuthorizationMetadata()
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

            // Act & Assert on Posts Endpoints
            var postEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(PostsController))
                .ToList();

            postEndpoints.Should().HaveCount(6);

            // Helper to get action descriptor
            ControllerActionDescriptor GetDescriptor(RouteEndpoint e) => e.Metadata.GetMetadata<ControllerActionDescriptor>()!;

            // 1. GET api/v1/posts (AllowAnonymous) - Action: GetPosts
            var getPosts = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetPosts) && GetHttpMethods(e).Contains("GET"));
            getPosts.Should().NotBeNull();
            GetFullRoutePattern(getPosts!).Should().BeEquivalentTo("api/v1/posts");
            getPosts!.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

            // 2. GET api/v1/posts/slug/{slug} (AllowAnonymous) - Action: GetBySlug
            var getBySlug = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetBySlug) && GetHttpMethods(e).Contains("GET"));
            getBySlug.Should().NotBeNull();
            GetFullRoutePattern(getBySlug!).Should().BeEquivalentTo("api/v1/posts/slug/{slug}");
            getBySlug!.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

            // 3. GET api/v1/posts/{id:guid} (ContentManager policy) - Action: GetById
            var getById = postEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(PostsController.GetById) && GetHttpMethods(e).Contains("GET"));
            getById.Should().NotBeNull();
            GetFullRoutePattern(getById!).Should().BeEquivalentTo("api/v1/posts/{id:guid}");
            AssertAuthorizePolicy(getById!, Policies.ContentManager);

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

            // Act & Assert on Comments Endpoints
            var commentEndpoints = endpoints.Where(e =>
                e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(CommentsController))
                .ToList();

            commentEndpoints.Should().HaveCount(6);

            // 7. GET api/v1/comments/posts/{postId:guid} (AllowAnonymous) - Action: GetPostComments
            var getPostComments = commentEndpoints.FirstOrDefault(e => GetDescriptor(e).ActionName == nameof(CommentsController.GetPostComments) && GetHttpMethods(e).Contains("GET"));
            getPostComments.Should().NotBeNull();
            GetFullRoutePattern(getPostComments!).Should().BeEquivalentTo("api/v1/comments/posts/{postId:guid}");
            getPostComments!.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

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
            getProdComments!.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();

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
        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        authData.Should().NotBeEmpty();
        authData.Any(a => a.Policy == expectedPolicy).Should().BeTrue($"Expected policy '{expectedPolicy}' on endpoint '{endpoint.DisplayName}'");
    }

    private static void AssertAuthorized(RouteEndpoint endpoint)
    {
        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        authData.Should().NotBeEmpty($"Expected authorization on endpoint '{endpoint.DisplayName}'");
    }
}

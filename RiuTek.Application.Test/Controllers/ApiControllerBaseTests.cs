using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Controllers;
using RiuTek.Core.Common;

namespace RiuTek.Application.Test.Controllers;

public class TestApiController : ApiControllerBase
{
    public IActionResult TestHandleResult<T>(Result<T> result) => HandleResult(result);
    public IActionResult TestHandleResult(Result result) => HandleResult(result);
    public IActionResult TestHandleCreatedResult<T>(Result<T> result, string? actionName = null, object? routeValues = null) =>
        HandleCreatedResult(result, actionName, routeValues);
    public IActionResult TestHandleCreatedResult<T>(Result<T> result, Uri uri) =>
        HandleCreatedResult(result, uri);
    public IActionResult TestHandleNoContentResult(Result result) =>
        HandleNoContentResult(result);
    public IActionResult TestHandleNoContentResult<T>(Result<T> result) =>
        HandleNoContentResult(result);
}

public class ApiControllerBaseTests
{
    private readonly TestApiController _controller = new();

    [Fact]
    public void HandleCreatedResult_WhenSuccessWithoutAction_Returns201Created()
    {
        // Arrange
        var result = Result.Success("CreatedItem");

        // Act
        var actionResult = _controller.TestHandleCreatedResult(result);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        objectResult.Value.Should().Be("CreatedItem");
    }

    [Fact]
    public void HandleCreatedResult_WhenSuccessWithAction_ReturnsCreatedAtActionResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        var result = Result.Success(new { Id = id });

        // Act
        var actionResult = _controller.TestHandleCreatedResult(result, "GetById", new { id });

        // Assert
        var createdResult = actionResult.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.ActionName.Should().Be("GetById");
        createdResult.RouteValues.Should().ContainKey("id");
    }

    [Fact]
    public void HandleCreatedResult_WhenFailure_ReturnsMappedErrorResponse()
    {
        // Arrange
        var error = Error.Validation("Post.InvalidTitle", "Title is invalid");
        var result = Result.Failure<string>(error);

        // Act
        var actionResult = _controller.TestHandleCreatedResult(result);

        // Assert
        var badRequestResult = actionResult.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void HandleNoContentResult_WhenSuccess_Returns204NoContent()
    {
        // Arrange
        var result = Result.Success();

        // Act
        var actionResult = _controller.TestHandleNoContentResult(result);

        // Assert
        actionResult.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public void HandleNoContentResult_WhenFailure_ReturnsMappedErrorResponse()
    {
        // Arrange
        var error = Error.NotFound("Post.NotFound", "Post not found");
        var result = Result.Failure(error);

        // Act
        var actionResult = _controller.TestHandleNoContentResult(result);

        // Assert
        var notFoundResult = actionResult.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}

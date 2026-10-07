using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Features.Users.Queries;

namespace RiuTek.API.Controllers;

[Authorize]
public class UsersController : ApiControllerBase
{
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new UpdateProfileCommand(request.FullName, request.PhoneNumber),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("me/addresses")]
    [ProducesResponseType(typeof(List<UserAddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyAddresses(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetMyAddressesQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("me/addresses")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddAddress(
        [FromBody] AddUserAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new AddUserAddressCommand(
                request.ReceiverName,
                request.PhoneNumber,
                request.AddressLine,
                request.Ward,
                request.District,
                request.City,
                request.IsDefault),
            cancellationToken);
        return HandleCreatedResult(result);
    }

    [HttpPut("me/addresses/{addressId:guid}")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAddress(
        [FromRoute] Guid addressId,
        [FromBody] UpdateUserAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(
            new UpdateUserAddressCommand(
                addressId,
                request.ReceiverName,
                request.PhoneNumber,
                request.AddressLine,
                request.Ward,
                request.District,
                request.City),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPatch("me/addresses/{addressId:guid}/default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDefaultAddress(
        [FromRoute] Guid addressId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new SetDefaultAddressCommand(addressId), cancellationToken);
        return HandleNoContentResult(result);
    }

    [HttpDelete("me/addresses/{addressId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAddress(
        [FromRoute] Guid addressId,
        CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new DeleteUserAddressCommand(addressId), cancellationToken);
        return HandleNoContentResult(result);
    }
}

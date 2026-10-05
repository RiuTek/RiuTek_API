using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.Application.Features.Payments.Commands;

namespace RiuTek.API.Controllers;

[AllowAnonymous]
public class PaymentsController : ApiControllerBase
{
    [HttpPost("stripe/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken cancellationToken = default)
    {
        string payload;
        using (var reader = new StreamReader(Request.Body))
        {
            payload = await reader.ReadToEndAsync(cancellationToken);
        }

        var signature = Request.Headers["Stripe-Signature"].ToString();

        var result = await Mediator.Send(new ProcessStripeWebhookCommand(payload, signature), cancellationToken);
        return HandleResult(result);
    }
}

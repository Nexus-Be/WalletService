namespace Wallet.Application.Features.Checkout.Commands.CheckoutDirect;

using MediatR;
using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Exceptions;

internal sealed class CheckoutDirectCommandHandler(IWalletDbContext context, IStripeCheckoutClient provider)
    : IRequestHandler<CheckoutDirectCommand, DirectPaymentResponse>
{
    public async Task<DirectPaymentResponse> Handle(CheckoutDirectCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId) || orderId == Guid.Empty)
            throw new BusinessRuleValidationException("VAL_INVALID_INPUT", "Order ID must be a valid UUID.");
        var result = await StripeDirectPreparation.CreateAsync(
            context, provider, request.UserId, orderId, request.Amount, cancellationToken);
        return new DirectPaymentResponse(result.TransactionId.ToString(), result.OrderId.ToString(),
            result.Amount, result.PaymentUrl, "Pending", result.CreatedAt);
    }
}

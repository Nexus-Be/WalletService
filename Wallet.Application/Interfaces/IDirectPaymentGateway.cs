namespace Wallet.Application.Interfaces;

public interface IDirectPaymentGateway
{
    string CreatePaymentUrl(
        string gatewayCode,
        Guid orderId,
        decimal amount,
        string currency,
        string? bankCode = null,
        string? returnUrl = null);
}

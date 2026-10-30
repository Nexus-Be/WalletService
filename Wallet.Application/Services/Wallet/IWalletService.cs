namespace Wallet.Application.Services.Wallet;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletService
{
    Task<WalletResponse> GetWalletByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<TopupResponse> TopupVnPayAsync(string userId, TopupVnPayRequest request, string clientIp, CancellationToken cancellationToken = default);
    Task<WithdrawalResponse> WithdrawAsync(string userId, WithdrawalRequest request, CancellationToken cancellationToken = default);
    Task<TransferResponse> TransferAsync(string senderUserId, TransferRequest request, CancellationToken cancellationToken = default);
    Task<WalletTransactionPageResponse> ListTransactionsAsync(string userId, ListTransactionsRequest request, CancellationToken cancellationToken = default);
    Task<DirectPaymentResponse> CheckoutDirectAsync(string userId, DirectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<VnPayIpnResponse> ProcessVnPayIpnAsync(ProcessVnPayIpnRequest request, CancellationToken cancellationToken = default);
}

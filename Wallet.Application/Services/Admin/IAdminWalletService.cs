namespace Wallet.Application.Services.Admin;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IAdminWalletService
{
    Task<ProcessWithdrawalResponse> ProcessWithdrawalAsync(
        Guid id,
        string adminUserId,
        ProcessWithdrawalRequest request,
        CancellationToken cancellationToken = default);

    Task<BalanceAdjustmentResponse> AdjustBalanceAsync(
        Guid walletId,
        string adminUserId,
        BalanceAdjustmentRequest request,
        CancellationToken cancellationToken = default);
}

namespace Wallet.Application.Features.RefundFund.Queries.GetRefundFundStatus;

using MediatR;

/// <summary>The refund fund at a glance: whether it is on, its balance, the caps and what it paid today.</summary>
public sealed record GetRefundFundStatusQuery : IRequest<RefundFundStatusResult>;

public sealed record RefundFundStatusResult(
    bool Enabled,
    decimal Balance,
    decimal PerCaseCap,
    decimal DailyCap,
    decimal SecondApprovalThreshold,
    decimal PaidToday,
    decimal RemainingToday);

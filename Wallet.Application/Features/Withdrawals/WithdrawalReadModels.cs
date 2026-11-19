namespace Wallet.Application.Features.Withdrawals;

using System;
using Wallet.Domain.Entities;

public sealed record WithdrawalSummaryResponse(
    Guid WithdrawalId,
    decimal Amount,
    decimal Fee,
    decimal NetAmount,
    string BankName,
    string MaskedAccountNumber,
    string AccountHolderName,
    string Status,
    string? StatusReason,
    string? TransferReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? CompletedAt);

public sealed record AdminWithdrawalResponse(
    Guid WithdrawalId,
    Guid WalletId,
    Guid OwnerUserId,
    decimal Amount,
    decimal Fee,
    decimal NetAmount,
    string BankName,
    string BankAccountNumber,
    string BankAccountHolder,
    string Status,
    string? StatusReason,
    string? TransferReference,
    Guid? ProcessedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? CompletedAt);

public sealed record WithdrawalHistoryEntry(
    Guid AuditId,
    string Action,
    string? ActorUserId,
    string? OldValues,
    string? NewValues,
    DateTimeOffset CreatedAt);

internal static class WithdrawalReadMapper
{
    internal static WithdrawalSummaryResponse ToSummary(WithdrawalRequest item) => new(
        item.Id,
        item.Amount,
        item.Fee,
        item.NetAmount,
        item.BankName,
        WithdrawalWorkflow.MaskAccount(item.BankAccountNumber),
        item.BankAccountHolder,
        item.Status.ToString(),
        item.StatusReason,
        item.GatewayReferenceId,
        item.CreatedAt,
        item.ProcessedAt,
        item.CompletedAt);

    internal static AdminWithdrawalResponse ToAdminSummary(WithdrawalRequest item, Guid ownerUserId) => new(
        item.Id,
        item.WalletId,
        ownerUserId,
        item.Amount,
        item.Fee,
        item.NetAmount,
        item.BankName,
        item.BankAccountNumber,
        item.BankAccountHolder,
        item.Status.ToString(),
        item.StatusReason,
        item.GatewayReferenceId,
        item.ProcessedBy,
        item.CreatedAt,
        item.ProcessedAt,
        item.CompletedAt);
}

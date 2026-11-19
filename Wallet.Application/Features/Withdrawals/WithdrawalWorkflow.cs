namespace Wallet.Application.Features.Withdrawals;

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Wallet.Application.Common.Extensions;
using Wallet.Domain.Enums;

public enum WithdrawalAction
{
    Approve,
    Reject,
    StartProcessing,
    Complete,
    Fail,
    ResolveFailed
}

internal static class WithdrawalWorkflow
{
    internal const int MinReasonLength = 3;
    internal const int MaxReasonLength = 500;
    internal const int MaxReferenceLength = 100;
    internal const int MaxIdempotencyKeyLength = 100;

    internal static bool TryParse(string? value, out WithdrawalAction action) =>
        value.TryToEnum(out action);

    internal static bool CanTransition(WithdrawalStatus current, WithdrawalAction action) => action switch
    {
        WithdrawalAction.Approve => current == WithdrawalStatus.Pending,
        WithdrawalAction.Reject => current == WithdrawalStatus.Pending,
        WithdrawalAction.StartProcessing => current == WithdrawalStatus.Approved,
        WithdrawalAction.Complete => current is WithdrawalStatus.Approved or WithdrawalStatus.Processing,
        WithdrawalAction.Fail => current == WithdrawalStatus.Approved,
        WithdrawalAction.ResolveFailed => current == WithdrawalStatus.Processing,
        _ => false
    };

    internal static WithdrawalStatus TargetOf(WithdrawalAction action) => action switch
    {
        WithdrawalAction.Approve => WithdrawalStatus.Approved,
        WithdrawalAction.Reject => WithdrawalStatus.Rejected,
        WithdrawalAction.StartProcessing => WithdrawalStatus.Processing,
        WithdrawalAction.Complete => WithdrawalStatus.Completed,
        WithdrawalAction.Fail => WithdrawalStatus.Failed,
        WithdrawalAction.ResolveFailed => WithdrawalStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal static bool RequiresAdmin(WithdrawalAction action) =>
        action == WithdrawalAction.ResolveFailed;

    internal static bool ReversesTheDebit(WithdrawalAction action) =>
        action is WithdrawalAction.Fail or WithdrawalAction.ResolveFailed;

    internal static AuditAction AuditActionOf(WithdrawalAction action) => action switch
    {
        WithdrawalAction.Approve => AuditAction.WithdrawalApprove,
        WithdrawalAction.Reject => AuditAction.WithdrawalReject,
        WithdrawalAction.StartProcessing => AuditAction.WithdrawalStartProcessing,
        WithdrawalAction.Complete => AuditAction.WithdrawalComplete,
        WithdrawalAction.Fail => AuditAction.WithdrawalFail,
        WithdrawalAction.ResolveFailed => AuditAction.WithdrawalResolveFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal static string Fingerprint(
        Guid walletId,
        decimal amount,
        string currency,
        string bankName,
        string accountNumber,
        string accountHolder)
    {
        var canonical = string.Join(
            '\n',
            walletId.ToString("D"),
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            currency.Trim().ToUpperInvariant(),
            bankName.Trim(),
            accountNumber.Trim(),
            accountHolder.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string MaskAccount(string? accountNumber)
    {
        if (string.IsNullOrEmpty(accountNumber))
        {
            return string.Empty;
        }

        return accountNumber.Length <= 4
            ? new string('*', accountNumber.Length)
            : $"{new string('*', accountNumber.Length - 4)}{accountNumber[^4..]}";
    }

    internal static bool IsValidIdempotencyKey(string? key) =>
        !string.IsNullOrEmpty(key) &&
        key.Length <= MaxIdempotencyKeyLength &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

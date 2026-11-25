namespace Wallet.Application.Features.Withdrawals;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    internal const int MinBankNameLength = 2;
    internal const int MaxBankNameLength = 150;
    internal const int MinAccountNumberLength = 4;
    internal const int MaxAccountNumberLength = 100;
    internal const int MinAccountHolderLength = 2;
    internal const int MaxAccountHolderLength = 255;

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

    internal static IEnumerable<ValidationResult> ValidateManualBankDetails(
        string? bankName,
        string? accountNumber,
        string? accountHolderName,
        string bankMemberName,
        string bankDisplayName)
    {
        var trimmedBank = bankName?.Trim() ?? string.Empty;
        if (trimmedBank.Length < MinBankNameLength || trimmedBank.Length > MaxBankNameLength)
        {
            yield return new ValidationResult(
                $"{bankDisplayName} must be between {MinBankNameLength} and {MaxBankNameLength} characters.",
                new[] { bankMemberName });
        }

        var trimmedAccount = accountNumber?.Trim() ?? string.Empty;
        if (trimmedAccount.Length < MinAccountNumberLength ||
            trimmedAccount.Length > MaxAccountNumberLength ||
            !trimmedAccount.All(char.IsAsciiLetterOrDigit))
        {
            yield return new ValidationResult(
                $"Account number must be between {MinAccountNumberLength} and {MaxAccountNumberLength} ASCII letters or digits.",
                new[] { "AccountNumber" });
        }

        var trimmedHolder = accountHolderName?.Trim() ?? string.Empty;
        if (trimmedHolder.Length < MinAccountHolderLength || trimmedHolder.Length > MaxAccountHolderLength)
        {
            yield return new ValidationResult(
                $"Account holder name must be between {MinAccountHolderLength} and {MaxAccountHolderLength} characters.",
                new[] { "AccountHolderName" });
        }
    }
}

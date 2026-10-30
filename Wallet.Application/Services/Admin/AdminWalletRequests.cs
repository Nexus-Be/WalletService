namespace Wallet.Application.Services.Admin;

using System.ComponentModel.DataAnnotations;

public sealed record ProcessWithdrawalRequest(
    [property: Required(ErrorMessage = "Action is required.")]
    string Action,
    string? AdminNote = null
);

public sealed record BalanceAdjustmentRequest(
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    decimal Amount,
    [property: Required(ErrorMessage = "Adjustment type is required.")]
    string AdjustmentType = "Credit",
    [property: Required(ErrorMessage = "Reason is required.")]
    string Reason = ""
);

namespace Wallet.Application.Common.Validation;

using System;
using System.ComponentModel.DataAnnotations;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value switch
    {
        Guid guid => guid != Guid.Empty,
        null => true,
        string text when string.IsNullOrWhiteSpace(text) => true,
        string text => Guid.TryParse(text, out var guid) && guid != Guid.Empty,
        _ => false
    };
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class MoneyPrecisionAttribute : ValidationAttribute
{
    public MoneyPrecisionAttribute()
    {
        ErrorMessage = "Amount must fit the supported decimal(18,2) range.";
    }

    public override bool IsValid(object? value) =>
        value is decimal amount &&
        amount <= 9999999999999999.99m &&
        amount >= -9999999999999999.99m &&
        amount == decimal.Round(amount, 2);
}
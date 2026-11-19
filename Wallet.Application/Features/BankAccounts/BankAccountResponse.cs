namespace Wallet.Application.Features.BankAccounts;

using System;
using Wallet.Domain.Entities;

public sealed record BankAccountResponse(
    Guid Id,
    string BankName,
    string AccountNumber,
    string AccountHolderName,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static BankAccountResponse FromEntity(BankAccount entity) => new(
        entity.Id,
        entity.BankName,
        entity.AccountNumber,
        entity.AccountHolderName,
        entity.IsDefault,
        entity.CreatedAt,
        entity.UpdatedAt);
}

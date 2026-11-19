namespace Wallet.Application.Features.BankAccounts.Commands.CreateBankAccount;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;
using Wallet.Application.Common.Validation;

public sealed record CreateBankAccountCommand(
    [Required(ErrorMessage = "Bank name is required.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Bank name must be between 2 and 150 characters.")]
    string BankName,
    [Required(ErrorMessage = "Account number is required.")]
    [StringLength(100, MinimumLength = 4, ErrorMessage = "Account number must be between 4 and 100 characters.")]
    string AccountNumber,
    [Required(ErrorMessage = "Account holder name is required.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Account holder name must be between 2 and 255 characters.")]
    string AccountHolderName,
    bool IsDefault = false) : IRequest<BankAccountResponse>, IRequestValidation
{
    [JsonIgnore] public Guid UserId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var error = RequestValidation.ValidateGuid(UserId, nameof(UserId), "User ID");
        if (error is not null)
        {
            yield return error;
        }
    }
}

namespace Wallet.Application.Features.AdminWallets.Queries.ListStripeReconciliationRequired;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Domain.Enums;

public sealed record ListStripeReconciliationRequiredQuery(
    [property: Range(1, int.MaxValue)] int Page = 1,
    [property: Range(1, 100)] int PageSize = 20,
    string? Purpose = null) : IRequest<PagedData<StripeReconciliationAttemptItem>>, IRequestValidation
{
    public IEnumerable<ValidationResult> ValidateRequest()
    {
        if (!string.IsNullOrWhiteSpace(Purpose) && !Purpose.TryToEnum<StripePaymentPurpose>(out _))
        {
            yield return new ValidationResult("Stripe payment purpose filter is invalid.", new[] { nameof(Purpose) });
        }
    }
}

public sealed record StripeReconciliationAttemptItem(
    Guid AttemptId,
    string Purpose,
    Guid OwnerId,
    Guid? WalletTopupId,
    Guid? DirectPaymentOrderId,
    decimal Amount,
    string? CheckoutSessionId,
    string? PaymentIntentId,
    string Status,
    bool BlocksReplacement,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

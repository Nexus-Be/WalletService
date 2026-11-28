namespace Wallet.Application.Features.Subsidies.Commands.ReleasePlatformSubsidy;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

/// <summary>
/// Gives a held subsidy back to the budget (order cancelled or refunded before payout). Repeating it is a no-op. A subsidy that was
/// already used by a payout cannot be released (409). A sub-order that never reserved anything returns Status "None".
/// </summary>
public sealed record ReleasePlatformSubsidyCommand(
    [Required(ErrorMessage = "Reason is required.")] [StringLength(200, ErrorMessage = "Reason must be 200 characters or fewer.")] string Reason)
    : IRequest<ReleasePlatformSubsidyResponse>, IRequestValidation
{
    [JsonIgnore] public Guid SubOrderId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var sub = RequestValidation.ValidateGuid(SubOrderId, nameof(SubOrderId), "Sub-order ID");
        if (sub is not null) yield return sub;
    }
}

public sealed record ReleasePlatformSubsidyResponse(string SubOrderId, string Status, decimal Amount, bool Changed);

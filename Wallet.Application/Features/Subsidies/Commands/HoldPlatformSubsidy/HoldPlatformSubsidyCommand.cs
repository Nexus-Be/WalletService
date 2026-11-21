namespace Wallet.Application.Features.Subsidies.Commands.HoldPlatformSubsidy;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common.Extensions;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

/// <summary>
/// Reserves the platform-funded voucher subsidy of one sub-order. Idempotent per sub-order: repeating the same hold returns the same
/// reservation and never counts twice. Fails (409) when there is no approved budget, the per-order cap or the daily or total limit
/// would be exceeded; it never creates budget and never goes negative.
/// </summary>
public sealed record HoldPlatformSubsidyCommand(
    Guid OrderId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")] [MoneyPrecision] decimal Amount)
    : IRequest<SubsidyReservationResponse>, IRequestValidation
{
    [JsonIgnore] public Guid SubOrderId { get; init; }

    public IEnumerable<ValidationResult> ValidateRequest()
    {
        var order = RequestValidation.ValidateGuid(OrderId, nameof(OrderId), "Order ID");
        if (order is not null) yield return order;
        var sub = RequestValidation.ValidateGuid(SubOrderId, nameof(SubOrderId), "Sub-order ID");
        if (sub is not null) yield return sub;
    }
}

public sealed record SubsidyReservationResponse(
    string ReservationId, string BudgetId, string OrderId, string SubOrderId, decimal Amount, string Status, bool IsReplay);

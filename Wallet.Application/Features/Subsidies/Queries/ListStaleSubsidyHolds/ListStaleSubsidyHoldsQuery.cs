namespace Wallet.Application.Features.Subsidies.Queries.ListStaleSubsidyHolds;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;

/// <summary>Platform subsidy reservations that are still held after <see cref="OlderThanMinutes"/>. Commerce checks each against its own sub-orders.</summary>
public sealed record ListStaleSubsidyHoldsQuery(
    [Range(5, 10080, ErrorMessage = "Older-than minutes must be between 5 and 10080.")] int OlderThanMinutes = 30,
    [Range(1, 100, ErrorMessage = "Limit must be between 1 and 100.")] int Limit = 50)
    : IRequest<IReadOnlyList<StaleSubsidyHoldResult>>;

public sealed record StaleSubsidyHoldResult(Guid SubOrderId, Guid OrderId, decimal Amount, DateTimeOffset HeldSince);

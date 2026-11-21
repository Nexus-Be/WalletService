namespace Wallet.Application.Features.Subsidies.Queries.ListSubsidyBudgets;

using System.ComponentModel.DataAnnotations;
using MediatR;
using Wallet.Application.Common.Models;
using Wallet.Application.Features.Subsidies.Commands.CreateSubsidyBudget;

public sealed record ListSubsidyBudgetsQuery(
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")] int Page = 1,
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")] int PageSize = 20)
    : IRequest<PagedData<SubsidyBudgetResult>>;

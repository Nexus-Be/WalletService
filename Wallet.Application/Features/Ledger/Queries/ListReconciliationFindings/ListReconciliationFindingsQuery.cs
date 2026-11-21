namespace Wallet.Application.Features.Ledger.Queries.ListReconciliationFindings;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Wallet.Application.Common;
using Wallet.Application.Common.Ledger;
using Wallet.Application.Common.Models;
using Wallet.Application.Common.Validation;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Wallet.Domain.Exceptions;

using Wallet.Application.Features.Ledger.Queries.ListReconciliationRuns;

public sealed record ListReconciliationFindingsQuery(
    [Range(1, int.MaxValue, ErrorMessage = "Page must be greater than or equal to 1.")] int Page = 1,
    [Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")] int PageSize = 20,
    string? Status = null,
    string? InvariantCode = null,
    string? Severity = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null) : IRequest<PagedData<ReconciliationFindingResult>>;

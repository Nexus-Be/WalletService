namespace Wallet.Application.Features.Ledger.Queries.GetTrialBalance;

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

public sealed record GetTrialBalanceQuery : IRequest<TrialBalanceResult>;

public sealed record TrialBalanceAccount(string Account, decimal TotalDebit, decimal TotalCredit);

public sealed record TrialBalanceResult(IReadOnlyList<TrialBalanceAccount> Accounts, decimal TotalDebit, decimal TotalCredit, bool Balanced);

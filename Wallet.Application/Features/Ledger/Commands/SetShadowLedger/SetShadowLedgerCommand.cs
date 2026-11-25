namespace Wallet.Application.Features.Ledger.Commands.SetShadowLedger;

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

public sealed record SetShadowLedgerCommand(bool Enabled, int ExpectedVersion) : IRequest<SetShadowLedgerResult>
{
    [JsonIgnore] public Guid AdminUserId { get; init; }
}

public sealed record SetShadowLedgerResult(bool Enabled, int ShadowEpoch, int Version, bool Changed);

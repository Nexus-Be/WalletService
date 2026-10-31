namespace Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;

using System.Collections.Generic;
using MediatR;

public sealed record ProcessVnPayReturnQuery(IReadOnlyDictionary<string, string> Parameters)
    : IRequest<VnPayReturnResponse>;

public sealed record VnPayReturnResponse(
    bool Success,
    string? TransactionReference,
    string? ResponseCode,
    string? TransactionNumber,
    string Message);

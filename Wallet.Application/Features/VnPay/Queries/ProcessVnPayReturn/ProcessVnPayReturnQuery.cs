namespace Wallet.Application.Features.VnPay.Queries.ProcessVnPayReturn;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;

public sealed record ProcessVnPayReturnQuery(
    [Required(ErrorMessage = "VNPay parameters are required.")]
    IReadOnlyDictionary<string, string> Parameters)
    : IRequest<VnPayReturnResponse>;

public sealed record VnPayReturnResponse(
    bool Success,
    string? TransactionReference,
    string? ResponseCode,
    string? TransactionNumber,
    string Message);
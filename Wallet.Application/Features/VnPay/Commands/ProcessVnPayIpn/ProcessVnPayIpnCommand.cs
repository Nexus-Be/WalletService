namespace Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MediatR;

public sealed record ProcessVnPayIpnCommand(
    [Required(ErrorMessage = "VNPay parameters are required.")]
    IReadOnlyDictionary<string, string> Parameters,
    string? ClientIp = null) : IRequest<VnPayIpnResponse>;

public sealed record VnPayIpnResponse(string ResponseCode, string Message);
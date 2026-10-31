namespace Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;

using System.Collections.Generic;
using MediatR;

public sealed record ProcessVnPayIpnCommand(
    IReadOnlyDictionary<string, string> Parameters,
    string? ClientIp = null) : IRequest<VnPayIpnResponse>;

public sealed record VnPayIpnResponse(string ResponseCode, string Message);

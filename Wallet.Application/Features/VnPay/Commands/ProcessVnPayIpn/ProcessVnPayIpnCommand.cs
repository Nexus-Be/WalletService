namespace Wallet.Application.Features.VnPay.Commands.ProcessVnPayIpn;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MediatR;

public sealed record ProcessVnPayIpnCommand(
    [Required(ErrorMessage = "VNPay parameters are required.")]
    IReadOnlyDictionary<string, string> Parameters,
    string? ClientIp = null) : IRequest<VnPayIpnResponse>;

/// <summary>VNPay acknowledgement. The wire format (RspCode/Message) is mandated by the VNPay IPN specification.</summary>
public sealed record VnPayIpnResponse(
    [property: JsonPropertyName("RspCode")] string ResponseCode,
    [property: JsonPropertyName("Message")] string Message);
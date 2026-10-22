using Wallet.Domain.Base;
using Wallet.Domain.Enums;

namespace Wallet.Domain.Entities;

public class VnpayIpnLog : BaseEntity<Guid>
{
    public required string VnpTxnRef { get; set; }
    public string? VnpResponseCode { get; set; }
    public string? VnpTransactionNo { get; set; }
    public string? ClientIp { get; set; }
    public string? RawPayload { get; set; }
    public required IpnProcessingStatus ProcessingStatus { get; set; } = IpnProcessingStatus.Processed;

    // Navigation
    public WalletTopup? WalletTopup { get; set; }
    public DirectPaymentOrder? DirectPaymentOrder { get; set; }
}

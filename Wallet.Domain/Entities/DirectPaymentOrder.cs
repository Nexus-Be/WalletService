using Wallet.Domain.Base;
using Wallet.Domain.Enums;

namespace Wallet.Domain.Entities;

public class DirectPaymentOrder : BaseEntity<Guid>
{
    public required Guid OrderId { get; set; }
    public required string VnpTxnRef { get; set; }
    public string? VnpTransactionNo { get; set; }
    public required decimal Amount { get; set; }
    public required TopupStatus Status { get; set; } = TopupStatus.Pending;
    public string? RawResponse { get; set; }

    // Navigation
    public ICollection<VnpayIpnLog> IpnLogs { get; set; } = new List<VnpayIpnLog>();
}

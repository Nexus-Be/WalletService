namespace Wallet.Domain.Enums;

public enum IpnProcessingStatus
{
    Processed = 0,
    Duplicate = 1,
    InvalidSignature = 2,
    OrderNotFound = 3,
    InvalidAmount = 4,
    SystemError = 5
}

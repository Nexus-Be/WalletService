namespace Wallet.Domain.Enums;

public enum StripePaymentAttemptStatus
{
    CreationPending = 0,
    AwaitingPayment = 1,
    Processing = 2,
    Successful = 3,
    Failed = 4,
    Expired = 5,
    ReconciliationRequired = 6
}

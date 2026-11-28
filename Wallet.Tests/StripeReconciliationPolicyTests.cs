namespace Wallet.Tests;

using Wallet.Application.Features.Stripe;
using Wallet.Application.Interfaces;
using Wallet.Domain.Entities;
using Wallet.Domain.Enums;
using Xunit;

public sealed class StripeReconciliationPolicyTests
{
    [Theory]
    [InlineData("open", "unpaid", "processing")]
    [InlineData("complete", "unpaid", "processing")]
    [InlineData("complete", "unpaid", "requires_payment_method")]
    [InlineData("open", "unpaid", "canceled")]
    public void NonterminalProviderState_RemainsBlocked(string sessionStatus, string paymentStatus, string intentStatus)
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.Pending,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, sessionStatus, paymentStatus),
                Intent(attempt, intentStatus)));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("expired")]
    public void TerminalSessionAndCanceledIntent_CanPermitReplacement(string sessionStatus)
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.CannotSucceed,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, sessionStatus, "unpaid"),
                Intent(attempt, "canceled")));
    }

    [Fact]
    public void ExpiredSessionWithoutIntent_CanPermitReplacement()
    {
        var attempt = CreateAttempt();
        var session = Session(attempt, "expired", "unpaid") with { PaymentIntentId = null };
        Assert.Equal(StripeReconciliationOutcome.CannotSucceed,
            StripeReconciliationPolicy.Evaluate(attempt, session, null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void ExpiredSessionWithUnconfirmedRecovery_RemainsBlocked(bool? recoveryEnabled)
    {
        var attempt = CreateAttempt();
        var session = Session(attempt, "expired", "unpaid") with
        {
            PaymentIntentId = null,
            RecoveryEnabled = recoveryEnabled
        };
        Assert.Equal(StripeReconciliationOutcome.Pending,
            StripeReconciliationPolicy.Evaluate(attempt, session, null));
    }

    [Fact]
    public void PaidSessionAndSuccessfulIntent_AreConfirmedPaid()
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.ConfirmedPaid,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, "complete", "paid"),
                Intent(attempt, "succeeded", 100000)));
    }

    [Fact]
    public void PaidSessionWithoutMatchingReceivedAmount_IsNotConfirmed()
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.IntegrityMismatch,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, "complete", "paid"),
                Intent(attempt, "succeeded", 0)));
    }

    [Fact]
    public void MissingIntentSnapshot_RemainsBlocked()
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.Pending,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, "expired", "unpaid"), null));
    }

    [Fact]
    public void EmptyProviderSessionId_IsAnIntegrityMismatchEvenBeforeBinding()
    {
        var attempt = CreateAttempt();
        attempt.CheckoutSessionId = null;
        var session = Session(attempt, "expired", "unpaid") with
        {
            SessionId = "",
            PaymentIntentId = null
        };

        Assert.Equal(StripeReconciliationOutcome.IntegrityMismatch,
            StripeReconciliationPolicy.Evaluate(attempt, session, null));
    }

    [Fact]
    public void UnknownProviderStates_DoNotPermitReplacement()
    {
        var attempt = CreateAttempt();
        Assert.Equal(StripeReconciliationOutcome.Pending,
            StripeReconciliationPolicy.Evaluate(attempt, Session(attempt, "unknown", "unpaid"),
                Intent(attempt, "canceled")));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("amount")]
    [InlineData("metadata")]
    [InlineData("client-reference")]
    [InlineData("intent")]
    [InlineData("intent-metadata")]
    public void MismatchedIdentity_IsEscalated(string change)
    {
        var attempt = CreateAttempt();
        var session = Session(attempt, "complete", "paid");
        var intent = Intent(attempt, "succeeded", 100000);
        switch (change)
        {
            case "session": session = session with { SessionId = "cs_test_other" }; break;
            case "amount": session = session with { AmountVnd = 1 }; break;
            case "metadata": session = session with { AttemptReference = Guid.NewGuid().ToString() }; break;
            case "client-reference": session = session with { ClientReference = Guid.NewGuid().ToString() }; break;
            case "intent": intent = intent with { PaymentIntentId = "pi_other" }; break;
            case "intent-metadata": intent = intent with { AttemptReference = Guid.NewGuid().ToString() }; break;
        }

        Assert.Equal(StripeReconciliationOutcome.IntegrityMismatch,
            StripeReconciliationPolicy.Evaluate(attempt, session, intent));
    }

    private static StripePaymentAttempt CreateAttempt() => new()
    {
        Id = Guid.NewGuid(),
        Purpose = StripePaymentPurpose.DirectOrder,
        OwnerId = Guid.NewGuid(),
        DirectPaymentOrderId = Guid.NewGuid(),
        Amount = 100000,
        RequestFingerprint = "fingerprint",
        ProviderIdempotencyKey = "key",
        CheckoutSessionId = "cs_test_123"
    };

    private static StripeSessionSnapshot Session(StripePaymentAttempt attempt, string status, string paymentStatus) =>
        new("cs_test_123", status, paymentStatus, 100000, "pi_123", attempt.Id.ToString(), attempt.Id.ToString(), false);

    private static StripePaymentIntentSnapshot Intent(StripePaymentAttempt attempt, string status, long received = 0) =>
        new("pi_123", status, 100000, received, attempt.Id.ToString());
}

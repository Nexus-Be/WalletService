namespace Wallet.Application.Common;

/// <summary>
/// Settings of the refund fund (section "RefundFund"). Off by default in every environment. The caps are in VND and are meant to start low;
/// the owner changes them in configuration, not in code.
/// </summary>
public static class RefundFundClock
{
    /// <summary>
    /// Midnight of today in Vietnam (UTC+7), as an instant in UTC. It must be UTC: PostgreSQL refuses a DateTimeOffset that has another
    /// offset, which the first QA run of the fund found (the status and the payout failed with 400).
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateTimeOffset now)
    {
        var offset = TimeSpan.FromHours(7);
        var local = now.ToOffset(offset);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, offset).ToUniversalTime();
    }
}

public sealed class RefundFundOptions
{
    public bool Enabled { get; set; }

    /// <summary>The largest refund paid from the fund for one sub-order. A bigger case goes to NeedsFinance.</summary>
    public decimal PerCaseCap { get; set; } = 500_000m;

    /// <summary>The most the fund pays out in one day (Vietnam time). Beyond it, cases go to NeedsFinance.</summary>
    public decimal DailyCap { get; set; } = 5_000_000m;

    /// <summary>Above this amount a second admin, different from the first, must also approve.</summary>
    public decimal SecondApprovalThreshold { get; set; } = 200_000m;
}

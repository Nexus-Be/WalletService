namespace Wallet.Application.Common;

/// <summary>
/// Settings of the refund fund (section "RefundFund"). Off by default in every environment. The caps are in VND and are meant to start low;
/// the owner changes them in configuration, not in code.
/// </summary>
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

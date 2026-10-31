namespace Wallet.Application.Common.Extensions;

using System;
using System.Linq;

public static class StatusTextExtensions
{
    /// <summary>Normalizes a persisted status token into a PascalCase API string.</summary>
    public static string ToApiStatus(this string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        var parts = status
            .Trim()
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());

        return string.Concat(parts);
    }
}

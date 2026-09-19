using System.Globalization;

namespace OmnisRouter.LocalProxy;

/// <summary>The lines the tray popup shows for cache hygiene. Presentation-neutral, so it is unit-tested
/// without WinForms. <see cref="Secondary"/> and <see cref="Basis"/> are null when they do not apply.</summary>
public sealed record CacheHygieneDisplayLines(string Primary, string? Secondary, string? Basis);

/// <summary>
/// Formats a <see cref="CacheHygieneSummaryInfo"/> (or its absence) into the popup's cache-hygiene lines
/// (contracts/tray-popup.md). Every figure carries its unit and period, £ figures carry their basis, and
/// nothing content-bearing can appear because the input is scalars and labels only (Principle XII).
/// </summary>
public static class CacheHygieneDisplay
{
    public static CacheHygieneDisplayLines Format(CacheHygieneSummaryInfo? summary)
    {
        if (summary is null)
        {
            return new CacheHygieneDisplayLines("Cache hygiene: not measuring", null, null);
        }

        if (!summary.MeasurementEnabled)
        {
            return new CacheHygieneDisplayLines("Cache hygiene: measurement off", null, null);
        }

        if (NothingMeasured(summary))
        {
            return new CacheHygieneDisplayLines("Cache hygiene: nothing measured yet", null, null);
        }

        var today = summary.Today;

        // Collect mode reports gross observed cache-write cost, always a shadow figure, never classified.
        if (string.Equals(summary.Source, "collect", StringComparison.Ordinal))
        {
            return new CacheHygieneDisplayLines(
                $"Observed cache-write cost today: {Money(today.AvoidableWasteGbp)} (estimate, not a bill)",
                null,
                Basis(summary));
        }

        var shadow = summary.ShadowPrice ? " (shadow estimate)" : string.Empty;
        return new CacheHygieneDisplayLines(
            $"Cache waste today: {Money(today.AvoidableWasteGbp)} avoidable{shadow}",
            $"Recovered by fixes today: {Money(today.RecoveredGbp)}",
            Basis(summary));
    }

    private static bool NothingMeasured(CacheHygieneSummaryInfo s) =>
        s.SinceStart.MissCount == 0 && s.SinceStart.RecoveryCount == 0
        && s.Today.MissCount == 0 && s.Today.RecoveryCount == 0;

    private static string Money(decimal gbp) => "£" + gbp.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? Basis(CacheHygieneSummaryInfo s)
    {
        if (s.PricingVersion is null)
        {
            return null;
        }

        var kind = s.ShadowPrice ? "shadow, not a bill" : "billed";
        var rate = s.UsdGbp?.ToString("0.####", CultureInfo.InvariantCulture) ?? "?";
        return $"basis: prices {s.PricingVersion}, USD→GBP {rate} ({s.FxDate}); {kind}";
    }
}

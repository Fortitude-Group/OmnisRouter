namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Stamps every £ figure with the pricing snapshot and FX rate it was computed from, and whether it is
/// a measured pound (pay-as-you-go) or a shadow estimate (subscription — never a bill). Ported from the
/// sibling tool's PricingStamp (research D6).
/// </summary>
public sealed record PricingStamp(string PricingVersion, string FxDate, decimal UsdGbp, bool ShadowPrice);

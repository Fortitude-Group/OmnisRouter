namespace OmnisRouter.CacheHygiene;

/// <summary>
/// A control-plane opinion on which byte-mutating fixes may run, layered over the local config so an
/// OmnisVigil policy can turn a fix class on or off fleet-wide the way it already carries caps and
/// kill-state (spec FR-012). Kept as an abstraction here so <see cref="CacheHygieneService"/> never
/// depends on the Vigil layer — the Vigil-backed implementation lives there and is injected.
/// </summary>
public interface IFixPolicy
{
    /// <summary>
    /// <c>true</c>/<c>false</c> forces the fix on/off from the control plane; <c>null</c> means the
    /// policy has no opinion (no policy fetched, or it omits the fix section) and the local config
    /// decides. This mirrors how <c>allowed_models</c> overrides only where present.
    /// </summary>
    bool? IsFixEnabled(FixClass fix);
}

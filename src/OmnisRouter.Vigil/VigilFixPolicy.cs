using OmnisRouter.CacheHygiene;

namespace OmnisRouter.Vigil;

/// <summary>
/// Bridges the polled OmnisVigil policy to the cache-hygiene fix decision (FR-012). When the current
/// policy carries a <c>cache_fixes</c> set it is authoritative — a fix is on iff its wire name is in the
/// set — so an operator can enable or disable fixes across the whole fleet from the control plane. When
/// no policy is present, or it omits the section, this defers (<c>null</c>) to the router's local config.
/// </summary>
public sealed class VigilFixPolicy : IFixPolicy
{
    private readonly VigilPolicyState _state;

    public VigilFixPolicy(VigilPolicyState state) => _state = state;

    public bool? IsFixEnabled(FixClass fix)
    {
        var fixes = _state.Current?.CacheFixes;
        if (fixes is null)
        {
            return null;   // no opinion — the local config decides
        }

        return fixes.Contains(fix.Wire());
    }
}

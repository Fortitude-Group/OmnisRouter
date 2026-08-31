using System.Collections.Concurrent;

namespace OmnisRouter.Vigil;

/// <summary>Outcome of checking a request against the current policy.</summary>
public enum PolicyGate
{
    Allow,
    OrgKilled,
    TeamKilled,
    OverBudget,
}

/// <summary>
/// Holds the current OmnisVigil policy and this router's spend since the last policy update, and
/// decides whether a request may proceed. Thread-safe and lock-free on the hot path. When no policy
/// has been fetched (the integration is off, or the first poll has not landed) every request is
/// allowed, so enforcement only ever tightens once a policy is present.
/// </summary>
public sealed class VigilPolicyState
{
    private volatile VigilPolicy? _policy;
    private long _localTotalMicros;
    private ConcurrentDictionary<string, long> _localPerProjectMicros = new();

    public VigilPolicy? Current => _policy;

    /// <summary>Applies a freshly polled policy and resets local spend (Vigil's spent now covers it).</summary>
    public void Update(VigilPolicy policy)
    {
        _policy = policy;
        Interlocked.Exchange(ref _localTotalMicros, 0);
        _localPerProjectMicros = new ConcurrentDictionary<string, long>();
    }

    /// <summary>Adds an actual request cost to this router's running local total (micro-USD, lock-free).</summary>
    public void RecordSpend(string? project, decimal usd)
    {
        if (usd <= 0m)
        {
            return;
        }

        var micros = (long)(usd * 1_000_000m);
        Interlocked.Add(ref _localTotalMicros, micros);
        if (!string.IsNullOrEmpty(project))
        {
            _localPerProjectMicros.AddOrUpdate(project, micros, (_, current) => current + micros);
        }
    }

    public PolicyGate Evaluate(string? project, string? team = null)
    {
        var policy = _policy;
        if (policy is null)
        {
            return PolicyGate.Allow;
        }

        if (policy.Kill.Org)
        {
            return PolicyGate.OrgKilled;
        }

        if (!string.IsNullOrEmpty(team) && policy.Kill.Teams.Count > 0
            && policy.Kill.Teams.Contains(team, StringComparer.OrdinalIgnoreCase))
        {
            return PolicyGate.TeamKilled;
        }

        if (policy.Caps.MonthlyUsd is { } monthly && monthly > 0m)
        {
            var effective = policy.Caps.SpentUsd + LocalUsd(Interlocked.Read(ref _localTotalMicros));
            if (effective >= monthly)
            {
                return PolicyGate.OverBudget;
            }
        }

        if (!string.IsNullOrEmpty(project)
            && policy.Caps.PerProjectUsd.TryGetValue(project, out var projectCap) && projectCap > 0m)
        {
            var spent = policy.Caps.SpentPerProjectUsd.TryGetValue(project, out var s) ? s : 0m;
            var local = _localPerProjectMicros.TryGetValue(project, out var lp) ? LocalUsd(lp) : 0m;
            if (spent + local >= projectCap)
            {
                return PolicyGate.OverBudget;
            }
        }

        return PolicyGate.Allow;
    }

    private static decimal LocalUsd(long micros) => (decimal)micros / 1_000_000m;
}

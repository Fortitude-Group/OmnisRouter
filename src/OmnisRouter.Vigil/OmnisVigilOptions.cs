namespace OmnisRouter.Vigil;

/// <summary>
/// Configuration for the optional OmnisVigil integration (the paid team control plane). Bound from
/// the "OmnisVigil" configuration section. When <see cref="Enabled"/> is false or the section is
/// absent, the router runs fully standalone (the free tier): no receipt push, no policy poll, only
/// its own local cap and kill-switch.
/// </summary>
public sealed class OmnisVigilOptions
{
    public const string SectionName = "OmnisVigil";

    /// <summary>Turns the integration on. Off by default so the open router is standalone out of the box.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the OmnisVigil collector, e.g. https://ingest.omnisvigil.fortitude-omnis.group.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Project key (Bearer credential). A secret, supplied from the environment, never committed.</summary>
    public string? ProjectKey { get; set; }

    /// <summary>Stable id for this router deployment (router_id on every receipt). Defaults to the machine name.</summary>
    public string? RouterId { get; set; }

    /// <summary>Flush a receipt batch after this many records.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Flush a receipt batch at least this often, even below <see cref="BatchSize"/>.</summary>
    public int FlushSeconds { get; set; } = 10;

    /// <summary>How often to poll OmnisVigil for the current policy (caps, kill state, allowed models).</summary>
    public int PolicyPollSeconds { get; set; } = 45;
}

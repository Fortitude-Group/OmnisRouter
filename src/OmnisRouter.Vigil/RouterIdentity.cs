namespace OmnisRouter.Vigil;

/// <summary>
/// Stable identity of this router deployment. Stamped as <c>router_id</c> on every content-free
/// receipt pushed to OmnisVigil, so a fleet of routers under one tenant can be told apart and spend
/// attributed per deployment.
/// </summary>
public sealed class RouterIdentity
{
    public RouterIdentity(string routerId) => RouterId = routerId;

    public string RouterId { get; }
}

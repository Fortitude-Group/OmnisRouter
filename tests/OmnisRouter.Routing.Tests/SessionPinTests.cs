using OmnisRouter.Core.Model;
using OmnisRouter.Routing.Pinning;

namespace OmnisRouter.Routing.Tests;

public class SessionPinTests
{
    private static readonly ModelRef ModelA = new(Provider.Anthropic, "claude-opus-4-8");
    private static readonly ModelRef ModelB = new(Provider.OpenAI, "gpt-5");

    private static SessionPinner NewPinner(TimeSpan? ttl = null) =>
        new(new SessionPinnerOptions { ServerSecret = "test-server-secret", Ttl = ttl ?? TimeSpan.FromMinutes(30) });

    private static ChatRequest RequestOf(string? sessionId, string systemText, string firstUserText) => new()
    {
        OriginFormat = ClientFormat.OpenAI,
        SessionId = sessionId,
        System = systemText.Length == 0 ? [] : [new TextPart(systemText)],
        Messages = [new Message(Role.User, [new TextPart(firstUserText)])],
    };

    [Fact]
    public void Client_supplied_session_header_wins_over_derivation()
    {
        var pinner = NewPinner();
        var request = RequestOf("client-header-value", "system prompt", "first message");

        var key = pinner.ResolveKey(request, "tenant-1");

        Assert.Equal("client-header-value", key);
    }

    [Fact]
    public void Derived_key_is_stable_for_same_system_and_first_user_text()
    {
        var pinner = NewPinner();
        var request1 = RequestOf(null, "system prompt", "first message");
        var request2 = RequestOf(null, "system prompt", "first message");

        var key1 = pinner.ResolveKey(request1, "tenant-1");
        var key2 = pinner.ResolveKey(request2, "tenant-1");

        Assert.Equal(key1, key2);
        // 128 bits = 16 bytes = 32 hex chars, lowercase.
        Assert.Equal(32, key1.Length);
        Assert.Equal(key1, key1.ToLowerInvariant());
    }

    [Fact]
    public void Derived_key_differs_when_system_text_changes()
    {
        var pinner = NewPinner();
        var request1 = RequestOf(null, "system prompt A", "first message");
        var request2 = RequestOf(null, "system prompt B", "first message");

        var key1 = pinner.ResolveKey(request1, "tenant-1");
        var key2 = pinner.ResolveKey(request2, "tenant-1");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Derived_key_differs_when_first_user_text_changes()
    {
        var pinner = NewPinner();
        var request1 = RequestOf(null, "system prompt", "first message A");
        var request2 = RequestOf(null, "system prompt", "first message B");

        var key1 = pinner.ResolveKey(request1, "tenant-1");
        var key2 = pinner.ResolveKey(request2, "tenant-1");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Derived_key_differs_across_tenants_for_same_text()
    {
        var pinner = NewPinner();
        var request = RequestOf(null, "system prompt", "first message");

        var key1 = pinner.ResolveKey(request, "tenant-1");
        var key2 = pinner.ResolveKey(request, "tenant-2");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetPin_returns_null_when_no_pin_exists()
    {
        var pinner = NewPinner();

        Assert.Null(pinner.GetPin("some-key", clusterId: 0));
    }

    [Fact]
    public void GetPin_returns_the_pinned_model_for_the_same_cluster()
    {
        var pinner = NewPinner();
        pinner.Pin("session-1", ModelA, clusterId: 5);

        var result = pinner.GetPin("session-1", clusterId: 5);

        Assert.Equal(ModelA, result);
    }

    [Fact]
    public void GetPin_returns_null_after_a_cluster_change()
    {
        var pinner = NewPinner();
        pinner.Pin("session-1", ModelA, clusterId: 5);

        var result = pinner.GetPin("session-1", clusterId: 9);

        Assert.Null(result);
    }

    [Fact]
    public void Pin_refreshes_the_stored_model_and_cluster()
    {
        var pinner = NewPinner();
        pinner.Pin("session-1", ModelA, clusterId: 5);
        pinner.Pin("session-1", ModelB, clusterId: 7);

        Assert.Null(pinner.GetPin("session-1", clusterId: 5));
        Assert.Equal(ModelB, pinner.GetPin("session-1", clusterId: 7));
    }

    [Fact]
    public void GetPin_returns_null_once_the_pin_has_expired()
    {
        var pinner = NewPinner(ttl: TimeSpan.FromMilliseconds(1));
        pinner.Pin("session-1", ModelA, clusterId: 5);

        Thread.Sleep(50);

        Assert.Null(pinner.GetPin("session-1", clusterId: 5));
    }
}

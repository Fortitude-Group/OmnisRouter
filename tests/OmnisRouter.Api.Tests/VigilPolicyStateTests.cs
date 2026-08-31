using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

public class VigilPolicyStateTests
{
    private static VigilPolicy Policy(
        bool kill = false,
        decimal? monthly = null,
        decimal spent = 0m,
        Dictionary<string, decimal>? perProject = null,
        Dictionary<string, decimal>? spentPerProject = null,
        string[]? killTeams = null) => new()
    {
        Kill = new PolicyKill { Org = kill, Teams = killTeams ?? [] },
        Caps = new PolicyCaps
        {
            MonthlyUsd = monthly,
            SpentUsd = spent,
            PerProjectUsd = perProject ?? [],
            SpentPerProjectUsd = spentPerProject ?? [],
        },
    };

    [Fact]
    public void No_policy_allows_everything()
    {
        Assert.Equal(PolicyGate.Allow, new VigilPolicyState().Evaluate("web"));
    }

    [Fact]
    public void Org_kill_halts_every_request()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(kill: true));
        Assert.Equal(PolicyGate.OrgKilled, state.Evaluate(null));
        Assert.Equal(PolicyGate.OrgKilled, state.Evaluate("web"));
    }

    [Fact]
    public void Team_kill_halts_only_the_named_team()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(killTeams: ["payments"]));

        Assert.Equal(PolicyGate.TeamKilled, state.Evaluate("web", "payments"));
        Assert.Equal(PolicyGate.TeamKilled, state.Evaluate("web", "Payments")); // case-insensitive
        Assert.Equal(PolicyGate.Allow, state.Evaluate("web", "platform"));      // other team runs
        Assert.Equal(PolicyGate.Allow, state.Evaluate("web", null));            // untagged runs
    }

    [Fact]
    public void Org_kill_takes_precedence_over_a_team_kill()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(kill: true, killTeams: ["payments"]));

        Assert.Equal(PolicyGate.OrgKilled, state.Evaluate("web", "payments"));
    }

    [Fact]
    public void Fleet_spend_at_the_monthly_cap_blocks()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(monthly: 100m, spent: 100m));
        Assert.Equal(PolicyGate.OverBudget, state.Evaluate(null));
    }

    [Fact]
    public void Local_spend_since_last_poll_pushes_over_the_cap()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(monthly: 100m, spent: 98m));
        Assert.Equal(PolicyGate.Allow, state.Evaluate(null));

        state.RecordSpend(null, 2m); // fleet 98 + local 2 = 100 >= cap
        Assert.Equal(PolicyGate.OverBudget, state.Evaluate(null));
    }

    [Fact]
    public void Per_project_cap_blocks_only_that_project()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(
            perProject: new() { ["web"] = 10m },
            spentPerProject: new() { ["web"] = 10m }));

        Assert.Equal(PolicyGate.OverBudget, state.Evaluate("web"));
        Assert.Equal(PolicyGate.Allow, state.Evaluate("infra"));
    }

    [Fact]
    public void A_fresh_policy_resets_local_spend()
    {
        var state = new VigilPolicyState();
        state.Update(Policy(monthly: 100m, spent: 0m));
        state.RecordSpend(null, 200m);
        Assert.Equal(PolicyGate.OverBudget, state.Evaluate(null));

        state.Update(Policy(monthly: 100m, spent: 0m)); // next poll: Vigil's spent now covers it
        Assert.Equal(PolicyGate.Allow, state.Evaluate(null));
    }

    [Fact]
    public void Parse_reads_caps_fleet_spend_kill_and_models()
    {
        const string json = """
            {
              "policy_version": "v1",
              "caps": {
                "monthly_usd": 5000,
                "per_project_usd": { "web": 2000 },
                "spent_usd": 214.30,
                "spent_per_project_usd": { "web": 120.00 }
              },
              "allowed_models": ["anthropic/claude-haiku-4-5", "openai/gpt-5"],
              "confidence_floor": 0.6,
              "kill": { "org": true, "teams": [] }
            }
            """;

        var policy = HttpVigilPolicyClient.Parse(json);

        Assert.Equal("v1", policy.PolicyVersion);
        Assert.Equal(5000m, policy.Caps.MonthlyUsd);
        Assert.Equal(2000m, policy.Caps.PerProjectUsd["web"]);
        Assert.Equal(214.30m, policy.Caps.SpentUsd);
        Assert.Equal(120.00m, policy.Caps.SpentPerProjectUsd["web"]);
        Assert.Equal(0.6, policy.ConfidenceFloor);
        Assert.True(policy.Kill.Org);
        Assert.Contains("openai/gpt-5", policy.AllowedModels);
    }
}

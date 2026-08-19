using System.Globalization;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>
/// Flags for the <c>build-model</c> CLI command. Defaults (<see cref="Defaults"/>) point at the
/// bundled sample dataset/bench-results/config so a bare <c>dotnet run -- build-model</c> works with
/// no flags.
/// </summary>
public sealed record RoutingModelBuildOptions
{
    public required string DatasetPath { get; init; }
    public required string BenchResultsPath { get; init; }
    public required string ModelsConfigPath { get; init; }
    public required string PricingDirectory { get; init; }

    /// <summary>Pricing snapshot date to pin (e.g. "2026-08-15"); null = latest available snapshot.</summary>
    public string? PricingSnapshotDate { get; init; }

    /// <summary>Cluster count. Default 8 for the small bundled sample; the real target is 64 (research.md R4), sized for a much larger dataset under the n>=30-per-cell statistical-power constraint.</summary>
    public int K { get; init; } = 8;

    /// <summary>Relative quality band: keep candidates with quality >= qmax*(1-epsilon) (research.md R4 default).</summary>
    public double Epsilon { get; init; } = 0.05;

    /// <summary>Minimum dataset prompts a cluster must have before its policy row is trusted (else marked low_confidence).</summary>
    public int MinSamples { get; init; } = 5;

    public required string OutDirectory { get; init; }

    public string Version { get; init; } = "v1-2026-08-19";

    /// <summary>Representative per-request token profile used only to rank candidates by cost (ranking order is scale-invariant). Production would use each cluster's actual OmnisBench token profile (research.md R4) instead of one fixed profile.</summary>
    public int RepresentativeInputTokens { get; init; } = 1000;

    public int RepresentativeOutputTokens { get; init; } = 500;

    /// <summary>Fixed seed for k-means initial-centroid selection -- never unseeded, so the build is reproducible (T064).</summary>
    public int RandomSeed { get; init; } = 20260819;

    public static RoutingModelBuildOptions Defaults(string repoRoot) => new()
    {
        DatasetPath = Path.Combine(repoRoot, "routing", "datasets", "sample-prompts.jsonl"),
        BenchResultsPath = Path.Combine(repoRoot, "routing", "datasets", "sample-bench-results.json"),
        ModelsConfigPath = Path.Combine(repoRoot, "config", "models.yaml"),
        PricingDirectory = Path.Combine(repoRoot, "config", "pricing"),
        OutDirectory = Path.Combine(repoRoot, "routing"),
    };

    /// <summary>Applies <c>--flag value</c> pairs on top of <paramref name="baseOptions"/> (typically <see cref="Defaults"/>).</summary>
    public static RoutingModelBuildOptions Parse(IReadOnlyList<string> args, RoutingModelBuildOptions baseOptions)
    {
        var options = baseOptions;
        for (var i = 0; i < args.Count; i++)
        {
            var flag = args[i];

            string NextValue()
            {
                if (i + 1 >= args.Count)
                {
                    throw new ArgumentException($"Missing value for '{flag}'.");
                }

                i++;
                return args[i];
            }

            options = flag switch
            {
                "--dataset" => options with { DatasetPath = NextValue() },
                "--bench-results" => options with { BenchResultsPath = NextValue() },
                "--k" => options with { K = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--epsilon" => options with { Epsilon = double.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--min-samples" => options with { MinSamples = int.Parse(NextValue(), CultureInfo.InvariantCulture) },
                "--out" => options with { OutDirectory = NextValue() },
                "--version" => options with { Version = NextValue() },
                _ => throw new ArgumentException($"Unknown build-model flag '{flag}'."),
            };
        }

        return options;
    }
}

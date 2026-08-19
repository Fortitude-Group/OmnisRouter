using OmnisRouter.RoutingModel.Build.Build;
using OmnisRouter.RoutingModel.Build.Seed;

if (args.Length > 0 && string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
{
    var routingDir = ResolveRoutingDir();
    SeedModelGenerator.Generate(routingDir);
    Console.WriteLine($"Seed routing model written to {routingDir}");
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "build-model", StringComparison.OrdinalIgnoreCase))
{
    var repoRoot = ResolveRepoRoot();
    var baseOptions = RoutingModelBuildOptions.Defaults(repoRoot);
    var options = RoutingModelBuildOptions.Parse(args[1..], baseOptions);

    var result = RoutingModelBuilder.Run(options);

    Console.WriteLine($"Routing model '{result.Version}' written to {result.OutDirectory}");
    Console.WriteLine($"  centroids: {result.CentroidsPath}");
    Console.WriteLine($"  policy:    {result.PolicyPath}");
    Console.WriteLine($"  k={result.K} dim={result.Dim} prompts={result.PromptCount} low_confidence_clusters={result.LowConfidenceClusterCount}");
    return 0;
}

Console.WriteLine("Usage: dotnet run -- seed");
Console.WriteLine("       dotnet run -- build-model [--dataset path] [--bench-results path] [--k n] [--epsilon e] [--min-samples n] [--out dir] [--version ver]");
return 0;

// Walks up from the running assembly's location to find the repo root (marked by the .slnx),
// so this works the same whether invoked via `dotnet run` or a published exe from any cwd.
static string ResolveRoutingDir() => Path.Combine(ResolveRepoRoot(), "routing");

static string ResolveRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OmnisRouter.slnx")))
    {
        dir = dir.Parent;
    }

    if (dir is null)
    {
        throw new InvalidOperationException(
            $"Could not locate repository root (OmnisRouter.slnx) from '{AppContext.BaseDirectory}'.");
    }

    return dir.FullName;
}

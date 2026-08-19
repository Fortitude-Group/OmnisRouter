using OmnisRouter.RoutingModel.Build.Seed;

if (args.Length > 0 && string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
{
    var routingDir = ResolveRoutingDir();
    SeedModelGenerator.Generate(routingDir);
    Console.WriteLine($"Seed routing model written to {routingDir}");
    return 0;
}

Console.WriteLine("Usage: dotnet run -- seed");
return 0;

// Walks up from the running assembly's location to find the repo root (marked by the .slnx),
// so this works the same whether invoked via `dotnet run` or a published exe from any cwd.
static string ResolveRoutingDir()
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

    return Path.Combine(dir.FullName, "routing");
}

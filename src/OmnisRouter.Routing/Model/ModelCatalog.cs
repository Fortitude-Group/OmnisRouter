using OmnisRouter.Core.Model;
using YamlDotNet.Serialization;

namespace OmnisRouter.Routing.Model;

/// <summary>The configured candidate model pool, loaded from <c>config/models.yaml</c>.</summary>
public sealed class ModelCatalog
{
    public required IReadOnlyList<ModelRef> Models { get; init; }

    public static ModelCatalog LoadFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        var file = deserializer.Deserialize<ModelsFile>(yaml)
                   ?? throw new InvalidDataException($"Empty models file {path}.");

        var models = new List<ModelRef>(file.Models.Count);
        foreach (var entry in file.Models)
        {
            if (!Enum.TryParse<Provider>(entry.Provider, ignoreCase: true, out var provider))
            {
                throw new InvalidDataException($"Unknown provider '{entry.Provider}' in {path}.");
            }

            models.Add(new ModelRef(provider, entry.ModelId)
            {
                Capabilities = MapCapabilities(entry.Capabilities),
            });
        }

        return new ModelCatalog { Models = models };
    }

    private static ModelCapabilities MapCapabilities(CapabilityFlags? flags)
    {
        var caps = ModelCapabilities.Streaming;
        if (flags is null)
        {
            return caps;
        }

        if (flags.Vision) caps |= ModelCapabilities.Vision;
        if (flags.Tools) caps |= ModelCapabilities.Tools;
        if (flags.Thinking) caps |= ModelCapabilities.Thinking;
        if (flags.Strict) caps |= ModelCapabilities.StrictSchema;
        if (flags.ParallelTools) caps |= ModelCapabilities.ParallelTools;
        if (flags.Cache) caps |= ModelCapabilities.PromptCachePin;
        return caps;
    }

    private sealed class ModelsFile
    {
        [YamlMember(Alias = "models")] public List<ModelEntry> Models { get; init; } = [];
    }

    private sealed class ModelEntry
    {
        [YamlMember(Alias = "provider")] public string Provider { get; init; } = "";
        [YamlMember(Alias = "model_id")] public string ModelId { get; init; } = "";
        [YamlMember(Alias = "capabilities")] public CapabilityFlags? Capabilities { get; init; }
    }

    private sealed class CapabilityFlags
    {
        [YamlMember(Alias = "vision")] public bool Vision { get; init; }
        [YamlMember(Alias = "tools")] public bool Tools { get; init; }
        [YamlMember(Alias = "thinking")] public bool Thinking { get; init; }
        [YamlMember(Alias = "strict")] public bool Strict { get; init; }
        [YamlMember(Alias = "parallel_tools")] public bool ParallelTools { get; init; }
        [YamlMember(Alias = "cache")] public bool Cache { get; init; }
    }
}

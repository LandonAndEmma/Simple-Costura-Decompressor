using System.Collections.Generic;
using System.IO;
using AsmResolver.DotNet;

namespace CosturaDecompressor;

public sealed class ExtractorNew
{
    private readonly ModuleDefinition _module;
    private readonly string _defaultOutputPath;

    public ExtractorNew(ModuleDefinition module)
    {
        _module = module;
        _defaultOutputPath = _module.GetOutputPath() ?? string.Empty;
    }

    /// <summary>Extracts resources to provided folder; falls back to default if empty.</summary>
    public void Run(string outputPath = "")
    {
        var target = string.IsNullOrWhiteSpace(outputPath) ? _defaultOutputPath : outputPath;
        
        if (!ExtractResources(out var resources))
        {
            Logger.Error("No Costura resources found");
            return;
        }

        SaveResources(resources, target);
    }

    private bool ExtractResources(out Dictionary<byte[], string> resources)
    {
        resources = new Dictionary<byte[], string>();

        if (_module.Resources.Count == 0)
            return false;

        foreach (var resource in _module.Resources)
        {
            if (!resource.IsEmbedded) continue;
            if (resource.Name.Length < 19) continue;
            var name = resource.Name?.ToString();
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("costura.") || !name.EndsWith(".compressed"))
                continue;

            name = name.Substring(8, name.LastIndexOf(".compressed") - 8);
            var data = resource.GetData();

            if (data != null)
            {
                resources.Add(data.Decompress(), name);
                Logger.Success($"Extracted {name}");
            }
        }

        return resources.Count != 0;
    }

    private void SaveResources(Dictionary<byte[], string> resources, string outputPath)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        foreach (var (data, name) in resources)
            File.WriteAllBytes(Path.Combine(outputPath, name), data);

        Logger.Info($"Saved {resources.Count} files to {outputPath}");
    }
}
using System.IO;
using System.IO.Compression;
using AsmResolver.DotNet;

namespace CosturaDecompressor;

public static class Extensions
{
    public static string? GetOutputPath(this ModuleDefinition module)
    {
        if (module.FilePath == null || module.Assembly == null)
            return null;
        var dir = Path.GetDirectoryName(module.FilePath) ?? string.Empty;
        return Path.Combine(dir, $"{module.Assembly.Name}-decompressed");
    }

    public static byte[] Decompress(this byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
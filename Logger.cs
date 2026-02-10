using System;
using System.Diagnostics;

namespace CosturaDecompressor;

public static class Logger
{
    public static void Info(string message) => Write("[i]", message);
    public static void Success(string message) => Write("[OK]", message);
    public static void Error(string message) => Write("[!]", message);

    private static void Write(string prefix, string message)
    {
        var text = $"{prefix} {message}";
        try { Console.WriteLine(text); } catch { /* UI scenario */ }
        Debug.WriteLine(text);
    }
}
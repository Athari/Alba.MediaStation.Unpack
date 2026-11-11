using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace MediaStation.Unpack.Tests.Integration.Infrastructure;

public static class TestDataConfig
{
    private static readonly Lazy<string?> Lazy101Dalmatians = new(() => ResolveGamePath(
        envVarName: "MEDIASTATION_TEST_101DALMATIANS",
        configKey: "101Dalmatians",
        signatureRelativePath: Path.Combine("DATA", "174.CXT")));

    private static readonly Lazy<string?> LazyLionKing = new(() => ResolveGamePath(
        envVarName: "MEDIASTATION_TEST_LIONKING",
        configKey: "LionKing",
        signatureRelativePath: Path.Combine("DATA", "218.CXT")));

    public static string? Dalmatians101Path => Lazy101Dalmatians.Value;
    public static string? LionKingPath => LazyLionKing.Value;

    public static void Skip(string reason) => Xunit.Skip.If(true, reason);

    public static string Require101Dalmatians()
    {
        if (string.IsNullOrWhiteSpace(Dalmatians101Path) || !Directory.Exists(Dalmatians101Path))
        {
            Skip("101 Dalmatians game data was not found. Set MEDIASTATION_TEST_101DALMATIANS or specify '101Dalmatians' in testsettings.local.json.");
        }
        return Dalmatians101Path!;
    }

    public static string RequireLionKing()
    {
        if (string.IsNullOrWhiteSpace(LionKingPath) || !Directory.Exists(LionKingPath))
        {
            Skip("The Lion King game data was not found. Set MEDIASTATION_TEST_LIONKING or specify 'LionKing' in testsettings.local.json.");
        }
        return LionKingPath!;
    }

    private static string? ResolveGamePath(string envVarName, string configKey, string signatureRelativePath)
    {
        // 1. Environment variable
        string? envVal = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(envVal) && Directory.Exists(envVal))
        {
            return envVal;
        }

        // 2. Settings files: testsettings.local.json then testsettings.json
        string? configVal = TryReadFromSettingsFile("testsettings.local.json", configKey)
                         ?? TryReadFromSettingsFile("testsettings.json", configKey);
        if (!string.IsNullOrWhiteSpace(configVal) && Directory.Exists(configVal))
        {
            return configVal;
        }

        // 3. Auto-detect mounted drive signatures (e.g. Z:\, D:\, etc.)
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;

                string candidate = drive.RootDirectory.FullName;
                string sigPath = Path.Combine(candidate, signatureRelativePath);
                if (File.Exists(sigPath))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Ignore drive enumeration errors
        }

        return null;
    }

    private static string? TryReadFromSettingsFile(string filename, string configKey)
    {
        string? filePath = FindFileInOutputOrAncestors(filename);
        if (filePath == null || !File.Exists(filePath))
            return null;

        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("GameDataPaths", out var pathsProp) &&
                pathsProp.TryGetProperty(configKey, out var keyProp))
            {
                string? val = keyProp.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }
        }
        catch
        {
            // Ignore invalid JSON
        }

        return null;
    }

    private static string? FindFileInOutputOrAncestors(string filename)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, filename);
            if (File.Exists(candidate))
                return candidate;

            if (File.Exists(Path.Combine(current.FullName, "MediaStation.Unpack.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                string inIntegration = Path.Combine(current.FullName, "tests", "MediaStation.Unpack.Tests.Integration", filename);
                if (File.Exists(inIntegration))
                    return inIntegration;
                break;
            }

            current = current.Parent;
        }
        return null;
    }
}

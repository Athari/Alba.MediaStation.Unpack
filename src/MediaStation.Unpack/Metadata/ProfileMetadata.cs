namespace MediaStation.Unpack.Metadata;

public class ProfileMetadata
{
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Asset ID -> Asset Name (e.g. 278 -> "image_6c14_Human1", 234 -> "spritemovie_6c14_Door3")
    /// </summary>
    public Dictionary<uint, string> Assets { get; } = new();

    /// <summary>
    /// ParamToken ID -> ParamToken Name (e.g. 10024 -> "$open", 10020 -> "$confetti")
    /// </summary>
    public Dictionary<uint, string> ParamTokens { get; } = new();

    /// <summary>
    /// File ID -> File Name (e.g. 100 -> "3664.cxt")
    /// </summary>
    public Dictionary<uint, string> Files { get; } = new();

    /// <summary>
    /// Variable ID -> Variable Name
    /// </summary>
    public Dictionary<uint, string> Variables { get; } = new();

    /// <summary>
    /// Context declarations
    /// </summary>
    public List<(string Type, string Name, uint Unk1)> Contexts { get; } = new();

    public string? GetAssetName(uint id)
    {
        return Assets.TryGetValue(id, out var name) ? name : null;
    }

    public string? GetClipName(uint id)
    {
        if (ParamTokens.TryGetValue(id, out var name))
        {
            return name.TrimStart('$');
        }
        return null;
    }

    public static ProfileMetadata? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var profile = new ProfileMetadata();
        string text = File.ReadAllText(filePath);
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        int section = 0; // 0 = Contexts, 1 = Assets, 2 = Files, 3 = Variables, 4 = ParamTokens, 5 = Constants
        bool versionRead = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (!versionRead)
            {
                // First non-empty line is version/platform info (e.g. "_Version3.3_ _MAC_")
                versionRead = true;
                var verParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (verParts.Length > 0) profile.Version = verParts[0];
                if (verParts.Length > 1) profile.Platform = verParts[1];
                continue;
            }

            if (line == "!")
            {
                section++;
                continue;
            }

            // Skip summary lines and image set declarations
            if (line.StartsWith('*') || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            switch (section)
            {
                case 0: // Contexts: <type> <name> <unk1>
                    if (parts.Length >= 2)
                    {
                        uint unk1 = parts.Length >= 3 && uint.TryParse(parts[2], out var u) ? u : 0;
                        profile.Contexts.Add((parts[0], parts[1], unk1));
                    }
                    break;

                case 1: // Assets: <name> <id> [chunkIds...]
                    if (uint.TryParse(parts[1], out uint assetId))
                    {
                        profile.Assets[assetId] = parts[0];
                    }
                    break;

                case 2: // Files: "<filename>" <id>
                    if (uint.TryParse(parts[1], out uint fileId))
                    {
                        profile.Files[fileId] = parts[0].Trim('"');
                    }
                    break;

                case 3: // Variables: <name> <id>
                    if (uint.TryParse(parts[1], out uint varId))
                    {
                        profile.Variables[varId] = parts[0];
                    }
                    break;

                case 4: // ParamTokens (Clips, commands): <name> <id>
                    if (uint.TryParse(parts[1], out uint tokenId))
                    {
                        profile.ParamTokens[tokenId] = parts[0];
                    }
                    break;

                default:
                    // Other sections (e.g. constants)
                    break;
            }
        }

        return profile;
    }
}

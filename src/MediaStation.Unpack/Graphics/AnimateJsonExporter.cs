using System.Text.Json;
using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Model;

namespace MediaStation.Unpack.Graphics;

public class AnimateJsonExportOptions
{
    public double MasterFrameRate { get; set; } = 60.0;

    // TODO: Maybe expose in CLI if needed for strict integer-only external importers
    public bool RoundKeyframeIndices { get; set; } = false;

    public int DefaultStageWidth { get; set; } = 640;
    public int DefaultStageHeight { get; set; } = 480;
}

public static class AnimateJsonExporter
{
    private static readonly string[] MoviePrefixes = ["movie_", "mov_"];
    private static readonly string[] SpritePrefixes = ["spritemovie_", "sprite_"];

    public static void ExportContext(
        MediaStationContext context,
        Palette palette,
        string contextOutDir,
        AnimateJsonExportOptions? options = null,
        Func<uint, string?>? getParamTokenName = null,
        Action<string>? logger = null)
    {
        options ??= new AnimateJsonExportOptions();

        var validMovies = context.Movies
            .Where(m => m.Frames.Count > 0 && m.Images.Count > 0)
            .ToList();

        var validSprites = context.Sprites
            .Where(s => s.Frames.Count > 0)
            .ToList();

        if (validMovies.Count == 0 && validSprites.Count == 0)
            return;

        string spritesDir = Path.Combine(contextOutDir, "sprites");
        Directory.CreateDirectory(spritesDir);

        var symbolsList = new List<Dictionary<string, object?>>();
        var masterLayers = new List<Dictionary<string, object?>>();

        // 1. Export Movies
        foreach (var movie in validMovies)
        {
            movie.DecompressAllFrames();

            string movieName = !string.IsNullOrWhiteSpace(movie.Name)
                ? CleanAssetName(movie.Name, MoviePrefixes)
                : $"{movie.Id}";
            string symName = $"Movie_{movieName}";

            var imageMap = new Dictionary<uint, PixMapImage>();
            foreach (var img in movie.Images)
            {
                imageMap.TryAdd(img.Index, img.Image);
            }

            // Export unique PNG frame assets
            var usedImageIndices = movie.Frames.Select(f => f.Index).Distinct();
            foreach (uint imgIdx in usedImageIndices)
            {
                if (imageMap.TryGetValue(imgIdx, out var pixmap) && pixmap.Width > 0 && pixmap.Height > 0)
                {
                    string imgBaseName = $"{symName}_img_{imgIdx:D4}";
                    string imgPath = Path.Combine(spritesDir, $"{imgBaseName}.png");
                    if (!File.Exists(imgPath))
                    {
                        ImageExporter.SaveImage(pixmap, palette, imgPath, GraphicFormat.Png);
                    }
                }
            }

            // Export synchronized audio if present
            if (movie.Audio.HasAudio)
            {
                string wavPath = Path.Combine(spritesDir, $"{symName}.wav");
                if (!File.Exists(wavPath))
                {
                    movie.Audio.SaveAsWav(wavPath);
                }
            }

            double nativeFps = DetermineMovieNativeFps(movie);
            var (movieSymbol, movieDuration) = BuildMovieSymbol(symName, movie, imageMap, nativeFps, options);
            symbolsList.Add(movieSymbol);

            masterLayers.Add(BuildMasterLayer(symName, movieDuration));
        }

        // 2. Export Sprites & Sub-Clips
        foreach (var sprite in validSprites)
        {
            string spriteName = !string.IsNullOrWhiteSpace(sprite.Name)
                ? CleanAssetName(sprite.Name, SpritePrefixes)
                : $"{sprite.Id}";
            string masterSymName = $"Sprite_{spriteName}";

            // Export unique PNG frame assets
            foreach (var frame in sprite.Frames)
            {
                if (frame.Pixmap.Width > 0 && frame.Pixmap.Height > 0)
                {
                    string imgBaseName = $"{masterSymName}_img_{frame.Index:D4}";
                    string imgPath = Path.Combine(spritesDir, $"{imgBaseName}.png");
                    if (!File.Exists(imgPath))
                    {
                        ImageExporter.SaveImage(frame.Pixmap, palette, imgPath, GraphicFormat.Png);
                    }
                }
            }

            double spriteFps = sprite.FrameRate > 0 ? sprite.FrameRate : 12.0;

            // Master Sprite Symbol
            var (masterSpriteSymbol, spriteDuration) = BuildSpriteSymbol(masterSymName, sprite.Frames, spriteFps, masterSymName, options);
            symbolsList.Add(masterSpriteSymbol);
            masterLayers.Add(BuildMasterLayer(masterSymName, spriteDuration));

            // Named Sub-Clips
            foreach (var clip in sprite.Clips.Values)
            {
                if (clip.Id == SpriteMovieActor.DefaultForwardClipId || clip.Id == SpriteMovieActor.DefaultBackwardClipId)
                    continue;

                int first = Math.Clamp(clip.FirstFrameIndex, 0, sprite.Frames.Count - 1);
                int last = Math.Clamp(clip.LastFrameIndex, 0, sprite.Frames.Count - 1);

                var clipFrames = new List<(PixMapImage Pixmap, Point Origin, uint Index)>();
                if (first <= last)
                {
                    for (int f = first; f <= last; f++)
                        clipFrames.Add(sprite.Frames[f]);
                }
                else
                {
                    for (int f = first; f >= last; f--)
                        clipFrames.Add(sprite.Frames[f]);
                }

                if (clipFrames.Count > 0)
                {
                    string? tokenName = getParamTokenName?.Invoke(clip.Id);
                    string clipName = !string.IsNullOrWhiteSpace(tokenName)
                        ? CleanClipName(tokenName)
                        : $"Clip{clip.Id:X}";

                    string clipSymName = $"{masterSymName}_{clipName}";
                    var (clipSymbol, _) = BuildSpriteSymbol(clipSymName, clipFrames, spriteFps, masterSymName, options);
                    symbolsList.Add(clipSymbol);
                }
            }
        }

        // Determine stage dimensions
        int stageWidth = options.DefaultStageWidth;
        int stageHeight = options.DefaultStageHeight;
        int maxActorW = context.Actors.Values.Select(a => a.BoundingBox.Width).DefaultIfEmpty(0).Max();
        int maxActorH = context.Actors.Values.Select(a => a.BoundingBox.Height).DefaultIfEmpty(0).Max();
        if (maxActorW > 0 && maxActorH > 0)
        {
            stageWidth = maxActorW;
            stageHeight = maxActorH;
        }

        string animName = string.IsNullOrWhiteSpace(context.Name)
            ? $"Context_{context.ContextId:D3}"
            : $"Context_{context.ContextId:D3}_{SanitizeFileName(context.Name)}";

        var root = new Dictionary<string, object?>
        {
            ["metadata"] = new Dictionary<string, object?>
            {
                ["framerate"] = options.MasterFrameRate,
                ["version"] = "1.0",
                ["width"] = stageWidth,
                ["height"] = stageHeight
            },
            ["ANIMATION"] = new Dictionary<string, object?>
            {
                ["name"] = animName,
                ["TIMELINE"] = new Dictionary<string, object?>
                {
                    ["LAYERS"] = masterLayers
                }
            },
            ["SYMBOL_DICTIONARY"] = new Dictionary<string, object?>
            {
                ["Symbols"] = symbolsList
            }
        };

        string jsonPath = Path.Combine(contextOutDir, "Animation.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonText = JsonSerializer.Serialize(root, jsonOptions);
        File.WriteAllText(jsonPath, jsonText);

        logger?.Invoke($"Exported Adobe Animate JSON package to '{jsonPath}' ({symbolsList.Count} symbols).");
    }

    private static (Dictionary<string, object?> Symbol, double Duration) BuildMovieSymbol(
        string symName,
        StreamMovieActor movie,
        Dictionary<uint, PixMapImage> imageMap,
        double nativeFps,
        AnimateJsonExportOptions options)
    {
        var framesByLayer = movie.Frames
            .Where(f => f.EndInMilliseconds > f.StartInMilliseconds && imageMap.ContainsKey(f.Index) && imageMap[f.Index].Width > 0 && imageMap[f.Index].Height > 0)
            .GroupBy(f => f.LayerId)
            // Visual order: lower ZIndex (foreground) first, higher ZIndex (background) last
            .OrderBy(g => g.Min(f => f.ZIndex))
            .ToList();

        var layersList = new List<Dictionary<string, object?>>();
        double maxFrameEnd = 1.0;

        foreach (var layerGroup in framesByLayer)
        {
            uint layerId = layerGroup.Key;
            var orderedFrames = layerGroup
                .OrderBy(f => f.StartInMilliseconds)
                .ThenBy(f => f.ZIndex)
                .ToList();

            var keyframesList = new List<Dictionary<string, object?>>();

            foreach (var frame in orderedFrames)
            {
                double exactStart = frame.StartInMilliseconds * options.MasterFrameRate / 1000.0;
                double exactEnd = frame.EndInMilliseconds * options.MasterFrameRate / 1000.0;
                double exactDuration = Math.Max(1.0, exactEnd - exactStart);

                if (exactEnd > maxFrameEnd)
                    maxFrameEnd = exactEnd;

                object indexVal = FormatNumber(exactStart, options.RoundKeyframeIndices);
                object durationVal = FormatNumber(exactDuration, options.RoundKeyframeIndices, minInt: 1);

                string spriteRefName = $"{symName}_img_{frame.Index:D4}";

                var element = new Dictionary<string, object?>
                {
                    ["ATLAS_SPRITE_instance"] = new Dictionary<string, object?>
                    {
                        ["name"] = spriteRefName,
                        ["Matrix3D"] = CreateMatrix3D(frame.LeftTop.X, frame.LeftTop.Y),
                        ["Position"] = new Dictionary<string, object?>
                        {
                            ["x"] = 0.0,
                            ["y"] = 0.0
                        }
                    }
                };

                keyframesList.Add(new Dictionary<string, object?>
                {
                    ["index"] = indexVal,
                    ["duration"] = durationVal,
                    ["elements"] = new List<object?> { element }
                });
            }

            layersList.Add(new Dictionary<string, object?>
            {
                ["Layer_name"] = $"Layer_{layerId}",
                ["Layer_type"] = "Normal",
                ["Frames"] = keyframesList
            });
        }

        var symbol = new Dictionary<string, object?>
        {
            ["SYMBOL_name"] = symName,
            ["framerate"] = nativeFps,
            ["TIMELINE"] = new Dictionary<string, object?>
            {
                ["LAYERS"] = layersList
            }
        };

        return (symbol, maxFrameEnd);
    }

    private static (Dictionary<string, object?> Symbol, double Duration) BuildSpriteSymbol(
        string symName,
        IReadOnlyList<(PixMapImage Pixmap, Point Origin, uint Index)> frames,
        double spriteFps,
        string assetPrefix,
        AnimateJsonExportOptions options)
    {
        var keyframesList = new List<Dictionary<string, object?>>();
        double stepFrames = options.MasterFrameRate / spriteFps;

        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            double exactStart = i * stepFrames;
            double exactDuration = stepFrames;

            object indexVal = FormatNumber(exactStart, options.RoundKeyframeIndices);
            object durationVal = FormatNumber(exactDuration, options.RoundKeyframeIndices, minInt: 1);

            string spriteRefName = $"{assetPrefix}_img_{frame.Index:D4}";

            var element = new Dictionary<string, object?>
            {
                ["ATLAS_SPRITE_instance"] = new Dictionary<string, object?>
                {
                    ["name"] = spriteRefName,
                    ["Matrix3D"] = CreateMatrix3D(frame.Origin.X, frame.Origin.Y),
                    ["Position"] = new Dictionary<string, object?>
                    {
                        ["x"] = 0.0,
                        ["y"] = 0.0
                    }
                }
            };

            keyframesList.Add(new Dictionary<string, object?>
            {
                ["index"] = indexVal,
                ["duration"] = durationVal,
                ["elements"] = new List<object?> { element }
            });
        }

        double totalDuration = Math.Max(1.0, frames.Count * stepFrames);

        var layersList = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["Layer_name"] = "Layer 1",
                ["Layer_type"] = "Normal",
                ["Frames"] = keyframesList
            }
        };

        var symbol = new Dictionary<string, object?>
        {
            ["SYMBOL_name"] = symName,
            ["framerate"] = spriteFps,
            ["TIMELINE"] = new Dictionary<string, object?>
            {
                ["LAYERS"] = layersList
            }
        };

        return (symbol, totalDuration);
    }

    private static Dictionary<string, object?> BuildMasterLayer(string symName, double duration)
    {
        var siElement = new Dictionary<string, object?>
        {
            ["SYMBOL_Instance"] = new Dictionary<string, object?>
            {
                ["SYMBOL_name"] = symName,
                ["Instance_Name"] = $"inst_{symName}",
                ["symbolType"] = "G",
                ["firstFrame"] = 0,
                ["loop"] = "LP",
                ["transformationPoint"] = new Dictionary<string, object?>
                {
                    ["x"] = 0.0,
                    ["y"] = 0.0
                },
                ["Matrix3D"] = CreateMatrix3D(0, 0)
            }
        };

        var frame = new Dictionary<string, object?>
        {
            ["index"] = 0,
            ["duration"] = FormatNumber(duration, roundToInt: false, minInt: 1),
            ["elements"] = new List<object?> { siElement }
        };

        return new Dictionary<string, object?>
        {
            ["Layer_name"] = $"Layer_{symName}",
            ["Layer_type"] = "Normal",
            ["Frames"] = new List<object?> { frame }
        };
    }

    private static double[] CreateMatrix3D(double tx, double ty)
    {
        // 4x4 Column-Major:
        // [ m0, m1, m2, m3,   m4, m5, m6, m7,   m8, m9, m10, m11,   m12, m13, m14, m15 ]
        // [  a,  b,  0,  0,    c,  d,  0,  0,    0,  0,   1,   0,    tx,  ty,   0,   1 ]
        return [
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            tx,  ty,  0.0, 1.0
        ];
    }

    private static object FormatNumber(double val, bool roundToInt, int minInt = 0)
    {
        if (roundToInt)
        {
            int rounded = (int)Math.Round(val);
            return Math.Max(minInt, rounded);
        }

        if (val == Math.Floor(val))
        {
            int intVal = (int)val;
            return Math.Max(minInt, intVal);
        }

        return Math.Round(val, 3);
    }

    private static double DetermineMovieNativeFps(StreamMovieActor movie)
    {
        if (movie.FrameRate > 0)
            return movie.FrameRate;

        uint minDurationMs = uint.MaxValue;
        foreach (var f in movie.Frames)
        {
            if (f.EndInMilliseconds > f.StartInMilliseconds)
            {
                uint dur = f.EndInMilliseconds - f.StartInMilliseconds;
                if (dur > 0 && dur < minDurationMs)
                {
                    minDurationMs = dur;
                }
            }
        }

        if (minDurationMs == uint.MaxValue || minDurationMs == 0)
            return 10.0;

        double calculatedFps = 1000.0 / minDurationMs;
        // Check standard common animation rates
        if (Math.Abs(calculatedFps - 10.0) < 0.2) return 10.0;
        if (Math.Abs(calculatedFps - 5.0) < 0.2) return 5.0;
        if (Math.Abs(calculatedFps - 15.0) < 0.5) return 15.0;
        if (Math.Abs(calculatedFps - 12.0) < 0.5) return 12.0;
        if (Math.Abs(calculatedFps - 20.0) < 0.5) return 20.0;
        if (Math.Abs(calculatedFps - 24.0) < 0.5) return 24.0;
        if (Math.Abs(calculatedFps - 30.0) < 0.5) return 30.0;

        return Math.Round(calculatedFps, 2);
    }

    public static string CleanAssetName(string rawName, string[] prefixesToRemove)
    {
        string name = SanitizeFileName(rawName);
        foreach (var prefix in prefixesToRemove)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string stripped = name[prefix.Length..];
                if (!string.IsNullOrWhiteSpace(stripped))
                    return stripped;
                break;
            }
        }
        return name;
    }

    public static string CleanClipName(string rawName)
    {
        string name = SanitizeFileName(rawName.TrimStart('$'));
        return string.IsNullOrWhiteSpace(name) ? rawName : name;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}

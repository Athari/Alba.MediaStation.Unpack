using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;

namespace MediaStation.Unpack.Model;

public class ExtractOptions
{
    public string OutputPath { get; set; } = "extracted";
    public bool ExtractImages { get; set; } = true;
    public bool ExtractSprites { get; set; } = true;
    public bool ExtractMovies { get; set; } = true;
    public bool ExtractAudio { get; set; } = false;
    public ushort? ContextFilter { get; set; }
    public bool Verbose { get; set; }
}

public class Extractor
{
    private readonly MediaStationTitle _title;
    private readonly ExtractOptions _options;
    private readonly Action<string> _logger;

    public Extractor(MediaStationTitle title, ExtractOptions options, Action<string>? logger = null)
    {
        _title = title;
        _options = options;
        _logger = logger ?? Console.WriteLine;
    }

    public void Run()
    {
        _logger($"Starting extraction for '{_title.GameTitle}' (Version {_title.Version.Major}.{_title.Version.Minor}r{_title.Version.Patch})");
        Directory.CreateDirectory(_options.OutputPath);

        var contextIds = _title.ContextReferences.Keys.OrderBy(k => k).ToList();
        if (_options.ContextFilter.HasValue)
        {
            contextIds = contextIds.Where(k => k == _options.ContextFilter.Value).ToList();
            if (contextIds.Count == 0)
            {
                _logger($"Warning: Context {_options.ContextFilter.Value} was not found in manifest.");
                return;
            }
        }

        foreach (ushort contextId in contextIds)
        {
            if (_options.Verbose)
            {
                _logger($"Loading context {contextId}...");
            }
            ExtractContext(contextId);
        }

        _logger("Extraction completed successfully.");
    }

    private void ExtractContext(ushort contextId)
    {
        var context = _title.LoadContext(contextId);
        string contextDirName = string.IsNullOrWhiteSpace(context.Name)
            ? $"Context_{contextId:D3}"
            : $"Context_{contextId:D3}_{SanitizeFileName(context.Name)}";

        string contextOutDir = Path.Combine(_options.OutputPath, contextDirName);
        var palette = context.ResolveActivePalette();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int imageCount = 0;
        int spriteCount = 0;
        int movieCount = 0;
        int soundCount = 0;

        // 1. Static Images
        if (_options.ExtractImages)
        {
            foreach (var imgActor in context.Images)
            {
                if (imgActor.Bitmap != null)
                {
                    string baseName = !string.IsNullOrWhiteSpace(imgActor.Name)
                        ? $"Image_{CleanAssetName(imgActor.Name, ImagePrefixes)}"
                        : $"Image_{imgActor.Id}";
                    string filePath = GetUniqueFilePath(usedPaths, contextOutDir, baseName, imgActor.Id, "png", _logger);
                    ImageExporter.SaveImage(imgActor.Bitmap, palette, filePath);
                    imageCount++;
                }
            }

            foreach (var diskImg in context.DiskImages)
            {
                var composite = diskImg.CompositeImage();
                if (composite != null)
                {
                    string baseName = !string.IsNullOrWhiteSpace(diskImg.Name)
                        ? $"Image_{CleanAssetName(diskImg.Name, ImagePrefixes)}"
                        : $"DiskImage_{diskImg.Id}";
                    string filePath = GetUniqueFilePath(usedPaths, contextOutDir, baseName, diskImg.Id, "png", _logger);
                    ImageExporter.SaveImage(composite, palette, filePath);
                    imageCount++;
                }
            }
        }

        // 2. Sprites
        if (_options.ExtractSprites)
        {
            foreach (var sprite in context.Sprites)
            {
                if (sprite.Frames.Count > 0)
                {
                    string spriteName = !string.IsNullOrWhiteSpace(sprite.Name)
                        ? CleanAssetName(sprite.Name, SpritePrefixes)
                        : $"{sprite.Id}";

                    // Save master animation (all frames)
                    var masterFrames = sprite.Frames.Select(f => (f.Pixmap, f.Origin)).ToList();
                    string masterBase = $"Sprite_{spriteName}";
                    string masterPath = GetUniqueFilePath(usedPaths, contextOutDir, masterBase, sprite.Id, "png", _logger);
                    ImageExporter.SaveSpriteApng(masterFrames, palette, (int)sprite.FrameRate, masterPath);
                    spriteCount++;

                    // Save custom sub-clips if any
                    foreach (var clip in sprite.Clips.Values)
                    {
                        if (clip.Id == SpriteMovieActor.DefaultForwardClipId || clip.Id == SpriteMovieActor.DefaultBackwardClipId)
                            continue;

                        int first = Math.Clamp(clip.FirstFrameIndex, 0, sprite.Frames.Count - 1);
                        int last = Math.Clamp(clip.LastFrameIndex, 0, sprite.Frames.Count - 1);

                        var clipFrames = new List<(PixMapImage Pixmap, Point Origin)>();
                        if (first <= last)
                        {
                            for (int f = first; f <= last; f++)
                                clipFrames.Add((sprite.Frames[f].Pixmap, sprite.Frames[f].Origin));
                        }
                        else
                        {
                            for (int f = first; f >= last; f--)
                                clipFrames.Add((sprite.Frames[f].Pixmap, sprite.Frames[f].Origin));
                        }

                        if (clipFrames.Count > 0)
                        {
                            string? tokenName = _title.GetParamTokenName(clip.Id);
                            string clipName = !string.IsNullOrWhiteSpace(tokenName)
                                ? CleanClipName(tokenName)
                                : $"Clip{clip.Id:X}";

                            string clipBase = $"Sprite_{spriteName}_{clipName}";
                            string clipPath = GetUniqueFilePath(usedPaths, contextOutDir, clipBase, clip.Id, "png", _logger);
                            ImageExporter.SaveSpriteApng(clipFrames, palette, (int)sprite.FrameRate, clipPath);
                        }
                    }
                }
            }
        }

        // 3. Movies
        if (_options.ExtractMovies)
        {
            foreach (var movie in context.Movies)
            {
                string movieName = !string.IsNullOrWhiteSpace(movie.Name)
                    ? CleanAssetName(movie.Name, MoviePrefixes)
                    : $"{movie.Id}";
                string baseName = $"Movie_{movieName}";

                if (movie.Frames.Count > 0 && movie.Images.Count > 0)
                {
                    // Map index to MovieImageData (first image with matching index, per ScummVM)
                    var imageMap = new Dictionary<uint, PixMapImage>();
                    foreach (var img in movie.Images)
                    {
                        imageMap.TryAdd(img.Index, img.Image);
                    }

                    // Decompress keyframes and delta frames
                    foreach (var frame in movie.Frames)
                    {
                        if (!imageMap.TryGetValue(frame.Index, out var framePixmap))
                            continue;

                        // Check if delta frame
                        if (frame.KeyframeIndex != 0 && imageMap.TryGetValue(frame.KeyframeIndex, out var keyframePixmap))
                        {
                            // Ensure keyframe is decompressed
                            if (keyframePixmap.IsCompressed)
                                keyframePixmap.Decompress();

                            if (framePixmap.IsCompressed)
                            {
                                framePixmap.Decompress(
                                    keyframePixmap.Pixels,
                                    keyframePixmap.Width,
                                    keyframePixmap.Height,
                                    frame.DiffBetweenKeyframeAndFrame);
                            }
                        }
                        else
                        {
                            if (framePixmap.IsCompressed)
                                framePixmap.Decompress();
                        }
                    }

                    var frameItems = new List<MovieFrameItem>();
                    for (int i = 0; i < movie.Frames.Count; i++)
                    {
                        var frame = movie.Frames[i];
                        if (imageMap.TryGetValue(frame.Index, out var framePixmap))
                        {
                            frameItems.Add(new MovieFrameItem
                            {
                                Pixmap = framePixmap,
                                Origin = frame.LeftTop,
                                StartTimeMs = frame.StartInMilliseconds,
                                EndTimeMs = frame.EndInMilliseconds,
                                ZIndex = frame.ZIndex,
                                Order = i
                            });
                        }
                    }

                    if (frameItems.Count > 0)
                    {
                        string moviePath = GetUniqueFilePath(usedPaths, contextOutDir, baseName, movie.Id, "png", _logger);
                        ImageExporter.SaveMovieApng(frameItems, palette, moviePath);
                        movieCount++;
                    }

                    // Check if multiple layers exist
                    var distinctLayers = movie.Frames.Select(f => f.LayerId).Distinct().ToList();
                    if (distinctLayers.Count > 1)
                    {
                        foreach (uint layerId in distinctLayers)
                        {
                            var layerFrames = new List<MovieFrameItem>();
                            for (int i = 0; i < movie.Frames.Count; i++)
                            {
                                var frame = movie.Frames[i];
                                if (frame.LayerId == layerId && imageMap.TryGetValue(frame.Index, out var framePixmap))
                                {
                                    layerFrames.Add(new MovieFrameItem
                                    {
                                        Pixmap = framePixmap,
                                        Origin = frame.LeftTop,
                                        StartTimeMs = frame.StartInMilliseconds,
                                        EndTimeMs = frame.EndInMilliseconds,
                                        ZIndex = frame.ZIndex,
                                        Order = i
                                    });
                                }
                            }

                            if (layerFrames.Count > 0)
                            {
                                string layerBase = $"{baseName}_Layer{layerId}";
                                string layerPath = GetUniqueFilePath(usedPaths, contextOutDir, layerBase, layerId, "png", _logger);
                                ImageExporter.SaveMovieApng(layerFrames, palette, layerPath);
                            }
                        }
                    }
                }

                // Movie synchronized audio
                if (movie.Audio.HasAudio)
                {
                    string wavPath = GetUniqueFilePath(usedPaths, contextOutDir, baseName, movie.Id, "wav", _logger);
                    movie.Audio.SaveAsWav(wavPath);
                    soundCount++;
                }
            }
        }

        // 4. Standalone Sounds
        if (_options.ExtractAudio)
        {
            foreach (var sound in context.Sounds)
            {
                if (sound.Sequence.HasAudio)
                {
                    string soundName = !string.IsNullOrWhiteSpace(sound.Name)
                        ? CleanAssetName(sound.Name, SoundPrefixes)
                        : $"{sound.Id}";
                    string baseName = $"Sound_{soundName}";
                    string filePath = GetUniqueFilePath(usedPaths, contextOutDir, baseName, sound.Id, "wav", _logger);
                    sound.Sequence.SaveAsWav(filePath);
                    soundCount++;
                }
            }
        }

        if (_options.Verbose || (imageCount + spriteCount + movieCount + soundCount > 0))
        {
            _logger($"[{contextDirName}] Extracted: {imageCount} images, {spriteCount} sprites, {movieCount} movies, {soundCount} audio tracks.");
        }
    }

    private static readonly string[] ImagePrefixes = ["image_", "img_"];
    private static readonly string[] SpritePrefixes = ["spritemovie_", "sprite_"];
    private static readonly string[] MoviePrefixes = ["movie_", "mov_"];
    private static readonly string[] SoundPrefixes = ["sound_", "snd_"];

    private static string CleanAssetName(string rawName, string[] prefixesToRemove)
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

    private static string CleanClipName(string rawName)
    {
        string name = SanitizeFileName(rawName.TrimStart('$'));
        return string.IsNullOrWhiteSpace(name) ? rawName : name;
    }

    private static string GetUniqueFilePath(
        HashSet<string> usedPaths,
        string outDir,
        string baseName,
        uint id,
        string extension,
        Action<string>? logger = null)
    {
        string candidate = $"{baseName}.{extension}";
        string fullPath = Path.Combine(outDir, candidate);
        if (usedPaths.Add(fullPath))
        {
            return fullPath;
        }

        // Collision detected: append integer ID to disambiguate
        string candidateWithId = $"{baseName}_{id}.{extension}";
        fullPath = Path.Combine(outDir, candidateWithId);
        if (usedPaths.Add(fullPath))
        {
            logger?.Invoke($"Disambiguating duplicate filename '{candidate}' -> '{candidateWithId}'");
            return fullPath;
        }

        int counter = 2;
        while (true)
        {
            string candidateNumbered = $"{baseName}_{id}_{counter}.{extension}";
            fullPath = Path.Combine(outDir, candidateNumbered);
            if (usedPaths.Add(fullPath))
            {
                logger?.Invoke($"Disambiguating duplicate filename '{candidate}' -> '{candidateNumbered}'");
                return fullPath;
            }
            counter++;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}

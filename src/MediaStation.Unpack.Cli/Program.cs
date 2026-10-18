using System.CommandLine;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Model;

namespace MediaStation.Unpack.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var pathArgument = new Argument<string>("path")
        {
            Description = "Path to the MediaStation game directory, BOOT.STM file, or context file (.CXT)"
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Destination directory for extracted assets",
            Aliases = { "-o" }
        };

        var extractOption = new Option<string>("--extract")
        {
            Description = "Asset types to extract: 'graphics' (default: images, sprites, movies), 'all' (graphics + audio), or comma-separated ('images', 'sprites', 'movies', 'audio')",
            Aliases = { "-e" }
        };

        var imageFormatOption = new Option<string>("--image-format")
        {
            Description = "Static image formats: comma-separated list ('png', 'webp', 'gif'). Default: png",
            Aliases = { "-if" }
        };

        var animationFormatOption = new Option<string>("--animation-format")
        {
            Description = "Animation formats: comma-separated list ('png', 'webp', 'gif', 'json-animate'). Default: png",
            Aliases = { "-af" }
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose output logging",
            Aliases = { "-v" }
        };

        var rootCommand = new RootCommand("Extracts graphics (PNG/APNG/WebP/GIF) and audio (WAV) from MediaStation game archives (.STM, .CXT)")
        {
            pathArgument,
            outputOption,
            extractOption,
            imageFormatOption,
            animationFormatOption,
            verboseOption
        };

        rootCommand.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(pathArgument);
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("Error: A path to a game directory, BOOT.STM file, or context file (.CXT) is required.");
                return 1;
            }

            string output = parseResult.GetValue(outputOption) ?? "extracted";
            string extractStr = parseResult.GetValue(extractOption) ?? "graphics";
            string imageFormatStr = parseResult.GetValue(imageFormatOption) ?? "png";
            string animFormatStr = parseResult.GetValue(animationFormatOption) ?? "png";
            bool verbose = parseResult.GetValue(verboseOption);

            var options = ParseExtractOptions(extractStr);
            options.OutputPath = Path.GetFullPath(output);
            options.Verbose = verbose;
            options.ImageFormats = ParseFormats(imageFormatStr);
            options.AnimationFormats = ParseFormats(animFormatStr);

            try
            {
                var title = new MediaStationTitle(path);
                title.LoadManifest();

                var extractor = new Extractor(title, options, Console.WriteLine);
                extractor.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during extraction: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.ToString());
                }
                return 1;
            }
        });

        var result = rootCommand.Parse(args);
        return result.Invoke();
    }

    private static List<GraphicFormat> ParseFormats(string formatStr)
    {
        var list = new List<GraphicFormat>();
        var parts = formatStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "png" or "apng":
                    if (!list.Contains(GraphicFormat.Png)) list.Add(GraphicFormat.Png);
                    break;
                case "webp":
                    if (!list.Contains(GraphicFormat.Webp)) list.Add(GraphicFormat.Webp);
                    break;
                case "gif":
                    if (!list.Contains(GraphicFormat.Gif)) list.Add(GraphicFormat.Gif);
                    break;
                case "json-animate" or "animate-json" or "animate" or "json":
                    if (!list.Contains(GraphicFormat.JsonAnimate)) list.Add(GraphicFormat.JsonAnimate);
                    break;
            }
        }
        return list.Count > 0 ? list : [GraphicFormat.Png];
    }

    private static ExtractOptions ParseExtractOptions(string extractStr)
    {
        var options = new ExtractOptions();
        string normalized = extractStr.Trim().ToLowerInvariant();

        if (normalized is "all")
        {
            options.ExtractImages = true;
            options.ExtractSprites = true;
            options.ExtractMovies = true;
            options.ExtractAudio = true;
            return options;
        }

        if (normalized is "graphics")
        {
            options.ExtractImages = true;
            options.ExtractSprites = true;
            options.ExtractMovies = true;
            options.ExtractAudio = false;
            return options;
        }

        if (normalized is "audio")
        {
            options.ExtractImages = false;
            options.ExtractSprites = false;
            options.ExtractMovies = false;
            options.ExtractAudio = true;
            return options;
        }

        // Custom comma-separated list
        var parts = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        options.ExtractImages = parts.Contains("images") || parts.Contains("image") || parts.Contains("img");
        options.ExtractSprites = parts.Contains("sprites") || parts.Contains("sprite") || parts.Contains("spr");
        options.ExtractMovies = parts.Contains("movies") || parts.Contains("movie") || parts.Contains("mov");
        options.ExtractAudio = parts.Contains("audio") || parts.Contains("sound") || parts.Contains("sounds") || parts.Contains("snd");

        return options;
    }
}

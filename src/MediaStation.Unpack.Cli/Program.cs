using System.CommandLine;
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

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose output logging",
            Aliases = { "-v" }
        };

        var rootCommand = new RootCommand("Extracts graphics (PNG/APNG) and audio (WAV) from MediaStation game archives (.STM, .CXT)")
        {
            pathArgument,
            outputOption,
            extractOption,
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
            bool verbose = parseResult.GetValue(verboseOption);

            var options = ParseExtractOptions(extractStr);
            options.OutputPath = Path.GetFullPath(output);
            options.Verbose = verbose;

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

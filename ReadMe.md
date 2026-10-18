# MediaStation Unpack

A .NET tool and library for extracting graphic and audio assets from MediaStation adventure game archives (`.STM`, `.CXT`).

The format parsing and decompression algorithms are implemented with reference to the MediaStation engine in [ScummVM](https://www.scummvm.org/).

## Features

- **Images**: Extracts 8-bit indexed bitmaps to PNG, WebP (lossless), and GIF with exact palette mapping and transparency.
- **Sprites**: Exports multi-frame sprite sequences and named sub-clips to Animated PNG (APNG), Animated WebP, and Animated GIF.
- **Movies**: Decompresses keyframes and delta frames, assembling multi-layer stream animations into APNG, WebP, and GIF files with accurate frame durations.
- **Audio**: Decodes IMA ADPCM audio streams and exports concatenated standard WAV files.
- **Asset Naming**: Maps numeric asset identifiers to human-readable names using profile metadata when present.

## Requirements

- .NET 10.0 SDK or later

## Building

Build the solution using the .NET CLI:

```cmd
dotnet build
```

Build outputs are placed in the root `artifacts/` directory.

## Usage

Run the command-line extractor:

```cmd
dotnet run --project src/MediaStation.Unpack.Cli -- <path-to-game-boot-or-context-file> [options]
```

### Options

| Option | Description |
|---|---|
| `-o, --output <dir>` | Destination directory for extracted files. |
| `-e, --extract <types>` | Asset types to extract: `graphics` (default: images, sprites, movies), `all` (graphics + audio), or comma-separated list (`images`, `sprites`, `movies`, `audio`). |
| `-if, --image-format <formats>` | Static image formats: comma-separated list (`png`, `webp`, `gif`). Default: `png`. (WebP is always lossless; GIF preserves exact 8-bit palette). |
| `-af, --animation-format <formats>` | Animation formats for sprites and movies: comma-separated list (`png`, `webp`, `gif`). Default: `png`. |
| `-v, --verbose` | Enable detailed progress logging. |

### Examples

Extract all graphics from a game disc to an output folder:

```cmd
dotnet run --project src/MediaStation.Unpack.Cli -- "D:\" -o "C:\Output\Game"
```

Extract animations as both GIF and APNG:

```cmd
dotnet run --project src/MediaStation.Unpack.Cli -- "D:\" -o "C:\Output\Game" -af gif,png
```

Extract graphics and audio from a specific context file:

```cmd
dotnet run --project src/MediaStation.Unpack.Cli -- "D:\DATA\174.CXT" -o "C:\Output\Game" -e all
```

## Acknowledgements

Special thanks to **Nathanael Gentry** ([@npjg](https://github.com/npjg)), author of:
- The [ScummVM MediaStation engine](https://github.com/scummvm/scummvm/tree/master/engines/mediastation) plugin in [ScummVM](https://www.scummvm.org/)
- The original [MediaStation](https://github.com/npjg/MediaStation) Python game archive extraction project

Their reverse-engineering work and format documentation served as the foundation for this tool.

## License

This project is licensed under the GNU General Public License Version 3 (GPL-3.0-or-later). See [License.md](License.md) for details.

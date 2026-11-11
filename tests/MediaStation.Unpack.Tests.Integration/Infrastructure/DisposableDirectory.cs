using System;
using System.IO;
using System.Threading;

namespace MediaStation.Unpack.Tests.Integration.Infrastructure;

/// <summary>
/// Manages a temporary directory located inside &lt;repoRoot&gt;/tmp/tests/ that is deleted upon disposal.
/// Includes retry resilience against transient file locks on Windows.
/// </summary>
public sealed class DisposableDirectory : IDisposable
{
    private static readonly Lazy<string> BaseTestsDir = new(ResolveBaseTestsDirectory);
    private bool _disposed;

    public string Path { get; }

    public DisposableDirectory(string prefix = "ms_test_integ_")
    {
        string dirName = prefix + Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(BaseTestsDir.Value, dirName);
        Directory.CreateDirectory(Path);
    }

    public static implicit operator string(DisposableDirectory dir) => dir.Path;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (!Directory.Exists(Path))
            return;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }

        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Do not crash test runner during disposal cleanup failure
        }
    }

    private static string ResolveBaseTestsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "MediaStation.Unpack.slnx")) ||
                File.Exists(System.IO.Path.Combine(current.FullName, "Directory.Build.props")))
            {
                string path = System.IO.Path.Combine(current.FullName, "tmp", "tests");
                Directory.CreateDirectory(path);
                return path;
            }
            current = current.Parent;
        }

        string fallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediaStationUnpack", "tmp", "tests");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}

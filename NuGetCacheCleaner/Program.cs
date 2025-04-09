using System.Reflection;
using Mono.Options;
using NuGet.Versioning;

namespace NugetCacheCleaner;

internal static class Program
{
    private static readonly string[] ByteQuantitySuffixes
        = ["B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB", "RB", "QB"];

    private static readonly string? ProcessExe = Path.GetFileName(Environment.ProcessPath);
    private static readonly string? ProcessName = Path.GetFileNameWithoutExtension(ProcessExe);

    internal static void Main(string[] args)
    {
        var commit = false;
        var showHelp = false;
        var minDays = TimeSpan.FromDays(90);
        var prune = false;
        var verbose = false;

        var options = new OptionSet
        {
            {
                "c|commit",
                "Performs the actual clean-up. Default is to do a dry-run and report the clean-up that would be done.",
                v => commit = v is not null
            },
            {
                "m|min-days=",
                "Number of days a package must not be used in order to be purged from the cache. Defaults to 90.",
                v => minDays = ParseDays(v)
            },
            {
                "v|verbose",
                "Verbose mode will display the paths directories that would be removed.",
                v => verbose = v is not null
            },
            {"p|prune", "Prune older versions of packages regardless of age.", v => prune = v is not null},
            {"?|h|help", "Show this message.", v => showHelp = v != null},
        };

        List<string> extra;
        try
        {
            extra = options.Parse(args);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return;
        }

        Unused(extra);

        if (showHelp)
        {
            ShowHelp(options);
            return;
        }

        var deletedBytes = CleanCache(commit, minDays, prune, verbose);
        var deletedQty = ToHumanReadableBytes(deletedBytes);

        if (commit)
        {
            Console.WriteLine($"Done! Deleted {deletedQty} MB.");
            return;
        }

        Console.WriteLine(prune
            ? $"{deletedQty} worth of packages are older than {minDays.TotalDays:N0} days or are not the latest version."
            : $"{deletedQty} worth of packages are older than {minDays.TotalDays:N0} days.");

        if (deletedBytes != 0)
            Console.WriteLine("To delete, re-run with -c or --commit flag.");
    }

    private static string ToHumanReadableBytes(double bytes)
    {
        if (bytes == 0)
            return "0 B";
        var signed = bytes < 0;
        bytes = Math.Abs(bytes);
        var lb = Math.Log(bytes, 1024);
        var suffixIndex = (int) Math.Floor(lb);
        if (suffixIndex >= ByteQuantitySuffixes.Length)
            suffixIndex = ByteQuantitySuffixes.Length - 1;
        var suffix = ByteQuantitySuffixes[suffixIndex];
        var suffixScale = Math.Pow(1024, suffixIndex);
        var size = Math.Round(bytes / suffixScale, 2, MidpointRounding.ToPositiveInfinity);
        return signed ? $"-{size} {suffix}" : $"{size} {suffix}";
    }

    private static void ShowHelp(OptionSet optionSet)
    {
        Console.WriteLine($"usage: {ProcessName} [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        optionSet.WriteOptionDescriptions(Console.Out);
    }

    private static TimeSpan ParseDays(string text)
    {
        if (!int.TryParse(text, out var days))
            throw new FormatException($"'{text}' isn't a valid integer");

        return TimeSpan.FromDays(days);
    }

    private static long CleanCache(bool commit, TimeSpan minDays, bool prune, bool verbose)
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var nugetCachePath = Path.Join(userProfilePath, ".nuget", "packages");
        var nugetCache = new DirectoryInfo(nugetCachePath);

        if (!nugetCache.Exists)
        {
            Console.WriteLine($"Warning: Missing nuget package folder: {nugetCache.FullName}");
            return 0;
        }


        var deleted = new HashSet<DirectoryInfo>();
        var deletedBytes = 0L;
        foreach (var dir in nugetCache.GetDirectories())
        {
            if (dir.Name != ".tools")
            {
                deletedBytes += CleanPackageDirectory(dir, commit, deleted, prune, minDays, verbose);
                continue;
            }

            foreach (var toolDir in dir.GetDirectories())
                deletedBytes += CleanPackageDirectory(toolDir, commit, deleted, prune, minDays, verbose);
        }

        return deletedBytes;
    }

    private static long CleanPackageDirectory(DirectoryInfo dir, bool commit, HashSet<DirectoryInfo> deleted,
        bool prune, TimeSpan minDays, bool verbose)
    {
        var deletedBytes = 0L;

        var versions = new Dictionary<NuGetVersion, DirectoryInfo>();
        deleted.Clear();
        foreach (var subDir in dir.GetDirectories())
        {
            if (!NuGetVersion.TryParse(subDir.Name, out var version))
            {
                Console.WriteLine($"Warning: Skipping non-version format directory {subDir.FullName}.");
                continue;
            }

            versions.Add(version, subDir);
        }

        if (prune)
            deletedBytes += Prune(commit, deleted, versions, verbose);

        foreach (var versionedDir in versions)
        {
            var versionDir = versionedDir.Value;
            var versionDirFiles = versionDir.GetFiles("*.*", SearchOption.AllDirectories);
            if (versionDirFiles.Length == 0)
            {
                DeleteDir(versionDir, commit, false, verbose);
                continue;
            }

            var lastAccessed = DateTime.UtcNow - versionDirFiles.Max(GetLastAccessed);

            if (lastAccessed <= minDays)
                continue;

            Console.WriteLine($"{versionDir.FullName} last accessed {Math.Floor(lastAccessed.TotalDays)} days ago");

            deletedBytes += DeleteVersion(versionDirFiles, versionDir, commit, deleted, true, verbose);
        }

        if (dir.GetDirectories().Length == 0)
            DeleteDir(dir, commit, false, verbose);

        return deletedBytes;
    }

    private static long Prune(bool commit, HashSet<DirectoryInfo> deleted,
        Dictionary<NuGetVersion, DirectoryInfo> versions, bool verbose)
    {
        var deletedBytes = 0L;
        // keep the newest release and newest prerelease (if newer than the newest release)
        var releases = versions.Keys.Where(v => !v.IsPrerelease).ToArray();
        var newestRelease = releases.Length != 0 ? releases.Max() : null;
        var prereleases = newestRelease != null
            ? versions.Keys.Where(v => v > newestRelease && v.IsPrerelease).ToArray()
            : [];
        var newestPrerelease = prereleases.Length != 0 ? prereleases.Max() : null;

        foreach (var versionedDir in versions)
        {
            if (versionedDir.Key == newestRelease) continue;
            if (versionedDir.Key == newestPrerelease) continue;
            var versionDir = versionedDir.Value;
            if (deleted.Contains(versionDir)) continue;
            var versionDirFiles = versionDir.GetFiles("*.*", SearchOption.AllDirectories);
            deletedBytes += DeleteVersion(versionDirFiles, versionDir, commit, deleted, verbose: verbose);
        }

        foreach (var deletedDir in deleted)
        {
            var parsedVersion = versions.First(k => k.Value == deletedDir).Key;
            versions.Remove(parsedVersion);
        }

        return deletedBytes;
    }

    private static long DeleteVersion(FileInfo[] versionDirFiles, DirectoryInfo versionDir, bool commit,
        HashSet<DirectoryInfo> deleted, bool withLockCheck = false, bool verbose = false)
    {
        try
        {
            var size = versionDirFiles.Sum(f => f.Length);
            DeleteDir(versionDir, commit, withLockCheck, verbose);
            deleted.Add(versionDir);
            return size;
        }
        catch (FileNotFoundException)
        {
            // ok
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine($"Warning: Not authorized to delete {versionDir.FullName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Deleting {versionDir.FullName} encountered {ex.GetType().Name}: {ex.Message}");
        }

        return 0;
    }

    private static DateTime GetLastAccessed(FileInfo f)
    {
        try
        {
            return DateTime.FromFileTimeUtc(Math.Max(f.LastAccessTimeUtc.ToFileTimeUtc(),
                f.LastWriteTimeUtc.ToFileTimeUtc()));
        }
        catch
        {
            return f.LastWriteTimeUtc;
        }
    }

    private static void DeleteDir(DirectoryInfo dir, bool commit, bool withLockCheck, bool verbose)
    {
        if (verbose)
            Console.WriteLine($" - {dir.FullName}");
        
        if (!commit) return;

        if (!withLockCheck) // This may only be good enough for Windows
        {
            dir.Delete(recursive: true);
            return;
        }

        var parentDir = dir.Parent;
        if (parentDir == null) throw new NotImplementedException("Missing parent directory.");

        // seems like the most effective the "lock check" is to rename the directory
        var tempPath = Path.Join(parentDir.FullName, $"_{dir.Name}");
        dir.MoveTo(tempPath);
        Directory.Delete(tempPath, recursive: true);
    }


    private static void Unused<T>(T _)
    {
    }
}
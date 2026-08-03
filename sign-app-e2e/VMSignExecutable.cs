using System.Reflection;

namespace VMSign.AppE2E;

internal static class VMSignExecutable
{
    private const string OverrideVariable = "VMSIGN_E2E_EXE";

    public static string Find()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var resolvedOverride = ResolveOverride(overridePath);
            if (File.Exists(resolvedOverride))
            {
                return Path.GetFullPath(resolvedOverride);
            }

            throw new FileNotFoundException(
                $"{OverrideVariable} points to a missing VMSign executable.",
                resolvedOverride);
        }

        var projectDirectory = ReadMetadata("VMSignProjectDirectory")
            ?? FindProjectDirectoryFrom(AppContext.BaseDirectory)
            ?? FindProjectDirectoryFrom(Directory.GetCurrentDirectory());

        if (projectDirectory is null)
        {
            throw new DirectoryNotFoundException(
                "Could not locate samples/sign-app. Set VMSIGN_E2E_EXE to the full path of VMSign.exe.");
        }

        var configuration = ReadMetadata("BuildConfiguration") ?? "Debug";
        var expectedPath = Path.Combine(projectDirectory, "bin", configuration, "net8.0", "VMSign.exe");
        if (File.Exists(expectedPath))
        {
            return Path.GetFullPath(expectedPath);
        }

        var binDirectory = Path.Combine(projectDirectory, "bin");
        var candidates = Directory.Exists(binDirectory)
            ? Directory.EnumerateFiles(binDirectory, "VMSign.exe", SearchOption.AllDirectories)
                .OrderByDescending(path => IsInConfiguration(path, configuration))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ToArray()
            : [];

        if (candidates.Length > 0)
        {
            return Path.GetFullPath(candidates[0]);
        }

        throw new FileNotFoundException(
            $"VMSign.exe was not found under '{binDirectory}'. Build samples/sign-app first or set {OverrideVariable}.",
            expectedPath);
    }

    private static string ResolveOverride(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        return Directory.Exists(fullPath) ? Path.Combine(fullPath, "VMSign.exe") : fullPath;
    }

    private static string? ReadMetadata(string key) =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;

    private static string? FindProjectDirectoryFrom(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            var siblingProject = Path.Combine(current.FullName, "sign-app", "VMSign.csproj");
            if (File.Exists(siblingProject))
            {
                return Path.GetDirectoryName(siblingProject);
            }

            var repositoryProject = Path.Combine(current.FullName, "samples", "sign-app", "VMSign.csproj");
            if (File.Exists(repositoryProject))
            {
                return Path.GetDirectoryName(repositoryProject);
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsInConfiguration(string path, string configuration) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, configuration, StringComparison.OrdinalIgnoreCase));
}

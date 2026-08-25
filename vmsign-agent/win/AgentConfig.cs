using System;
using System.Configuration;
using System.IO;

namespace VMSignAgent;

/// <summary>
/// Per-user settings store for VMSignAgent.
///
/// The agent used to read and write VMSignAgent.exe.config in its own install
/// directory. That cannot work for a normal (non-elevated) run: the installer
/// puts the agent under C:\Program Files, and Configuration.Save() does not
/// write in place -- it creates a temp file next to the target and swaps it in,
/// so it needs create-file rights on the install directory, not just write
/// rights on the .config itself.
///
/// Settings therefore live in %LOCALAPPDATA%\VMSignAgent\VMSignAgent.config,
/// seeded once from the installed VMSignAgent.exe.config. That file stays the
/// read-only template of shipped defaults, and seeding from it also carries
/// over settings saved by older versions that happened to run elevated.
/// </summary>
internal static class AgentConfig
{
    private static readonly object Gate = new();
    private static Configuration? _config;

    public static string UserConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VMSignAgent",
        "VMSignAgent.config");

    /// <summary>Reads a setting; returns an empty string when absent.</summary>
    public static string Get(string key)
    {
        lock (Gate)
        {
            return Load().AppSettings.Settings[key]?.Value ?? string.Empty;
        }
    }

    /// <summary>Drops the cached copy so the next read hits disk again.</summary>
    public static void Reload()
    {
        lock (Gate)
        {
            _config = null;
        }
    }

    /// <summary>Applies <paramref name="edit"/> to the settings and persists them.</summary>
    public static void Save(Action<KeyValueConfigurationCollection> edit)
    {
        lock (Gate)
        {
            var config = Load();
            edit(config.AppSettings.Settings);
            config.Save(ConfigurationSaveMode.Modified);
            _config = null;
        }
    }

    private static Configuration Load()
    {
        if (_config != null)
        {
            return _config;
        }

        EnsureUserConfigExists();
        var map = new ExeConfigurationFileMap { ExeConfigFilename = UserConfigPath };
        _config = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);
        return _config;
    }

    private static void EnsureUserConfigExists()
    {
        if (File.Exists(UserConfigPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(UserConfigPath)!);

        var template = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
        if (!string.IsNullOrEmpty(template) && File.Exists(template))
        {
            File.Copy(template, UserConfigPath);
            // the template may come off a read-only install directory
            var attrs = File.GetAttributes(UserConfigPath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(UserConfigPath, attrs & ~FileAttributes.ReadOnly);
            }
        }
        else
        {
            File.WriteAllText(UserConfigPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<configuration>\r\n  <appSettings />\r\n</configuration>\r\n");
        }
    }
}

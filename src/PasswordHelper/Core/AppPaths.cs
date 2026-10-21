using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PasswordHelper.Core;

/// <summary>Locations of everything Password helper writes to disk. All data lives under %LOCALAPPDATA%.</summary>
internal static class AppPaths
{
    public const string AppFolderName = "PasswordHelper";

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string DataDirectory { get; private set; } = DefaultDataDirectory;

    /// <summary>True when the data directory was overridden with --data-dir (portable use and automated tests).</summary>
    public static bool IsCustomDataDirectory { get; private set; }

    public static string VaultFile => Path.Combine(DataDirectory, "vault.dat");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PasswordHelper.exe");

    /// <summary>
    /// Short key that identifies this data directory. Used to name the single-instance mutex and signals,
    /// so a test instance with its own data directory can run beside the normal one.
    /// </summary>
    public static string InstanceKey { get; private set; } = "default";

    public static void Initialize(string? overrideDirectory)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            DataDirectory = Path.GetFullPath(overrideDirectory);
            IsCustomDataDirectory = !string.Equals(DataDirectory.TrimEnd('\\'), DefaultDataDirectory.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        if (IsCustomDataDirectory)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant()));
            InstanceKey = Convert.ToHexString(hash, 0, 6);
        }

        Directory.CreateDirectory(DataDirectory);
    }
}

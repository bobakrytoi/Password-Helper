using System;
using Microsoft.Win32;
using PasswordHelper.Interop;

namespace PasswordHelper.Core;

/// <summary>Read-only facts about the running Windows installation and the user's display preferences.</summary>
internal static class SystemInfo
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static int OsBuild { get; } = Environment.OSVersion.Version.Build;

    public static bool IsWindows11 => OsBuild >= 22000;

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE (documented Mica/Acrylic backdrops) exists from Windows 11 22H2.</summary>
    public static bool SupportsSystemBackdrop => OsBuild >= 22621;

    public static bool TransparencyEffectsEnabled => ReadPersonalize("EnableTransparency", 1) != 0;

    public static bool TaskbarUsesLightTheme => ReadPersonalize("SystemUsesLightTheme", 0) != 0;

    /// <summary>"Animation effects" in Windows Settings › Accessibility › Visual effects.</summary>
    public static bool AnimationsEnabled
    {
        get
        {
            int enabled = 1;
            return !Win32.SystemParametersInfo(Win32.SPI_GETCLIENTAREAANIMATION, 0, ref enabled, 0) || enabled != 0;
        }
    }

    public static string Describe() =>
        $"Windows build {OsBuild} ({(IsWindows11 ? "Windows 11" : "Windows 10")}), .NET {Environment.Version}, " +
        $"{(Environment.Is64BitProcess ? "64" : "32")}-bit";

    private static int ReadPersonalize(string name, int fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

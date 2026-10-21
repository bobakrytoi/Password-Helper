using System;
using System.Collections.Generic;
using System.Linq;
using PasswordHelper.Passwords;

namespace PasswordHelper.Core;

internal enum OverlayPlacement
{
    MousePointer,
    ScreenCenter,
}

internal enum RadialButtonSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// User preferences. Contains no secrets, so it is stored as plain JSON.
/// "Launch at Windows startup" is not stored here: the registry Run entry is the single source of truth.
/// </summary>
internal sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // General
    public bool StartMinimized { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool CloseOnClickOutside { get; set; } = true;
    public bool CloseAfterSelection { get; set; } = true;
    public bool InsertIntoActiveApp { get; set; } = true;
    public OverlayPlacement OverlayPlacement { get; set; } = OverlayPlacement.MousePointer;

    // Hotkey (must contain a real key: Windows' RegisterHotKey cannot express a modifier-only chord)
    public string Hotkey { get; set; } = "Ctrl+Shift+P";

    // Password generation
    public int PasswordLength { get; set; } = 32;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; }
    public string SymbolSet { get; set; } = PasswordGenerator.DefaultSymbols;

    // Clipboard
    public bool ClipboardHistoryEnabled { get; set; } = true;
    public int ClipboardHistoryLimit { get; set; } = 20;
    /// <summary>Seconds after which a password placed on the clipboard is cleared (0 = never).</summary>
    public int ClearClipboardAfterSeconds { get; set; } = 90;
    public List<string> ClipboardIgnoredApps { get; set; } = DefaultIgnoredApps();

    // Security
    public bool LockVaultOnWindowsLock { get; set; } = true;
    /// <summary>Minutes of no vault use after which the vault locks itself (0 = never).</summary>
    public int AutoLockMinutes { get; set; }

    // Sound
    public bool ClickSoundEnabled { get; set; } = true;
    public double ClickSoundVolume { get; set; } = 0.45;

    // Appearance
    public bool GlassEffect { get; set; } = true;
    public double GlassOpacity { get; set; } = 0.78;
    public double BlurIntensity { get; set; } = 0.6;
    public double AnimationSpeed { get; set; } = 1.0;
    public RadialButtonSize ButtonSize { get; set; } = RadialButtonSize.Medium;
    /// <summary>Saved-password shortcuts shown on the outer ring before a "More" item takes over.</summary>
    public int MaxRadialItems { get; set; } = 10;

    public static List<string> DefaultIgnoredApps() => new()
    {
        "KeePass.exe", "KeePassXC.exe", "1Password.exe", "Bitwarden.exe", "Dashlane.exe",
        "NordPass.exe", "Enpass.exe", "RoboForm.exe", "LastPass.exe", "ProtonPass.exe",
    };

    public PasswordPolicy ToPasswordPolicy() =>
        new(PasswordLength, IncludeUppercase, IncludeLowercase, IncludeDigits, IncludeSymbols, ExcludeAmbiguous, SymbolSet);

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.ClipboardIgnoredApps = new List<string>(ClipboardIgnoredApps);
        return copy;
    }

    /// <summary>Clamps every value into its supported range so a hand-edited file cannot break the app.</summary>
    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        if (!Enum.IsDefined(OverlayPlacement)) OverlayPlacement = OverlayPlacement.MousePointer;
        if (!Enum.IsDefined(ButtonSize)) ButtonSize = RadialButtonSize.Medium;
        if (string.IsNullOrWhiteSpace(Hotkey)) Hotkey = "Ctrl+Shift+P";

        PasswordLength = Math.Clamp(PasswordLength, PasswordGenerator.MinLength, PasswordGenerator.MaxLength);
        SymbolSet = PasswordGenerator.SanitizeSymbols(SymbolSet);
        if (SymbolSet.Length == 0) SymbolSet = PasswordGenerator.DefaultSymbols;
        if (!IncludeUppercase && !IncludeLowercase && !IncludeDigits && !IncludeSymbols)
        {
            IncludeLowercase = true;
        }

        ClipboardHistoryLimit = Math.Clamp(ClipboardHistoryLimit, 5, 100);
        ClearClipboardAfterSeconds = Math.Clamp(ClearClipboardAfterSeconds, 0, 3600);
        ClipboardIgnoredApps = (ClipboardIgnoredApps ?? new List<string>())
            .Select(a => a?.Trim() ?? string.Empty)
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AutoLockMinutes = Math.Clamp(AutoLockMinutes, 0, 24 * 60);

        ClickSoundVolume = Math.Clamp(double.IsFinite(ClickSoundVolume) ? ClickSoundVolume : 0.45, 0.0, 1.0);
        GlassOpacity = Math.Clamp(double.IsFinite(GlassOpacity) ? GlassOpacity : 0.78, 0.4, 1.0);
        BlurIntensity = Math.Clamp(double.IsFinite(BlurIntensity) ? BlurIntensity : 0.6, 0.0, 1.0);
        AnimationSpeed = Math.Clamp(double.IsFinite(AnimationSpeed) ? AnimationSpeed : 1.0, 0.5, 2.0);
        MaxRadialItems = Math.Clamp(MaxRadialItems, 0, 16);
    }
}

using PasswordHelper.Core;
using Xunit;

namespace PasswordHelper.Tests;

public class SettingsTests
{
    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var settings = new AppSettings
        {
            PasswordLength = 9999,
            ClipboardHistoryLimit = 1,
            ClearClipboardAfterSeconds = -5,
            GlassOpacity = 5,
            AnimationSpeed = 0.01,
            MaxRadialItems = 999,
            Hotkey = "   ",
        };

        settings.Normalize();

        Assert.InRange(settings.PasswordLength, 4, 256);
        Assert.InRange(settings.ClipboardHistoryLimit, 5, 100);
        Assert.Equal(0, settings.ClearClipboardAfterSeconds);
        Assert.InRange(settings.GlassOpacity, 0.4, 1.0);
        Assert.InRange(settings.AnimationSpeed, 0.5, 2.0);
        Assert.InRange(settings.MaxRadialItems, 0, 16);
        Assert.Equal("Ctrl+Shift+P", settings.Hotkey);
    }

    [Fact]
    public void Normalize_EnsuresAtLeastOneCharacterClass()
    {
        var settings = new AppSettings
        {
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeDigits = false,
            IncludeSymbols = false,
        };

        settings.Normalize();
        Assert.True(settings.IncludeLowercase);
    }

    [Fact]
    public void ToPasswordPolicy_ReflectsSettings()
    {
        var settings = new AppSettings { PasswordLength = 24, IncludeSymbols = false };
        settings.Normalize();
        var policy = settings.ToPasswordPolicy();
        Assert.Equal(24, policy.Length);
        Assert.False(policy.Symbols);
    }
}

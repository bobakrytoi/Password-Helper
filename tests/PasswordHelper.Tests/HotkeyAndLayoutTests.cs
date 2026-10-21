using System.Linq;
using PasswordHelper.Input;
using PasswordHelper.UI.Overlay;
using Xunit;

namespace PasswordHelper.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("Ctrl+Alt+K")]
    [InlineData("Shift+Win+Space")]
    [InlineData("Ctrl+F12")]
    public void ParseThenToString_RoundTrips(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
        Assert.Equal(text, gesture.ToString());
    }

    [Fact]
    public void Parse_IsCaseInsensitiveAndTrims()
    {
        Assert.True(HotkeyGesture.TryParse(" control + shift + p ", out HotkeyGesture gesture));
        Assert.Equal("Ctrl+Shift+P", gesture.ToString());
    }

    [Fact]
    public void Validate_RejectsModifierOnly()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift", out HotkeyGesture gesture));
        Assert.NotNull(gesture.Validate());
    }

    [Fact]
    public void Validate_RejectsReservedShortcut()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+C", out HotkeyGesture gesture));
        Assert.NotNull(gesture.Validate());
    }

    [Fact]
    public void Validate_RejectsPlainLetterWithoutStrongModifier()
    {
        Assert.True(HotkeyGesture.TryParse("Shift+A", out HotkeyGesture gesture));
        Assert.NotNull(gesture.Validate());
    }

    [Fact]
    public void Validate_AcceptsTypicalCombo()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift+P", out HotkeyGesture gesture));
        Assert.Null(gesture.Validate());
    }

    [Fact]
    public void Default_IsValidAndRegisterable()
    {
        Assert.Equal("Ctrl+Shift+P", HotkeyGesture.Default.ToString());
        Assert.Null(HotkeyGesture.Default.Validate());
        Assert.False(HotkeyGesture.Default.IsModifierOnly);
    }

    [Fact]
    public void ToNativeModifiers_MapsAllFlags()
    {
        HotkeyGesture.TryParse("Ctrl+Alt+Shift+Win+P", out HotkeyGesture gesture);
        uint native = gesture.ToNativeModifiers();
        Assert.Equal(0x0001u | 0x0002u | 0x0004u | 0x0008u, native); // ALT|CONTROL|SHIFT|WIN
    }
}

public class RadialLayoutTests
{
    private static readonly RadialMetrics Metrics = RadialMetrics.ForButtonRadius(37);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(30)]
    public void Arrange_ProducesOnePlacementPerItem(int count)
    {
        RadialLayoutResult result = RadialLayout.Arrange(count, Metrics);
        Assert.Equal(count, result.Placements.Count);
    }

    [Fact]
    public void Arrange_StartsMultipleRingsForManyItems()
    {
        RadialLayoutResult few = RadialLayout.Arrange(5, Metrics);
        RadialLayoutResult many = RadialLayout.Arrange(28, Metrics);
        Assert.Equal(1, few.RingCount);
        Assert.True(many.RingCount >= 2);
        Assert.True(many.BoundingRadius > few.BoundingRadius);
    }

    [Fact]
    public void Arrange_AllCoordinatesAreFinite()
    {
        RadialLayoutResult result = RadialLayout.Arrange(16, Metrics);
        Assert.All(result.Placements, p =>
        {
            Assert.True(double.IsFinite(p.X));
            Assert.True(double.IsFinite(p.Y));
        });
    }

    [Fact]
    public void Arrange_KeepsMinimumSpacingBetweenNeighboursOnARing()
    {
        RadialLayoutResult result = RadialLayout.Arrange(6, Metrics);
        var ring0 = result.Placements.Where(p => p.Ring == 0).ToList();
        double minSpacing = 2 * Metrics.ButtonRadius; // buttons must not overlap
        for (int i = 0; i < ring0.Count; i++)
        {
            var a = ring0[i];
            var b = ring0[(i + 1) % ring0.Count];
            double dist = System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            Assert.True(dist >= minSpacing * 0.9, $"neighbours too close: {dist}");
        }
    }
}

using System;
using System.Collections.Generic;

namespace PasswordHelper.UI.Overlay;

/// <summary>Sizing inputs for <see cref="RadialLayout"/>, all in device-independent pixels.</summary>
internal readonly record struct RadialMetrics(double CenterRadius, double ButtonRadius, double Gap, double RingGap, double InnerMargin)
{
    public static RadialMetrics ForButtonRadius(double buttonRadius) =>
        new(CenterRadius: Math.Max(46, buttonRadius * 1.7),
            ButtonRadius: buttonRadius,
            Gap: buttonRadius * 0.75,
            RingGap: buttonRadius * 1.15,
            InnerMargin: buttonRadius * 0.55);
}

/// <summary>Where one radial button sits: its ring, angle, and centre offset from the overlay centre.</summary>
internal readonly record struct RadialPlacement(int Index, int Ring, double Angle, double X, double Y);

internal sealed class RadialLayoutResult
{
    public required IReadOnlyList<RadialPlacement> Placements { get; init; }
    public required int RingCount { get; init; }
    /// <summary>Distance from the centre to the outer edge of the outermost button.</summary>
    public required double BoundingRadius { get; init; }
}

/// <summary>
/// Arranges a number of equal-size buttons in concentric rings around a central hub. Rings are filled from the
/// inside out; each ring holds as many buttons as fit without overlapping (respecting the gap), and the radius
/// grows for each new ring so the layout expands intelligently rather than cramming or shrinking buttons.
/// Pure geometry with no WPF dependency, so it is unit-tested directly.
/// </summary>
internal static class RadialLayout
{
    /// <summary>Maximum buttons on a single ring before a new, larger ring is started (keeps rings readable).</summary>
    private const int HardPerRingCap = 12;

    public static RadialLayoutResult Arrange(int count, RadialMetrics metrics)
    {
        var placements = new List<RadialPlacement>(Math.Max(count, 0));
        if (count <= 0)
        {
            return new RadialLayoutResult { Placements = placements, RingCount = 0, BoundingRadius = metrics.CenterRadius };
        }

        double spacing = 2 * metrics.ButtonRadius + metrics.Gap; // minimum centre-to-centre distance
        double radius = metrics.CenterRadius + metrics.ButtonRadius + metrics.InnerMargin;
        double outerEdge = metrics.CenterRadius;

        int remaining = count;
        int ring = 0;
        int placed = 0;

        while (remaining > 0)
        {
            int capacity = Math.Clamp((int)Math.Floor(2 * Math.PI * radius / spacing), 3, HardPerRingCap);
            int take = Math.Min(remaining, capacity);

            // If everything that is left fits on this ring, spread it out; otherwise fill the ring.
            int onThisRing = take;
            double angleStep = 2 * Math.PI / onThisRing;

            // Alternate rings start offset by half a step so buttons are not radially aligned between rings.
            double startAngle = -Math.PI / 2 + (ring % 2 == 1 ? angleStep / 2 : 0);

            for (int i = 0; i < onThisRing; i++)
            {
                double angle = startAngle + i * angleStep;
                placements.Add(new RadialPlacement(
                    Index: placed + i,
                    Ring: ring,
                    Angle: angle,
                    X: radius * Math.Cos(angle),
                    Y: radius * Math.Sin(angle)));
            }

            outerEdge = radius + metrics.ButtonRadius;
            placed += onThisRing;
            remaining -= onThisRing;
            ring++;
            radius += 2 * metrics.ButtonRadius + metrics.RingGap;
        }

        return new RadialLayoutResult
        {
            Placements = placements,
            RingCount = ring,
            BoundingRadius = outerEdge,
        };
    }
}

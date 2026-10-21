using System.Collections.Generic;
using System.Windows.Media;

namespace PasswordHelper.UI.Common;

/// <summary>
/// Vector glyphs on a 24×24 canvas, drawn as stroked open paths to match the thin, minimal aesthetic.
/// Geometries are frozen for reuse. Path data follows the Lucide icon set (ISC licensed).
/// </summary>
internal static class Icons
{
    public const string Generate = "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5l1.582-6.135a.5.5 0 0 1 .962 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.582a.5.5 0 0 1 0 .962L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.962 0z M20 3v4 M22 5h-4 M4 17v2 M5 18H3";
    public const string SavedList = "M8 6h13 M8 12h13 M8 18h13 M3.5 6h.01 M3.5 12h.01 M3.5 18h.01";
    public const string Clipboard = "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2 M9 2h6a1 1 0 0 1 1 1v1a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z M8.5 12h7 M8.5 16h7";
    public const string Add = "M12 5v14 M5 12h14";
    public const string More = "M5 12h.02 M12 12h.02 M19 12h.02";
    public const string Close = "M6 6 18 18 M18 6 6 18";
    public const string Key = "M16.5 7.5h.01 M21 2l-9.6 9.6 M15.5 7.5a6 6 0 1 1-8.49 8.49 6 6 0 0 1 8.49-8.49z M18 5l2 2 M15 8l2.5 2.5";
    public const string Copy = "M9 7h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z M15 3H5a2 2 0 0 0-2 2v10";
    public const string Edit = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    public const string Trash = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M10 11v6 M14 11v6";
    public const string Eye = "M2.1 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.8 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.8 0z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0z";
    public const string EyeOff = "M10.73 5.08A10.43 10.43 0 0 1 12 5c5.5 0 9.2 4.6 9.9 6.35a1 1 0 0 1 0 .7 13 13 0 0 1-1.7 2.68 M6.6 6.6A13 13 0 0 0 2.1 11.65a1 1 0 0 0 0 .7C2.8 14.1 6.5 19 12 19a9.7 9.7 0 0 0 5.4-1.6 M3 3l18 18 M9.9 9.9a3 3 0 0 0 4.2 4.2";
    public const string Settings = "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z";
    public const string Lock = "M5 11h14a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z M8 11V7a4 4 0 0 1 8 0v4";
    public const string Unlock = "M5 11h14a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z M8 11V7a4 4 0 0 1 7.6-1.6";
    public const string Search = "M11 3a8 8 0 1 0 0 16 8 8 0 0 0 0-16z M21 21l-4.35-4.35";
    public const string Pin = "M12 17v5 M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z";
    public const string Check = "M20 6 9 17l-5-5";
    public const string ChevronRight = "M9 18l6-6-6-6";
    public const string ChevronLeft = "M15 18l-6-6 6-6";
    public const string User = "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2 M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    public const string Link = "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71 M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71";
    public const string Note = "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z M14 2v5h5 M9 13h6 M9 17h6";
    public const string Refresh = "M3 12a9 9 0 0 1 15-6.7L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-15 6.7L3 16 M3 21v-5h5";
    public const string Dice = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 8h.01 M16 8h.01 M8 16h.01 M16 16h.01 M12 12h.01";
    public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry Get(string pathData)
    {
        if (Cache.TryGetValue(pathData, out Geometry? cached)) return cached;
        Geometry geometry = Geometry.Parse(pathData);
        geometry.Freeze();
        Cache[pathData] = geometry;
        return geometry;
    }
}

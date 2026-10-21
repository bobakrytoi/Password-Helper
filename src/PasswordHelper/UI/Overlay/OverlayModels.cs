using System;
using System.Collections.Generic;

namespace PasswordHelper.UI.Overlay;

internal enum RadialItemKind
{
    Generate,
    ClipboardHistory,
    SavedPasswords,
    AddPassword,
    More,
    SavedEntry,
}

/// <summary>One button on the radial. For <see cref="RadialItemKind.SavedEntry"/> the monogram is shown; others use an icon.</summary>
internal sealed class RadialItem
{
    public required RadialItemKind Kind { get; init; }
    public required string Label { get; init; }
    public string IconPath { get; init; } = string.Empty;
    public string? Monogram { get; init; }
    public Guid EntryId { get; init; }
    public bool Primary { get; init; }
    public string? Tooltip { get; init; }

    /// <summary>Invoked when the button is chosen. The controller decides what happens (and whether to close).</summary>
    public required Action Activate { get; init; }
}

/// <summary>The data the overlay renders in its primary radial view.</summary>
internal sealed class RadialMenuModel
{
    public required IReadOnlyList<RadialItem> Items { get; init; }
    public string VaultStatusLine { get; init; } = string.Empty;
}

internal sealed class PanelAction
{
    public required string IconPath { get; init; }
    public required string Tooltip { get; init; }
    public required Action Invoke { get; init; }
    public bool Danger { get; init; }
}

/// <summary>One row in a list panel (clipboard history or saved passwords).</summary>
internal sealed class PanelEntry
{
    public required string Primary { get; init; }
    public string Secondary { get; init; } = string.Empty;
    public string? Monogram { get; init; }
    public string? GlyphPath { get; init; }
    /// <summary>Hidden text that still matches the search box (e.g. a URL), without being shown.</summary>
    public string SearchText { get; init; } = string.Empty;
    public Action? OnActivate { get; init; }
    public IReadOnlyList<PanelAction> Actions { get; init; } = Array.Empty<PanelAction>();
}

internal sealed class OverlayPanelModel
{
    public required string Title { get; init; }
    public bool ShowSearch { get; init; }
    public string SearchPlaceholder { get; init; } = "Search…";
    public string EmptyText { get; init; } = "Nothing here yet.";
    public string? FooterText { get; init; }
    public PanelAction? FooterAction { get; init; }
    public required IReadOnlyList<PanelEntry> Entries { get; init; }
}

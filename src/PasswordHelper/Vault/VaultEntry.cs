using System;
using PasswordHelper.Core;

namespace PasswordHelper.Vault;

/// <summary>
/// A saved password entry. Instances are immutable snapshots: edits create a new instance, which keeps
/// background saves and the UI from ever observing a half-updated entry.
/// </summary>
internal sealed class VaultEntry
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string Username { get; init; } = string.Empty;
    public required ProtectedSecret Password { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    /// <summary>Pinned entries are offered first on the radial ring.</summary>
    public bool Pinned { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }

    public string Monogram
    {
        get
        {
            foreach (char c in Title)
            {
                if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            }

            return "•";
        }
    }
}

/// <summary>Values entered in the add/edit form. Owns a pinned password buffer that <see cref="Dispose"/> wipes.</summary>
internal sealed class EntryDraft : IDisposable
{
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public char[] Password { get; set; } = Array.Empty<char>();
    public string Url { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool Pinned { get; set; }

    public void Dispose()
    {
        Sensitive.Clear(Password);
        Password = Array.Empty<char>();
    }
}

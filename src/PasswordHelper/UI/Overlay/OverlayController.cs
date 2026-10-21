using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PasswordHelper.ClipboardServices;
using PasswordHelper.Core;
using PasswordHelper.Insertion;
using PasswordHelper.Passwords;
using PasswordHelper.UI.Common;
using PasswordHelper.UI.Dialogs;
using PasswordHelper.UI.Editor;
using PasswordHelper.UI.Toast;
using PasswordHelper.Vault;

namespace PasswordHelper.UI.Overlay;

/// <summary>
/// Builds the overlay's contents and carries out the chosen actions: generating passwords, copying and inserting,
/// browsing clipboard history and saved passwords, and adding/editing entries. Owns the single live overlay window
/// and the foreground target captured when it opened. All members run on the UI thread.
/// </summary>
internal sealed class OverlayController
{
    private readonly SettingsService _settings;
    private readonly VaultService _vault;
    private readonly ClipboardManager _clipboard;
    private readonly ClipboardHistoryService _history;
    private readonly InsertionService _insertion;
    private readonly ToastService _toast;

    private OverlayWindow? _overlay;
    private ForegroundTarget? _target;
    private Func<Task>? _afterClose;

    public OverlayController(SettingsService settings, VaultService vault, ClipboardManager clipboard,
        ClipboardHistoryService history, InsertionService insertion, ToastService toast)
    {
        _settings = settings;
        _vault = vault;
        _clipboard = clipboard;
        _history = history;
        _insertion = insertion;
        _toast = toast;
    }

    public bool IsOpen => _overlay is not null;

    public void Toggle()
    {
        if (_overlay is not null)
        {
            _overlay.BeginClose();
            return;
        }

        Open();
    }

    public void CloseIfOpen() => _overlay?.BeginClose();

    private void Open()
    {
        // Capture the window the user was in BEFORE our overlay takes the foreground.
        _target = ForegroundCapture.Capture();
        _afterClose = null;

        var model = BuildRadialModel();
        var overlay = new OverlayWindow(_settings.Current);
        _overlay = overlay;
        overlay.Closed += OnOverlayClosed;

        try
        {
            overlay.Prepare(model, _settings.Current.OverlayPlacement);
            overlay.Show();
        }
        catch (Exception ex)
        {
            Log.Error("Could not show the overlay", ex);
            _overlay = null;
        }
    }

    private void OnOverlayClosed(object? sender, EventArgs e)
    {
        _overlay = null;
        Func<Task>? after = _afterClose;
        _afterClose = null;
        if (after is not null)
        {
            _ = RunAfterCloseAsync(after);
        }
    }

    private async Task RunAfterCloseAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error("Post-overlay action failed", ex);
        }
    }

    // ------------------------------------------------------------------ radial model

    private RadialMenuModel BuildRadialModel()
    {
        var items = new List<RadialItem>
        {
            new()
            {
                Kind = RadialItemKind.Generate,
                Label = "Generate",
                IconPath = Icons.Generate,
                Primary = true,
                Tooltip = "Generate a secure password",
                Activate = OnGenerate,
            },
        };

        if (_settings.Current.ClipboardHistoryEnabled)
        {
            items.Add(new RadialItem
            {
                Kind = RadialItemKind.ClipboardHistory,
                Label = "Clipboard",
                IconPath = Icons.Clipboard,
                Tooltip = "Recent clipboard items",
                Activate = ShowClipboardPanel,
            });
        }

        items.Add(new RadialItem
        {
            Kind = RadialItemKind.SavedPasswords,
            Label = "Saved",
            IconPath = Icons.SavedList,
            Tooltip = "Saved passwords",
            Activate = ShowSavedPanel,
        });

        items.Add(new RadialItem
        {
            Kind = RadialItemKind.AddPassword,
            Label = "Add",
            IconPath = Icons.Add,
            Tooltip = "Add a password",
            Activate = () => CloseThen(() => OpenEditorAsync(null)),
        });

        foreach (VaultEntry entry in OrderedSavedEntries().Take(Math.Max(0, _settings.Current.MaxRadialItems)))
        {
            VaultEntry captured = entry;
            items.Add(new RadialItem
            {
                Kind = RadialItemKind.SavedEntry,
                Label = entry.Title,
                Monogram = entry.Monogram,
                EntryId = entry.Id,
                Tooltip = string.IsNullOrEmpty(entry.Username) ? entry.Title : $"{entry.Title} — {entry.Username}",
                Activate = () => UseSavedEntry(captured),
            });
        }

        return new RadialMenuModel { Items = items };
    }

    private IEnumerable<VaultEntry> OrderedSavedEntries()
    {
        if (_vault.State != VaultState.Ready) return Enumerable.Empty<VaultEntry>();
        return _vault.Entries
            .OrderByDescending(e => e.Pinned)
            .ThenByDescending(e => e.ModifiedUtc);
    }

    // ------------------------------------------------------------------ actions

    private void OnGenerate()
    {
        PasswordPolicy policy = _settings.Current.ToPasswordPolicy();
        if (PasswordGenerator.Validate(policy) is not null)
        {
            policy = PasswordPolicy.Default;
        }

        char[] password = PasswordGenerator.Generate(policy);
        try
        {
            if (!_clipboard.SetSensitiveText(password))
            {
                _toast.Show("Could not access the clipboard", ToastKind.Warning);
                _overlay?.BeginClose();
                return;
            }
        }
        finally
        {
            Sensitive.Clear(password);
        }

        FinishWithInsertion("Password copied", "Password inserted");
    }

    private void UseSavedEntry(VaultEntry entry)
    {
        char[]? password = _vault.RevealPassword(entry.Id);
        if (password is null)
        {
            _toast.Show("That password is not available", ToastKind.Warning);
            _overlay?.BeginClose();
            return;
        }

        try
        {
            if (!_clipboard.SetSensitiveText(password))
            {
                _toast.Show("Could not access the clipboard", ToastKind.Warning);
                _overlay?.BeginClose();
                return;
            }
        }
        finally
        {
            Sensitive.Clear(password);
        }

        FinishWithInsertion($"{entry.Title} password copied", $"Pasted {entry.Title} password");
    }

    private void CopyUsername(VaultEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Username)) return;
        _clipboard.SetText(entry.Username);
        _toast.Show("Username copied");
    }

    private void UseClipboardEntry(ClipboardEntry entry)
    {
        _clipboard.SetText(entry.Text);
        FinishWithInsertion("Copied", "Pasted");
    }

    /// <summary>Closes the overlay, then (if enabled) pastes into the captured window and shows the right toast.</summary>
    private void FinishWithInsertion(string copiedMessage, string insertedMessage)
    {
        ForegroundTarget? target = _target;
        bool insert = _settings.Current.InsertIntoActiveApp;

        _afterClose = async () =>
        {
            if (!insert)
            {
                _toast.Show(copiedMessage);
                return;
            }

            InsertionResult result = await _insertion.TryPasteAsync(target);
            if (result.Outcome == InsertionOutcome.Inserted)
            {
                _toast.Show(insertedMessage);
            }
            else
            {
                _toast.Show("Copied — press Ctrl+V to paste", ToastKind.Info);
            }
        };

        _overlay?.BeginClose();
    }

    private void CloseThen(Func<Task> action)
    {
        _afterClose = action;
        _overlay?.BeginClose();
    }

    // ------------------------------------------------------------------ panels

    private void ShowClipboardPanel()
    {
        if (_overlay is null) return;
        _overlay.ShowPanel(BuildClipboardPanel());
    }

    private OverlayPanelModel BuildClipboardPanel()
    {
        var entries = new List<PanelEntry>();
        foreach (ClipboardEntry item in _history.Entries)
        {
            ClipboardEntry captured = item;
            entries.Add(new PanelEntry
            {
                Primary = item.Preview,
                Secondary = $"{item.CharacterCount} characters · {Relative(item.CapturedUtc)}",
                GlyphPath = Icons.Clipboard,
                SearchText = item.Text,
                OnActivate = () => UseClipboardEntry(captured),
                Actions = new[]
                {
                    new PanelAction
                    {
                        IconPath = Icons.Trash,
                        Tooltip = "Remove",
                        Danger = true,
                        Invoke = () => { _history.Remove(captured.Id); RefreshClipboardPanel(); },
                    },
                },
            });
        }

        return new OverlayPanelModel
        {
            Title = "Clipboard history",
            ShowSearch = true,
            SearchPlaceholder = "Search clipboard…",
            EmptyText = _settings.Current.ClipboardHistoryEnabled ? "No clipboard history yet." : "Clipboard history is turned off.",
            Entries = entries,
            FooterAction = entries.Count > 0
                ? new PanelAction { IconPath = Icons.Trash, Tooltip = "Clear history", Danger = true, Invoke = () => { _history.Clear(); RefreshClipboardPanel(); } }
                : null,
        };
    }

    private void RefreshClipboardPanel() => _overlay?.ShowPanel(BuildClipboardPanel());

    private void ShowSavedPanel()
    {
        if (_overlay is null) return;
        _overlay.ShowPanel(BuildSavedPanel());
    }

    private OverlayPanelModel BuildSavedPanel()
    {
        if (_vault.State == VaultState.Locked)
        {
            return new OverlayPanelModel
            {
                Title = "Saved passwords",
                EmptyText = "The vault is locked.",
                Entries = Array.Empty<PanelEntry>(),
                FooterAction = new PanelAction { IconPath = Icons.Unlock, Tooltip = "Unlock vault", Invoke = UnlockAndRefresh },
            };
        }

        if (_vault.State != VaultState.Ready)
        {
            return new OverlayPanelModel
            {
                Title = "Saved passwords",
                EmptyText = _vault.Problem ?? "The vault is not available.",
                Entries = Array.Empty<PanelEntry>(),
            };
        }

        var entries = new List<PanelEntry>();
        foreach (VaultEntry entry in OrderedSavedEntries())
        {
            VaultEntry captured = entry;
            var actions = new List<PanelAction>();
            if (!string.IsNullOrEmpty(entry.Username))
            {
                actions.Add(new PanelAction { IconPath = Icons.User, Tooltip = "Copy username", Invoke = () => CopyUsername(captured) });
            }

            actions.Add(new PanelAction { IconPath = Icons.Eye, Tooltip = "View details", Invoke = () => CloseThen(() => ViewDetailsAsync(captured)) });
            actions.Add(new PanelAction { IconPath = Icons.Edit, Tooltip = "Edit", Invoke = () => CloseThen(() => OpenEditorAsync(captured)) });
            actions.Add(new PanelAction { IconPath = Icons.Trash, Tooltip = "Delete", Danger = true, Invoke = () => DeleteEntry(captured) });

            entries.Add(new PanelEntry
            {
                Primary = entry.Title,
                Secondary = string.IsNullOrEmpty(entry.Username) ? (string.IsNullOrEmpty(entry.Url) ? "No username" : entry.Url) : entry.Username,
                Monogram = entry.Monogram,
                SearchText = entry.Title + " " + entry.Username + " " + entry.Url,
                OnActivate = () => UseSavedEntry(captured),
                Actions = actions,
            });
        }

        return new OverlayPanelModel
        {
            Title = "Saved passwords",
            ShowSearch = true,
            SearchPlaceholder = "Search passwords…",
            EmptyText = "No saved passwords yet.",
            Entries = entries,
            FooterAction = new PanelAction { IconPath = Icons.Add, Tooltip = "Add password", Invoke = () => CloseThen(() => OpenEditorAsync(null)) },
        };
    }

    private async void UnlockAndRefresh()
    {
        await _vault.UnlockAsync();
        if (_overlay is not null) _overlay.ShowPanel(BuildSavedPanel());
    }

    private async void DeleteEntry(VaultEntry entry)
    {
        if (_overlay is null) return;
        _overlay.SuppressAutoClose = true;
        try
        {
            bool confirmed = ConfirmDialog.Show(_overlay, "Delete password?",
                $"Delete the saved password for “{entry.Title}”? This cannot be undone.",
                "Delete", "Cancel", danger: true);
            if (!confirmed) return;

            string? error = await _vault.DeleteAsync(entry.Id);
            if (error is not null)
            {
                _toast.Show(error, ToastKind.Warning);
            }
            else
            {
                _toast.Show("Password deleted");
            }

            if (_overlay is not null) _overlay.ShowPanel(BuildSavedPanel());
        }
        finally
        {
            if (_overlay is not null)
            {
                _overlay.SuppressAutoClose = false;
                _overlay.Activate();
            }
        }
    }

    // ------------------------------------------------------------------ dialogs (overlay already closed)

    private async Task ViewDetailsAsync(VaultEntry entry)
    {
        EntryDetailsResult result = EntryDetailsDialog.Show(null, entry,
            copyUsername: () => CopyUsername(entry),
            copyPassword: () => CopyEntryPasswordToClipboard(entry));

        if (result == EntryDetailsResult.Edit)
        {
            await OpenEditorAsync(entry);
        }
        else if (result == EntryDetailsResult.Delete)
        {
            if (ConfirmDialog.Show(null, "Delete password?",
                    $"Delete the saved password for “{entry.Title}”? This cannot be undone.",
                    "Delete", "Cancel", danger: true))
            {
                string? error = await _vault.DeleteAsync(entry.Id);
                _toast.Show(error ?? "Password deleted", error is null ? ToastKind.Success : ToastKind.Warning);
            }
        }
    }

    private void CopyEntryPasswordToClipboard(VaultEntry entry)
    {
        char[]? password = _vault.RevealPassword(entry.Id);
        if (password is null) return;
        try { _clipboard.SetSensitiveText(password); } finally { Sensitive.Clear(password); }
        _toast.Show("Password copied");
    }

    private async Task OpenEditorAsync(VaultEntry? existing)
    {
        var editor = new EntryEditorWindow(_settings.Current, existing) { Topmost = true };
        editor.CenterOnOwnerOrCursor();
        bool? ok = editor.ShowDialog();
        if (ok != true || editor.Result is not EntryDraft draft)
        {
            return;
        }

        using (draft)
        {
            string? error = existing is null
                ? await _vault.AddAsync(draft)
                : await _vault.UpdateAsync(existing.Id, draft, editor.ReplacePassword);

            if (error is not null)
            {
                _toast.Show(error, ToastKind.Warning);
            }
            else
            {
                _toast.Show(existing is null ? "Password saved" : "Changes saved");
            }
        }
    }

    private static string Relative(DateTime utc)
    {
        TimeSpan ago = DateTime.UtcNow - utc;
        if (ago < TimeSpan.FromSeconds(10)) return "just now";
        if (ago < TimeSpan.FromMinutes(1)) return $"{(int)ago.TotalSeconds}s ago";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours} h ago";
        return $"{(int)ago.TotalDays} d ago";
    }
}

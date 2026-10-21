using System;
using System.Collections.Generic;
using System.Linq;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.ClipboardServices;

/// <summary>One captured clipboard text entry. Kept in memory only; never written to disk.</summary>
internal sealed class ClipboardEntry
{
    public required Guid Id { get; init; }
    public required string Text { get; init; }
    public required DateTime CapturedUtc { get; init; }

    /// <summary>Single-line, length-limited preview for the UI (the full text is never shown in tooltips/logs).</summary>
    public string Preview
    {
        get
        {
            string collapsed = Text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (collapsed.Contains("  ")) collapsed = collapsed.Replace("  ", " ");
            return collapsed.Length <= 64 ? collapsed : collapsed[..63] + "…";
        }
    }

    public int CharacterCount => Text.Length;
}

/// <summary>
/// Event-driven clipboard history. Subscribes to clipboard-update notifications (no polling), captures ordinary
/// text, and keeps a bounded, in-memory, newest-first list. Content marked as excluded from monitoring (our own
/// password copies, and other apps' sensitive copies) and content owned by ignored applications are skipped.
/// </summary>
internal sealed class ClipboardHistoryService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly SettingsService _settings;
    private readonly List<ClipboardEntry> _entries = new();
    private bool _listening;
    private bool _disposed;

    public ClipboardHistoryService(MessageWindow window, SettingsService settings)
    {
        _window = window;
        _settings = settings;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ClipboardEntry> Entries => _entries;

    public void Start()
    {
        if (_listening) return;
        if (Win32.AddClipboardFormatListener(_window.Handle))
        {
            _listening = true;
            _window.ClipboardUpdated += OnClipboardUpdated;
        }
        else
        {
            Log.Warn("Could not register the clipboard listener.");
        }
    }

    private void OnClipboardUpdated()
    {
        if (_disposed || !_settings.Current.ClipboardHistoryEnabled) return;

        // Skip anything flagged as sensitive by us or by another application.
        if (ClipboardText.IsMonitoringExcluded()) return;
        if (!ClipboardText.IsTextAvailable()) return;

        if (IsOwnerIgnored()) return;

        if (!ClipboardText.TryGetText(_window.Handle, out string text) || string.IsNullOrEmpty(text)) return;
        if (text.Length > 100_000) return; // keep the history light; skip very large blobs

        Capture(text);
    }

    private bool IsOwnerIgnored()
    {
        uint pid = ClipboardText.GetOwnerProcessId();
        // pid 0: owner unknown. Our own process: ordinary copies (usernames, re-copied entries) are allowed in.
        if (pid == 0 || pid == ProcessUtil.CurrentProcessId) return false;

        string exe = ProcessUtil.GetImageFileName(pid);
        if (exe.Length == 0) return false;

        foreach (string ignored in _settings.Current.ClipboardIgnoredApps)
        {
            string candidate = ignored.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ignored : ignored + ".exe";
            if (string.Equals(candidate, exe, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void Capture(string text)
    {
        // Remove an existing identical entry so the list never holds duplicates and the newest copy wins.
        _entries.RemoveAll(e => string.Equals(e.Text, text, StringComparison.Ordinal));
        _entries.Insert(0, new ClipboardEntry { Id = Guid.NewGuid(), Text = text, CapturedUtc = DateTime.UtcNow });

        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Trim()
    {
        int limit = _settings.Current.ClipboardHistoryLimit;
        if (_entries.Count > limit)
        {
            _entries.RemoveRange(limit, _entries.Count - limit);
        }
    }

    public ClipboardEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);

    public void Remove(Guid id)
    {
        if (_entries.RemoveAll(e => e.Id == id) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        Log.Info("Clipboard history cleared.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_listening)
        {
            _window.ClipboardUpdated -= OnClipboardUpdated;
            Win32.RemoveClipboardFormatListener(_window.Handle);
            _listening = false;
        }

        _entries.Clear();
    }
}

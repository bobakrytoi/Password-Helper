using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PasswordHelper.Core;

namespace PasswordHelper.Vault;

internal enum VaultState
{
    Loading,
    Ready,
    Locked,
    /// <summary>The vault file exists but cannot be used (damaged, foreign, newer). Nothing is written in this state.</summary>
    Unavailable,
}

/// <summary>
/// The in-memory vault and its lifecycle. All members are used from the UI thread; decryption and file I/O run
/// on the thread pool. Changes are persisted before they become visible, so the UI never shows unsaved data.
/// </summary>
internal sealed class VaultService : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<VaultEntry> _entries = new();
    private int _version; // bumped by every load/lock so in-flight work can detect it was superseded

    public VaultService(string path)
    {
        _path = path;
    }

    public VaultState State { get; private set; } = VaultState.Loading;

    /// <summary>User-facing explanation when <see cref="State"/> is <see cref="VaultState.Unavailable"/>.</summary>
    public string? Problem { get; private set; }

    public bool BackupAvailable { get; private set; }

    public string FilePath => _path;

    public IReadOnlyList<VaultEntry> Entries => _entries;

    public event EventHandler? Changed;

    /// <summary>Raised whenever a stored secret is used; drives the inactivity auto-lock.</summary>
    public event EventHandler? Used;

    public VaultEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);

    public async Task LoadAsync()
    {
        int version = ++_version;
        ReplaceEntries(new List<VaultEntry>());
        SetState(VaultState.Loading, null);

        VaultLoadResult result;
        try
        {
            result = await Task.Run(() => VaultStore.Load(_path));
        }
        catch (Exception ex)
        {
            Log.Error("Unexpected failure while loading the vault", ex, includeExceptionMessage: false);
            result = VaultLoadResult.Fail(VaultLoadStatus.AccessError, "an unexpected error occurred.");
        }

        if (version != _version)
        {
            VaultStore.DisposeEntries(result.Entries); // locked or reloaded meanwhile
            return;
        }

        BackupAvailable = result.BackupAvailable;
        if (result.RecoveryNote is not null) Log.Warn(result.RecoveryNote);

        switch (result.Status)
        {
            case VaultLoadStatus.Loaded:
                ReplaceEntries(result.Entries);
                SetState(VaultState.Ready, null);
                break;
            case VaultLoadStatus.NotFound:
                SetState(VaultState.Ready, null);
                break;
            case VaultLoadStatus.MissingWithBackup:
                SetState(VaultState.Unavailable, "The vault file is missing, but a backup copy exists.");
                break;
            case VaultLoadStatus.Damaged:
                SetState(VaultState.Unavailable, $"The vault file is damaged: {result.Detail} It has not been modified.");
                break;
            case VaultLoadStatus.CannotDecrypt:
                SetState(VaultState.Unavailable, $"{result.Detail} The vault file has not been modified.");
                break;
            case VaultLoadStatus.UnsupportedVersion:
                SetState(VaultState.Unavailable, result.Detail);
                break;
            default:
                SetState(VaultState.Unavailable, $"The vault file could not be read: {result.Detail}");
                break;
        }

        if (State == VaultState.Unavailable) Log.Warn($"Vault unavailable ({result.Status})");
    }

    public void Lock()
    {
        if (State is VaultState.Locked or VaultState.Unavailable) return;
        _version++;
        ReplaceEntries(new List<VaultEntry>());
        SetState(VaultState.Locked, null);
        Log.Info("Vault locked");
    }

    public Task UnlockAsync() => State == VaultState.Locked ? LoadAsync() : Task.CompletedTask;

    public void NotifyUsed() => Used?.Invoke(this, EventArgs.Empty);

    /// <summary>Returns the password in a pinned buffer that the caller must clear, or null if unavailable.</summary>
    public char[]? RevealPassword(Guid id)
    {
        VaultEntry? entry = Find(id);
        if (entry is null) return null;
        NotifyUsed();
        return entry.Password.Reveal();
    }

    public Task<string?> AddAsync(EntryDraft draft) => CommitAsync(current =>
    {
        DateTime now = DateTime.UtcNow;
        var entry = new VaultEntry
        {
            Id = Guid.NewGuid(),
            Title = draft.Title.Trim(),
            Username = draft.Username.Trim(),
            Password = ProtectedSecret.FromChars(draft.Password),
            Url = draft.Url.Trim(),
            Notes = draft.Notes,
            Pinned = draft.Pinned,
            CreatedUtc = now,
            ModifiedUtc = now,
        };
        var next = new List<VaultEntry>(current) { entry };
        return new Change(next, Array.Empty<VaultEntry>(), new[] { entry });
    });

    /// <summary>Updates an entry. When <paramref name="replacePassword"/> is false the stored password is kept.</summary>
    public Task<string?> UpdateAsync(Guid id, EntryDraft draft, bool replacePassword) => CommitAsync(current =>
    {
        int index = current.FindIndex(e => e.Id == id);
        if (index < 0) return null;

        VaultEntry old = current[index];
        ProtectedSecret password = replacePassword ? ProtectedSecret.FromChars(draft.Password) : old.Password.Clone();
        var updated = new VaultEntry
        {
            Id = old.Id,
            Title = draft.Title.Trim(),
            Username = draft.Username.Trim(),
            Password = password,
            Url = draft.Url.Trim(),
            Notes = draft.Notes,
            Pinned = draft.Pinned,
            CreatedUtc = old.CreatedUtc,
            ModifiedUtc = DateTime.UtcNow,
        };
        var next = new List<VaultEntry>(current) { [index] = updated };
        return new Change(next, new[] { old }, new[] { updated });
    });

    public Task<string?> DeleteAsync(Guid id) => CommitAsync(current =>
    {
        int index = current.FindIndex(e => e.Id == id);
        if (index < 0) return null;
        var next = new List<VaultEntry>(current);
        VaultEntry removed = next[index];
        next.RemoveAt(index);
        return new Change(next, new[] { removed }, Array.Empty<VaultEntry>());
    });

    /// <summary>Restores the last good copy. The current (unreadable) file is renamed, never deleted.</summary>
    public async Task<string?> RestoreBackupAsync()
    {
        string backup = VaultStore.BackupPath(_path);
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(backup)) return "No backup copy is available.";

            VaultLoadResult check = await Task.Run(() => VaultStore.ReadFile(backup));
            VaultStore.DisposeEntries(check.Entries);
            if (check.Status != VaultLoadStatus.Loaded)
            {
                return $"The backup copy cannot be used either: {check.Detail}";
            }

            await Task.Run(() =>
            {
                if (File.Exists(_path)) File.Move(_path, DamagedName(_path));
                File.Copy(backup, _path, overwrite: false);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Restoring the vault backup failed", ex);
            return $"The backup could not be restored: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }

        await LoadAsync();
        return null;
    }

    /// <summary>Starts an empty vault, moving the unreadable file (and its backup) aside so nothing is lost.</summary>
    public async Task<string?> StartNewVaultAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                string backup = VaultStore.BackupPath(_path);
                if (File.Exists(_path)) File.Move(_path, DamagedName(_path));
                if (File.Exists(backup)) File.Move(backup, DamagedName(backup));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not move the unreadable vault aside", ex);
            return $"The existing vault file could not be moved aside: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }

        await LoadAsync();
        return null;
    }

    /// <summary>Permanently deletes the vault file, its backup and any temporary file.</summary>
    public async Task<string?> DeleteAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _version++;
            await Task.Run(() =>
            {
                foreach (string file in new[] { _path, VaultStore.BackupPath(_path), VaultStore.TempPath(_path) })
                {
                    if (File.Exists(file)) File.Delete(file);
                }
            });
            ReplaceEntries(new List<VaultEntry>());
            BackupAvailable = false;
            SetState(VaultState.Ready, null);
            Log.Info("Vault data deleted by the user");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Deleting vault data failed", ex);
            return $"Vault data could not be deleted: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record Change(List<VaultEntry> Next, IReadOnlyList<VaultEntry> Discarded, IReadOnlyList<VaultEntry> Created);

    private async Task<string?> CommitAsync(Func<List<VaultEntry>, Change?> build)
    {
        await _gate.WaitAsync();
        try
        {
            if (State != VaultState.Ready) return "The vault is not available right now.";

            int version = _version;
            Change? change = build(_entries);
            if (change is null) return "The entry no longer exists.";

            byte[] plaintext = VaultStore.Serialize(change.Next, DateTime.UtcNow);
            try
            {
                await Task.Run(() => VaultStore.WriteEncrypted(_path, plaintext));
            }
            catch (Exception ex)
            {
                Log.Error("Saving the vault failed", ex);
                VaultStore.DisposeEntries(change.Created);
                return $"The vault could not be saved. {ex.Message}";
            }
            finally
            {
                Sensitive.Clear(plaintext);
            }

            if (version != _version)
            {
                // Locked while saving: the data is on disk, but nothing may stay decrypted in memory.
                VaultStore.DisposeEntries(change.Next);
                return null;
            }

            _entries = change.Next;
            VaultStore.DisposeEntries(change.Discarded);
            BackupAvailable = BackupAvailable || File.Exists(VaultStore.BackupPath(_path));
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReplaceEntries(List<VaultEntry> entries)
    {
        List<VaultEntry> old = _entries;
        _entries = entries;
        VaultStore.DisposeEntries(old);
    }

    private void SetState(VaultState state, string? problem)
    {
        State = state;
        Problem = problem;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string DamagedName(string path) =>
        $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";

    public void Dispose()
    {
        _version++;
        ReplaceEntries(new List<VaultEntry>());
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PasswordHelper.Core;
using PasswordHelper.Vault;
using Xunit;

namespace PasswordHelper.Tests;

public class ProtectedSecretTests
{
    [Theory]
    [InlineData("hunter2")]
    [InlineData("")]
    [InlineData("a-very-long-password-well-beyond-one-block-of-sixteen-bytes!!")]
    [InlineData("exact16chars!!!!")]
    public void FromChars_Reveal_RoundTrips(string value)
    {
        using var secret = ProtectedSecret.FromChars(value);
        char[] revealed = secret.Reveal();
        try
        {
            Assert.Equal(value, new string(revealed));
            Assert.Equal(value.Length, secret.Length);
        }
        finally
        {
            Sensitive.Clear(revealed);
        }
    }

    [Fact]
    public void Clone_ProducesIndependentButEqualSecret()
    {
        using var original = ProtectedSecret.FromChars("Steam#2026");
        using ProtectedSecret copy = original.Clone();
        char[] a = original.Reveal();
        char[] b = copy.Reveal();
        try
        {
            Assert.Equal(new string(a), new string(b));
        }
        finally
        {
            Sensitive.Clear(a);
            Sensitive.Clear(b);
        }
    }
}

public class VaultStoreTests
{
    private static List<VaultEntry> SampleEntries()
    {
        DateTime now = DateTime.UtcNow;
        return new List<VaultEntry>
        {
            new() { Id = Guid.NewGuid(), Title = "Steam", Username = "gamer", Password = ProtectedSecret.FromChars("p@ss-A1"), Url = "store.steampowered.com", Notes = "note", Pinned = true, CreatedUtc = now, ModifiedUtc = now },
            new() { Id = Guid.NewGuid(), Title = "Email", Username = "me@example.com", Password = ProtectedSecret.FromChars("Zy9!longpassword"), CreatedUtc = now, ModifiedUtc = now },
        };
    }

    [Fact]
    public void WriteThenLoad_RoundTripsEntries()
    {
        string path = NewVaultPath();
        List<VaultEntry> entries = SampleEntries();
        try
        {
            byte[] plaintext = VaultStore.Serialize(entries, DateTime.UtcNow);
            try { VaultStore.WriteEncrypted(path, plaintext); } finally { Sensitive.Clear(plaintext); }

            VaultLoadResult result = VaultStore.Load(path);
            Assert.Equal(VaultLoadStatus.Loaded, result.Status);
            Assert.Equal(2, result.Entries.Count);

            VaultEntry steam = result.Entries.Find(e => e.Title == "Steam")!;
            Assert.True(steam.Pinned);
            Assert.Equal("gamer", steam.Username);
            char[] pw = steam.Password.Reveal();
            try { Assert.Equal("p@ss-A1", new string(pw)); } finally { Sensitive.Clear(pw); }

            VaultStore.DisposeEntries(result.Entries);
        }
        finally
        {
            VaultStore.DisposeEntries(entries);
            Cleanup(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReportsNotFound()
    {
        string path = NewVaultPath();
        VaultLoadResult result = VaultStore.Load(path);
        Assert.Equal(VaultLoadStatus.NotFound, result.Status);
    }

    [Fact]
    public void Load_TamperedPayload_IsDetectedAsDamaged_NotEmptied()
    {
        string path = NewVaultPath();
        List<VaultEntry> entries = SampleEntries();
        try
        {
            byte[] plaintext = VaultStore.Serialize(entries, DateTime.UtcNow);
            try { VaultStore.WriteEncrypted(path, plaintext); } finally { Sensitive.Clear(plaintext); }

            // Flip a byte in the encrypted payload (past the 48-byte header).
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            VaultLoadResult result = VaultStore.Load(path);
            Assert.Equal(VaultLoadStatus.Damaged, result.Status);
            Assert.True(File.Exists(path)); // the unreadable file is never destroyed
        }
        finally
        {
            VaultStore.DisposeEntries(entries);
            Cleanup(path);
        }
    }

    [Fact]
    public void Load_TruncatedFile_IsDamaged()
    {
        string path = NewVaultPath();
        List<VaultEntry> entries = SampleEntries();
        try
        {
            byte[] plaintext = VaultStore.Serialize(entries, DateTime.UtcNow);
            try { VaultStore.WriteEncrypted(path, plaintext); } finally { Sensitive.Clear(plaintext); }

            byte[] bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

            VaultLoadResult result = VaultStore.Load(path);
            Assert.Equal(VaultLoadStatus.Damaged, result.Status);
        }
        finally
        {
            VaultStore.DisposeEntries(entries);
            Cleanup(path);
        }
    }

    [Fact]
    public void WriteEncrypted_CreatesBackupOnSecondWrite()
    {
        string path = NewVaultPath();
        List<VaultEntry> entries = SampleEntries();
        try
        {
            byte[] first = VaultStore.Serialize(entries, DateTime.UtcNow);
            try { VaultStore.WriteEncrypted(path, first); } finally { Sensitive.Clear(first); }
            byte[] second = VaultStore.Serialize(entries, DateTime.UtcNow);
            try { VaultStore.WriteEncrypted(path, second); } finally { Sensitive.Clear(second); }

            Assert.True(File.Exists(VaultStore.BackupPath(path)));
        }
        finally
        {
            VaultStore.DisposeEntries(entries);
            Cleanup(path);
        }
    }

    private static string NewVaultPath() =>
        Path.Combine(Path.GetTempPath(), "phtests", Guid.NewGuid().ToString("N"), "vault.dat");

    private static void Cleanup(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* ignore */ }
    }
}

public class VaultServiceTests
{
    [Fact]
    public async Task AddUpdateDelete_PersistsAcrossReload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "phtests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "vault.dat");
        try
        {
            using (var vault = new VaultService(path))
            {
                await vault.LoadAsync();
                Assert.Equal(VaultState.Ready, vault.State);

                using var draft = new EntryDraft { Title = "Discord", Username = "user", Password = "Secret#1".ToCharArray() };
                Assert.Null(await vault.AddAsync(draft));
                Assert.Single(vault.Entries);
            }

            // Reload in a fresh service: the entry must still be there and decrypt correctly.
            using (var reopened = new VaultService(path))
            {
                await reopened.LoadAsync();
                Assert.Single(reopened.Entries);
                VaultEntry entry = reopened.Entries[0];
                Assert.Equal("Discord", entry.Title);
                char[] pw = reopened.RevealPassword(entry.Id)!;
                try { Assert.Equal("Secret#1", new string(pw)); } finally { Sensitive.Clear(pw); }

                using var edit = new EntryDraft { Title = "Discord", Username = "user2", Password = Array.Empty<char>() };
                Assert.Null(await reopened.UpdateAsync(entry.Id, edit, replacePassword: false));
                Assert.Equal("user2", reopened.Entries[0].Username);

                Assert.Null(await reopened.DeleteAsync(entry.Id));
                Assert.Empty(reopened.Entries);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Lock_ClearsEntriesUntilUnlocked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "phtests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "vault.dat");
        try
        {
            using var vault = new VaultService(path);
            await vault.LoadAsync();
            using var draft = new EntryDraft { Title = "Work", Password = "abc123XYZ".ToCharArray() };
            await vault.AddAsync(draft);

            vault.Lock();
            Assert.Equal(VaultState.Locked, vault.State);
            Assert.Empty(vault.Entries);

            await vault.UnlockAsync();
            Assert.Equal(VaultState.Ready, vault.State);
            Assert.Single(vault.Entries);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

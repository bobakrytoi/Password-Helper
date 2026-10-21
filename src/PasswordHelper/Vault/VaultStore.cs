using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using PasswordHelper.Core;

namespace PasswordHelper.Vault;

internal enum VaultLoadStatus
{
    Loaded,
    NotFound,
    MissingWithBackup,
    Damaged,
    CannotDecrypt,
    UnsupportedVersion,
    AccessError,
}

internal sealed class VaultLoadResult
{
    public required VaultLoadStatus Status { get; init; }
    public List<VaultEntry> Entries { get; init; } = new();
    public DateTime SavedUtc { get; init; }
    public string? Detail { get; init; }
    public bool BackupAvailable { get; set; }
    public string? RecoveryNote { get; set; }

    public static VaultLoadResult Fail(VaultLoadStatus status, string detail) => new() { Status = status, Detail = detail };
}

/// <summary>
/// On-disk format of the vault.
/// <code>
/// offset  size  field
///      0     8  magic "PHVAULT\0"
///      8     2  file format version (little endian)
///     10     2  reserved (0)
///     12     4  payload length
///     16    32  SHA-256 of the payload (detects truncation/corruption before decryption)
///     48     n  payload: DPAPI (current user) encrypted UTF-8 JSON { schemaVersion, savedUtc, entries[] }
/// </code>
/// Writes go to a temporary file that is flushed, verified and then atomically swapped in with ReplaceFile,
/// keeping the previous version as <c>.bak</c>. An unreadable vault is never replaced or emptied automatically.
/// </summary>
internal static class VaultStore
{
    public const int SchemaVersion = 1;
    private const ushort FormatVersion = 1;
    private const int HeaderSize = 48;
    private static ReadOnlySpan<byte> Magic => "PHVAULT\0"u8;

    public static string BackupPath(string path) => path + ".bak";
    public static string TempPath(string path) => path + ".tmp";

    public static VaultLoadResult Load(string path)
    {
        string temp = TempPath(path);
        bool backupExists = File.Exists(BackupPath(path));

        if (!File.Exists(path))
        {
            if (File.Exists(temp))
            {
                VaultLoadResult pending = ReadFile(temp);
                if (pending.Status == VaultLoadStatus.Loaded)
                {
                    try
                    {
                        File.Move(temp, path);
                        pending.RecoveryNote = "Recovered the vault from an interrupted save.";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Warn("Could not promote the pending vault file", ex);
                    }

                    pending.BackupAvailable = backupExists;
                    return pending;
                }
            }

            return backupExists
                ? new VaultLoadResult { Status = VaultLoadStatus.MissingWithBackup, BackupAvailable = true }
                : new VaultLoadResult { Status = VaultLoadStatus.NotFound };
        }

        VaultLoadResult main = ReadFile(path);
        if (main.Status == VaultLoadStatus.Loaded && File.Exists(temp))
        {
            VaultLoadResult pending = ReadFile(temp);
            if (pending.Status == VaultLoadStatus.Loaded && pending.SavedUtc > main.SavedUtc)
            {
                // The previous run crashed after the new file was fully written but before it was swapped in.
                try
                {
                    File.Replace(temp, path, BackupPath(path), ignoreMetadataErrors: true);
                    DisposeEntries(main.Entries);
                    pending.RecoveryNote = "Completed a vault save that was interrupted.";
                    pending.BackupAvailable = true;
                    return pending;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                {
                    Log.Warn("Could not complete the interrupted vault save", ex);
                    DisposeEntries(pending.Entries);
                }
            }
            else
            {
                DisposeEntries(pending.Entries);
                if (pending.Status is VaultLoadStatus.Loaded or VaultLoadStatus.Damaged)
                {
                    TryDelete(temp); // stale or partially written leftover; the main file is intact
                }
            }
        }

        main.BackupAvailable = backupExists;
        return main;
    }

    internal static VaultLoadResult ReadFile(string path)
    {
        byte[] file;
        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VaultLoadResult.Fail(VaultLoadStatus.AccessError, ex.Message);
        }

        if (file.Length < HeaderSize || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return VaultLoadResult.Fail(VaultLoadStatus.Damaged, "the file is not a Password helper vault.");
        }

        ushort format = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(8));
        if (format == 0 || format > FormatVersion)
        {
            return VaultLoadResult.Fail(VaultLoadStatus.UnsupportedVersion,
                $"The vault uses file format {format}, which this version of Password helper cannot read.");
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12));
        if ((long)HeaderSize + payloadLength != file.Length)
        {
            return VaultLoadResult.Fail(VaultLoadStatus.Damaged, "the file is truncated or has unexpected data.");
        }

        ReadOnlySpan<byte> payload = file.AsSpan(HeaderSize);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, file.AsSpan(16, 32)))
        {
            return VaultLoadResult.Fail(VaultLoadStatus.Damaged, "the file failed its integrity check.");
        }

        byte[] plaintext;
        try
        {
            plaintext = Dpapi.Unprotect(payload);
        }
        catch (CryptographicException ex)
        {
            return VaultLoadResult.Fail(VaultLoadStatus.CannotDecrypt, DescribeDpapiError(ex.HResult));
        }

        try
        {
            var (entries, schema, saved) = Deserialize(plaintext);
            if (schema > SchemaVersion)
            {
                DisposeEntries(entries);
                return VaultLoadResult.Fail(VaultLoadStatus.UnsupportedVersion,
                    $"The vault was created by a newer version of Password helper (schema {schema}). It has not been modified.");
            }

            return new VaultLoadResult { Status = VaultLoadStatus.Loaded, Entries = entries, SavedUtc = saved };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            // The exception message could quote decrypted content, so only its type is logged.
            Log.Warn("Vault contents could not be parsed", ex, includeExceptionMessage: false);
            return VaultLoadResult.Fail(VaultLoadStatus.Damaged, "its decrypted contents could not be read.");
        }
        finally
        {
            Sensitive.Clear(plaintext);
        }
    }

    /// <summary>
    /// Serializes entries to UTF-8 JSON in a pinned buffer that the caller must clear. Passwords are decrypted one
    /// at a time straight into the writer, so no managed string ever holds them.
    /// </summary>
    public static byte[] Serialize(IReadOnlyList<VaultEntry> entries, DateTime savedUtc)
    {
        using var buffer = new SensitiveBufferWriter(4096 + entries.Count * 512);
        var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("savedUtc", savedUtc);
            writer.WriteStartArray("entries");
            foreach (VaultEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("title", entry.Title);
                writer.WriteString("username", entry.Username);
                char[] password = entry.Password.Reveal();
                try
                {
                    writer.WriteString("password", (ReadOnlySpan<char>)password);
                }
                finally
                {
                    Sensitive.Clear(password);
                }

                writer.WriteString("url", entry.Url);
                writer.WriteString("notes", entry.Notes);
                writer.WriteBoolean("pinned", entry.Pinned);
                writer.WriteString("createdUtc", entry.CreatedUtc);
                writer.WriteString("modifiedUtc", entry.ModifiedUtc);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.ToPinnedArray();
    }

    internal static (List<VaultEntry> Entries, int Schema, DateTime SavedUtc) Deserialize(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 16 });
        var entries = new List<VaultEntry>();
        int schema = 0;
        DateTime saved = default;

        try
        {
            Expect(reader.Read() && reader.TokenType == JsonTokenType.StartObject);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("schemaVersion"))
                {
                    reader.Read();
                    schema = reader.GetInt32();
                }
                else if (reader.ValueTextEquals("savedUtc"))
                {
                    reader.Read();
                    saved = reader.GetDateTime();
                }
                else if (reader.ValueTextEquals("entries"))
                {
                    reader.Read();
                    Expect(reader.TokenType == JsonTokenType.StartArray);
                    while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                    {
                        entries.Add(ReadEntry(ref reader));
                    }

                    Expect(reader.TokenType == JsonTokenType.EndArray);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            Expect(reader.TokenType == JsonTokenType.EndObject);
            Expect(schema >= 1);
        }
        catch
        {
            DisposeEntries(entries);
            throw;
        }

        return (entries, schema, saved);
    }

    private static VaultEntry ReadEntry(ref Utf8JsonReader reader)
    {
        Guid id = Guid.Empty;
        string title = string.Empty, username = string.Empty, url = string.Empty, notes = string.Empty;
        bool pinned = false;
        DateTime created = default, modified = default;
        ProtectedSecret? password = null;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("password"))
                {
                    reader.Read();
                    Expect(reader.TokenType == JsonTokenType.String);
                    int capacity = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
                    char[] chars = Sensitive.AllocChars(capacity);
                    try
                    {
                        int written = reader.CopyString(chars);
                        password?.Dispose();
                        password = ProtectedSecret.FromChars(chars.AsSpan(0, written));
                    }
                    finally
                    {
                        Sensitive.Clear(chars);
                    }
                }
                else if (reader.ValueTextEquals("id")) { reader.Read(); id = reader.GetGuid(); }
                else if (reader.ValueTextEquals("title")) { reader.Read(); title = reader.GetString() ?? string.Empty; }
                else if (reader.ValueTextEquals("username")) { reader.Read(); username = reader.GetString() ?? string.Empty; }
                else if (reader.ValueTextEquals("url")) { reader.Read(); url = reader.GetString() ?? string.Empty; }
                else if (reader.ValueTextEquals("notes")) { reader.Read(); notes = reader.GetString() ?? string.Empty; }
                else if (reader.ValueTextEquals("pinned")) { reader.Read(); pinned = reader.GetBoolean(); }
                else if (reader.ValueTextEquals("createdUtc")) { reader.Read(); created = reader.GetDateTime(); }
                else if (reader.ValueTextEquals("modifiedUtc")) { reader.Read(); modified = reader.GetDateTime(); }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            Expect(reader.TokenType == JsonTokenType.EndObject);
            Expect(id != Guid.Empty && password is not null);

            return new VaultEntry
            {
                Id = id,
                Title = title,
                Username = username,
                Password = password!,
                Url = url,
                Notes = notes,
                Pinned = pinned,
                CreatedUtc = DateTime.SpecifyKind(created, DateTimeKind.Utc),
                ModifiedUtc = DateTime.SpecifyKind(modified, DateTimeKind.Utc),
            };
        }
        catch
        {
            password?.Dispose();
            throw;
        }
    }

    /// <summary>Encrypts the plaintext and atomically replaces the vault file, keeping the previous one as .bak.</summary>
    public static void WriteEncrypted(string path, ReadOnlySpan<byte> plaintext)
    {
        byte[] payload = Dpapi.Protect(plaintext);
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)payload.Length);
        SHA256.HashData(payload, header.AsSpan(16, 32));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = TempPath(path);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(header);
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }

        VerifyWritten(temp, header, payload);

        if (File.Exists(path))
        {
            try
            {
                File.Replace(temp, path, BackupPath(path), ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // ReplaceFile is unavailable on some file systems; fall back to copy + move.
                Log.Warn("Atomic replace failed; using copy and move", ex);
                if (File.Exists(path)) File.Copy(path, BackupPath(path), overwrite: true);
                File.Move(temp, path, overwrite: true);
            }
        }
        else
        {
            File.Move(temp, path);
        }
    }

    public static void DisposeEntries(IEnumerable<VaultEntry> entries)
    {
        foreach (VaultEntry entry in entries)
        {
            entry.Password.Dispose();
        }
    }

    private static void VerifyWritten(string path, ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        byte[] written = File.ReadAllBytes(path);
        if (written.Length != header.Length + payload.Length ||
            !written.AsSpan(0, header.Length).SequenceEqual(header) ||
            !written.AsSpan(header.Length).SequenceEqual(payload))
        {
            throw new IOException("The vault file could not be verified after writing.");
        }
    }

    private static string DescribeDpapiError(int hresult)
    {
        const int NTE_BAD_KEY_STATE = unchecked((int)0x8009000B);
        const int ERROR_INVALID_DATA = 13;
        if (hresult == NTE_BAD_KEY_STATE)
        {
            return "The vault was encrypted by a different Windows account or on another PC, or this account's credentials were reset.";
        }

        if ((hresult & 0xFFFF) == ERROR_INVALID_DATA)
        {
            return "Windows reported that the encrypted data is invalid.";
        }

        return $"Windows could not decrypt the vault (error 0x{hresult:X8}).";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not delete a leftover vault file", ex);
        }
    }

    private static void Expect(bool condition)
    {
        if (!condition) throw new JsonException("Unexpected vault structure.");
    }
}

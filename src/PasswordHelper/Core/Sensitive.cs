using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PasswordHelper.Core;

/// <summary>
/// Helpers for buffers that hold secrets. Buffers are allocated on the pinned object heap so the GC never
/// moves (and thereby copies) them, which makes zeroing them afterwards meaningful.
/// Managed strings are immutable and cannot be wiped, so secret-handling code avoids them where practical;
/// this is best-effort hygiene, not a guarantee that no copy of a secret ever remains in memory.
/// </summary>
internal static class Sensitive
{
    public static char[] AllocChars(int length) => GC.AllocateArray<char>(length, pinned: true);

    public static byte[] AllocBytes(int length) => GC.AllocateArray<byte>(length, pinned: true);

    public static void Clear(char[]? buffer)
    {
        if (buffer is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));
    }

    public static void Clear(byte[]? buffer)
    {
        if (buffer is not null) CryptographicOperations.ZeroMemory(buffer);
    }

    public static char[] Copy(ReadOnlySpan<char> source)
    {
        char[] copy = AllocChars(source.Length);
        source.CopyTo(copy);
        return copy;
    }
}

/// <summary>
/// <see cref="IBufferWriter{T}"/> backed by pinned arrays that are zeroed whenever they are discarded.
/// Used to serialize the vault so the plaintext JSON never lands in an unpinned or pooled buffer.
/// </summary>
internal sealed class SensitiveBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    public SensitiveBufferWriter(int initialCapacity = 16 * 1024)
    {
        _buffer = Sensitive.AllocBytes(Math.Max(256, initialCapacity));
    }

    public int WrittenCount => _written;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>Copies the written bytes into a new pinned array owned by the caller (who must clear it).</summary>
    public byte[] ToPinnedArray()
    {
        byte[] copy = Sensitive.AllocBytes(_written);
        WrittenSpan.CopyTo(copy);
        return copy;
    }

    private void Ensure(int sizeHint)
    {
        if (sizeHint < 1) sizeHint = 1;
        if (_buffer.Length - _written >= sizeHint) return;

        byte[] larger = Sensitive.AllocBytes(Math.Max(_buffer.Length * 2, _written + sizeHint));
        _buffer.AsSpan(0, _written).CopyTo(larger);
        Sensitive.Clear(_buffer);
        _buffer = larger;
    }

    public void Dispose()
    {
        Sensitive.Clear(_buffer);
        _written = 0;
    }
}

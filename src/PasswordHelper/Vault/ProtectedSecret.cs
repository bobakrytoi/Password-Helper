using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.Vault;

/// <summary>
/// A secret kept encrypted in process memory with <c>CryptProtectMemory</c> (same-process scope), so a memory
/// dump or swap file does not contain it in plaintext while the vault sits idle. It is decrypted only for the
/// moment it is needed, into a pinned buffer that the caller wipes.
/// </summary>
internal sealed class ProtectedSecret : IDisposable
{
    private readonly object _gate = new();
    private readonly int _length;
    private byte[]? _cipher;

    private ProtectedSecret(byte[] cipher, int length)
    {
        _cipher = cipher;
        _length = length;
    }

    /// <summary>Number of characters in the secret.</summary>
    public int Length => _length;

    public bool IsEmpty => _length == 0;

    public static unsafe ProtectedSecret FromChars(ReadOnlySpan<char> chars)
    {
        int byteCount = chars.Length * sizeof(char);
        int blockSize = Win32.CRYPTPROTECTMEMORY_BLOCK_SIZE;
        int padded = Math.Max(blockSize, (byteCount + blockSize - 1) / blockSize * blockSize);

        byte[] buffer = Sensitive.AllocBytes(padded);
        MemoryMarshal.AsBytes(chars).CopyTo(buffer);

        fixed (byte* p = buffer)
        {
            if (!Win32.CryptProtectMemory((IntPtr)p, (uint)padded, Win32.CRYPTPROTECTMEMORY_SAME_PROCESS))
            {
                int error = Marshal.GetLastPInvokeError();
                Sensitive.Clear(buffer);
                throw new Win32Exception(error, "CryptProtectMemory failed.");
            }
        }

        return new ProtectedSecret(buffer, chars.Length);
    }

    /// <summary>Returns the plaintext in a new pinned buffer. The caller must wipe it with <see cref="Sensitive.Clear(char[])"/>.</summary>
    public unsafe char[] Reveal()
    {
        lock (_gate)
        {
            byte[] cipher = _cipher ?? throw new ObjectDisposedException(nameof(ProtectedSecret));
            byte[] work = Sensitive.AllocBytes(cipher.Length);
            try
            {
                cipher.CopyTo(work, 0);
                fixed (byte* p = work)
                {
                    if (!Win32.CryptUnprotectMemory((IntPtr)p, (uint)work.Length, Win32.CRYPTPROTECTMEMORY_SAME_PROCESS))
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "CryptUnprotectMemory failed.");
                    }
                }

                char[] result = Sensitive.AllocChars(_length);
                MemoryMarshal.Cast<byte, char>(work.AsSpan(0, _length * sizeof(char))).CopyTo(result);
                return result;
            }
            finally
            {
                Sensitive.Clear(work);
            }
        }
    }

    /// <summary>Copies the encrypted form; the copy stays valid for the lifetime of this process.</summary>
    public ProtectedSecret Clone()
    {
        lock (_gate)
        {
            byte[] cipher = _cipher ?? throw new ObjectDisposedException(nameof(ProtectedSecret));
            byte[] copy = Sensitive.AllocBytes(cipher.Length);
            cipher.CopyTo(copy, 0);
            return new ProtectedSecret(copy, _length);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Sensitive.Clear(_cipher);
            _cipher = null;
        }
    }
}

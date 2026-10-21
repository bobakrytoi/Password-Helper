using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.Vault;

/// <summary>
/// Windows Data Protection API, current-user scope. The key is derived by Windows from the user's logon
/// credentials; nothing secret is stored by this application. Another Windows account, or someone who copies
/// the file to another machine, cannot decrypt it.
/// </summary>
internal static class Dpapi
{
    /// <summary>
    /// Optional entropy used purely for domain separation (so generic DPAPI tooling cannot decrypt the blob
    /// without knowing it). It is NOT a secret and does not add confidentiality beyond DPAPI itself.
    /// </summary>
    private static readonly byte[] Entropy = "PasswordHelper/vault/v1"u8.ToArray();

    public static unsafe byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        fixed (byte* data = plaintext)
        fixed (byte* entropy = Entropy)
        {
            var input = new Win32.DATA_BLOB { cbData = plaintext.Length, pbData = (IntPtr)data };
            var optional = new Win32.DATA_BLOB { cbData = Entropy.Length, pbData = (IntPtr)entropy };

            if (!Win32.CryptProtectData(ref input, "Password helper vault", ref optional, IntPtr.Zero, IntPtr.Zero,
                    Win32.CRYPTPROTECT_UI_FORBIDDEN, out Win32.DATA_BLOB output))
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            try
            {
                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                Win32.LocalFree(output.pbData);
            }
        }
    }

    /// <summary>Decrypts a blob. The returned pinned buffer holds plaintext and must be cleared by the caller.</summary>
    public static unsafe byte[] Unprotect(ReadOnlySpan<byte> blob)
    {
        fixed (byte* data = blob)
        fixed (byte* entropy = Entropy)
        {
            var input = new Win32.DATA_BLOB { cbData = blob.Length, pbData = (IntPtr)data };
            var optional = new Win32.DATA_BLOB { cbData = Entropy.Length, pbData = (IntPtr)entropy };

            if (!Win32.CryptUnprotectData(ref input, IntPtr.Zero, ref optional, IntPtr.Zero, IntPtr.Zero,
                    Win32.CRYPTPROTECT_UI_FORBIDDEN, out Win32.DATA_BLOB output))
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            var native = new Span<byte>((void*)output.pbData, output.cbData);
            try
            {
                byte[] result = Sensitive.AllocBytes(output.cbData);
                native.CopyTo(result);
                return result;
            }
            finally
            {
                native.Clear();
                Win32.LocalFree(output.pbData);
            }
        }
    }
}

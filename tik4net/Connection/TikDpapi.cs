using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace tik4net.Connection
{
    /// <summary>
    /// Windows DPAPI (<c>crypt32!CryptProtectData</c> / <c>CryptUnprotectData</c>) by P/Invoke — the one piece of
    /// Windows-only code in the core library, reached only when a password is written as <c>dpapi:</c>.
    /// </summary>
    /// <remarks>
    /// P/Invoke rather than the <c>System.Security.Cryptography.ProtectedData</c> package, which neither target has in
    /// its base library: the package would reach every consumer, Linux and Unity included, for one prefix. The blob is
    /// the same one <c>ProtectedData.Protect(data, null, scope)</c> makes, so a value produced by PowerShell decrypts
    /// here. No additional entropy: a constant in public source adds nothing.
    /// </remarks>
    internal static class TikDpapi
    {
        private const int CryptProtectUiForbidden = 0x1;
        private const int CryptProtectLocalMachine = 0x4;

        internal static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Encrypts <paramref name="data"/> for the current user, or for the machine.</summary>
        internal static byte[] Protect(byte[] data, bool machineScope)
            => Run(data, machineScope ? CryptProtectUiForbidden | CryptProtectLocalMachine : CryptProtectUiForbidden,
                protect: true);

        /// <summary>Decrypts a blob made by <see cref="Protect"/> (or by <c>ProtectedData.Protect</c>), either scope.</summary>
        /// <exception cref="Win32Exception">The blob does not decrypt for this user on this machine.</exception>
        internal static byte[] Unprotect(byte[] blob) => Run(blob, CryptProtectUiForbidden, protect: false);

        // No unsafe code: the input is pinned with a GCHandle, the output copied out and wiped with Marshal.Copy.
        private static byte[] Run(byte[] input, int flags, bool protect)
        {
            if (!IsSupported)
                throw new PlatformNotSupportedException("DPAPI is Windows-only.");
            var pin = GCHandle.Alloc(input, GCHandleType.Pinned);
            try
            {
                var inBlob = new DataBlob { cbData = input.Length, pbData = pin.AddrOfPinnedObject() };
                var outBlob = default(DataBlob);
                int ok = protect
                    ? NativeMethods.CryptProtectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, flags, ref outBlob)
                    : NativeMethods.CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, flags, ref outBlob);
                if (ok == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                    return result;
                }
                finally
                {
                    // The plaintext of an unprotect is not left in freed native memory.
                    Marshal.Copy(new byte[outBlob.cbData], 0, outBlob.pbData, outBlob.cbData);
                    NativeMethods.LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                pin.Free();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        // Blittable signatures only (int, IntPtr, a sequential struct of both), so no marshalling stub is generated
        // and the assembly stays AOT-compatible.
        private static class NativeMethods
        {
            [DllImport("crypt32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern int CryptProtectData(ref DataBlob pDataIn, IntPtr szDataDescr, IntPtr pOptionalEntropy,
                IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

            [DllImport("crypt32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern int CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
                IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

            [DllImport("kernel32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            internal static extern IntPtr LocalFree(IntPtr hMem);
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SaveSpace.Services
{
    public static class CredentialStore
    {
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static bool Save(string target, string secret)
        {
            if (string.IsNullOrEmpty(secret)) return Delete(target);
            var bytes = Encoding.Unicode.GetBytes(secret);
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var cred = new CREDENTIAL
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredPersistLocalMachine,
                    UserName = Environment.UserName,
                    Comment = "SaveSpace API-sleutel"
                };
                return CredWrite(ref cred, 0);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Credential Manager schrijven mislukt: " + ex.Message);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }

        public static string? Read(string target)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                if (!CredRead(target, CredTypeGeneric, 0, out ptr)) return null;
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return "";
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Credential Manager lezen mislukt: " + ex.Message);
                return null;
            }
            finally
            {
                if (ptr != IntPtr.Zero) CredFree(ptr);
            }
        }

        public static bool Delete(string target)
        {
            try { return CredDelete(target, CredTypeGeneric, 0); }
            catch { return false; }
        }
    }

    public class AiException : Exception
    {
        public AiException(string message) : base(message) { }
    }
}

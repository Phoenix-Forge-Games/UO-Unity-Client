using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UOClient.Content;
using UOClient.Patching;

namespace UOClient.EditorTools
{
    /// <summary>
    /// Builds the Addressables content and encrypts the bundles of every Protected_* group in place, with the
    /// content.pak header and a nonce derived from the plain bundle (so an unchanged bundle gives the same file and
    /// the updater skips it). The updater's PatchBuild calls <see cref="EncryptProtectedBundles"/> after its own
    /// Addressables build; the menu item here is for building and checking content on its own. See Docs/SecureArt.md.
    /// </summary>
    public static class SecureArtBundles
    {
        public const string TargetFolder = "StandaloneWindows64";

        /// <summary>Addressables writes a group's bundles as "&lt;group name, lowercase&gt;_…bundle".</summary>
        private static readonly string ProtectedFilePrefix = SecureArtAddresses.ProtectedPrefix.ToLowerInvariant();

        [MenuItem("UO Client/Secure Art/Build Content (encrypted)")]
        public static void BuildMenu()
        {
            string error = BuildAndEncrypt();
            if (error != null)
            {
                Debug.LogError("[SecureArt] " + error);
            }
        }

        [MenuItem("UO Client/Secure Art/Verify Built Content")]
        public static void VerifyMenu()
        {
            List<string> problems = Verify(Path.Combine(PatchPaths.ContentRoot, TargetFolder));
            if (problems.Count == 0)
            {
                Debug.Log("[SecureArt] Every protected bundle is encrypted.");
            }

            foreach (string problem in problems)
            {
                Debug.LogError("[SecureArt] " + problem);
            }
        }

        /// <summary>Addressables build into the active profile's build path, then encryption. Null on success.</summary>
        public static string BuildAndEncrypt()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return "No Addressables settings; run Tools/SecureArt/SetupSecureArt.eval.cs first.";
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                return "Addressables build failed: " + result.Error;
            }

            string folder = Path.Combine(PatchPaths.ContentRoot, TargetFolder);
            int count = EncryptProtectedBundles(folder);
            List<string> problems = Verify(folder);
            if (problems.Count > 0)
            {
                return string.Join("\n", problems);
            }

            Debug.Log($"[SecureArt] Built content in {folder}; encrypted {count} protected bundle(s).");
            return null;
        }

        /// <summary>
        /// Encrypts every plain protected bundle under <paramref name="folder"/> (already-encrypted ones are left alone,
        /// so running it twice is harmless). Returns how many it encrypted. Throws when this machine has no content key.
        /// </summary>
        public static int EncryptProtectedBundles(string folder)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException(folder);
            }

            byte[] key = ContentKey.Get();
            if (key == null)
            {
                throw new InvalidOperationException("No content key on this machine (ContentKey.Local.cs). Generate it with UO Client > Release first.");
            }

            int count = 0;
            foreach (string path in Directory.GetFiles(folder, "*.bundle", SearchOption.AllDirectories))
            {
                if (!IsProtected(path) || IsEncrypted(path))
                {
                    continue;
                }

                byte[] plain = File.ReadAllBytes(path);
                byte[] nonce = new byte[CtrCipherStream.NonceSize];
                using (SHA256 sha = SHA256.Create())
                {
                    Buffer.BlockCopy(sha.ComputeHash(plain), 0, nonce, 0, nonce.Length);
                }

                var header = new byte[ProtectedContent.HeaderSize];
                Buffer.BlockCopy(ProtectedContent.Magic, 0, header, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ProtectedContent.FormatVersion), 0, header, 4, 4);
                Buffer.BlockCopy(nonce, 0, header, 8, nonce.Length);

                // CTR is symmetric: reading the plain bytes through the cipher stream gives the cipher bytes.
                var cipher = new byte[plain.Length];
                using (var source = new MemoryStream(plain, false))
                using (var stream = new CtrCipherStream(source, 0, plain.Length, key, nonce))
                {
                    int read = 0;
                    while (read < cipher.Length)
                    {
                        int n = stream.Read(cipher, read, cipher.Length - read);
                        if (n <= 0)
                        {
                            throw new IOException("Short read encrypting " + path);
                        }

                        read += n;
                    }
                }

                string temp = path + ".tmp";
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    output.Write(header, 0, header.Length);
                    output.Write(cipher, 0, cipher.Length);
                }

                File.Delete(path);
                File.Move(temp, path);
                count++;
            }

            return count;
        }

        /// <summary>Problems with built content: protected bundles that aren't encrypted, or no protected bundles at all.</summary>
        public static List<string> Verify(string folder)
        {
            var problems = new List<string>();
            if (!Directory.Exists(folder))
            {
                problems.Add("No built content at " + folder);
                return problems;
            }

            int protectedCount = 0;
            foreach (string path in Directory.GetFiles(folder, "*.bundle", SearchOption.AllDirectories))
            {
                if (!IsProtected(path))
                {
                    continue;
                }

                protectedCount++;
                if (!IsEncrypted(path))
                {
                    problems.Add("Protected bundle is not encrypted: " + path);
                }
            }

            if (protectedCount == 0)
            {
                problems.Add("No protected bundles in " + folder + " (were the Protected_* groups built?)");
            }

            return problems;
        }

        private static bool IsProtected(string path) =>
            Path.GetFileName(path).StartsWith(ProtectedFilePrefix, StringComparison.OrdinalIgnoreCase);

        private static bool IsEncrypted(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var magic = new byte[4];
                return file.Read(magic, 0, 4) == 4 && magic[0] == ProtectedContent.Magic[0] && magic[1] == ProtectedContent.Magic[1]
                       && magic[2] == ProtectedContent.Magic[2] && magic[3] == ProtectedContent.Magic[3];
            }
        }
    }
}

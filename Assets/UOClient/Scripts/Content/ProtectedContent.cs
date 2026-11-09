using System.IO;
using UnityEngine;

namespace UOClient.Content
{
    /// <summary>
    /// Opens the encrypted content pack a release build ships in StreamingAssets and hands back its
    /// <see cref="ContentPack"/>. The bundle is read through <see cref="CtrCipherStream"/>, so the
    /// paid art only ever exists decrypted in memory.
    ///
    /// Pack layout: "UOCP" magic, uint32 format version, 8-byte nonce, then the AES-CTR encrypted
    /// AssetBundle.
    /// </summary>
    public static class ProtectedContent
    {
        public const string FileName = "content.pak";
        public const int HeaderSize = 16;
        public const uint FormatVersion = 1;
        public static readonly byte[] Magic = { (byte)'U', (byte)'O', (byte)'C', (byte)'P' };

        private static AssetBundle _bundle;
        private static ContentPack _pack;

        public static string PackPath => Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>
        /// <c>UOClient.exe -contentSelfTest</c>: opens the pack, logs what it holds, and exits 0 on
        /// success or 1 on failure. The release pipeline's smoke test.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SelfTest()
        {
            if (Application.isEditor || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-contentSelfTest") < 0)
            {
                return;
            }

            ContentPack pack = Load();
            bool ok = pack != null && pack.Wardrobe != null && pack.Props != null && pack.Vegetation != null
                      && pack.Kit != null && pack.Kit.Prefabs.Length > 0 && pack.WorldMaterial != null;
            if (pack != null)
            {
                int kitMeshes = 0;
                foreach (GameObject prefab in pack.Kit != null ? pack.Kit.Prefabs : new GameObject[0])
                {
                    if (prefab != null && prefab.GetComponentInChildren<MeshFilter>(true)?.sharedMesh != null) kitMeshes++;
                }

                Debug.Log($"[ContentSelfTest] wardrobe={pack.Wardrobe != null} props={pack.Props != null} " +
                          $"vegetation={pack.Vegetation != null} wallKit={pack.WallKit != null} " +
                          $"landLayers={pack.LandLayers?.Length} humanoids={pack.HumanoidPrefabs?.Length} " +
                          $"kitPrefabs={pack.Kit?.Prefabs.Length} kitPrefabsWithMesh={kitMeshes} kitMaterials={pack.Kit?.Materials.Length}");
            }

            Debug.Log(ok ? "[ContentSelfTest] PASS" : "[ContentSelfTest] FAIL");
            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>The release content, or null (with an error logged) when it can't be opened.</summary>
        public static ContentPack Load()
        {
            if (_pack != null) return _pack;

            string path = PackPath;
            if (!File.Exists(path))
            {
                Debug.LogError($"[UOClient] Content pack missing: {path}. Release builds must come from UO Client > Release > Build.");
                return null;
            }

            byte[] key = ContentKey.Get();
            if (key == null)
            {
                Debug.LogError("[UOClient] This build has no content key, so it can't open the content pack.");
                return null;
            }

            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            var header = new byte[HeaderSize];
            if (file.Read(header, 0, HeaderSize) != HeaderSize || header[0] != Magic[0] || header[1] != Magic[1]
                || header[2] != Magic[2] || header[3] != Magic[3] || System.BitConverter.ToUInt32(header, 4) != FormatVersion)
            {
                file.Dispose();
                Debug.LogError($"[UOClient] {path} is not a content pack this client understands.");
                return null;
            }

            var nonce = new byte[CtrCipherStream.NonceSize];
            System.Buffer.BlockCopy(header, 8, nonce, 0, nonce.Length);

            // Unity keeps reading from the stream for as long as the bundle is loaded, so it stays open
            // for the life of the process.
            var stream = new CtrCipherStream(file, HeaderSize, file.Length - HeaderSize, key, nonce);
            _bundle = AssetBundle.LoadFromStream(stream);
            if (_bundle == null)
            {
                stream.Dispose();
                Debug.LogError("[UOClient] The content pack didn't decrypt to a valid bundle (wrong key for this build?).");
                return null;
            }

            ContentPack[] packs = _bundle.LoadAllAssets<ContentPack>();
            _pack = packs.Length > 0 ? packs[0] : null;
            if (_pack == null)
            {
                Debug.LogError("[UOClient] The content pack has no ContentPack asset.");
            }

            return _pack;
        }
    }
}

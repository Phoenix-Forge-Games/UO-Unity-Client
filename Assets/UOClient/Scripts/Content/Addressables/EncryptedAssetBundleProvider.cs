using System;
using System.ComponentModel;
using System.IO;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace UOClient.Content
{
    /// <summary>
    /// The Asset Bundle Provider of every Protected_* Addressables group. Their bundles are encrypted after the
    /// Addressables build (<c>SecureArtBundles.EncryptProtectedBundles</c>) with the content.pak header
    /// ("UOCP", format, 8-byte nonce) and AES-256-CTR under the compiled-in <see cref="ContentKey"/>; this opens
    /// them through <see cref="CtrCipherStream"/>, so the decrypted bundle only ever exists in memory.
    ///
    /// The nonce is the first 8 bytes of SHA-256 of the plain bundle: an unchanged bundle encrypts to the same
    /// file, so the updater skips it (the old content.pak used a random nonce and changed on every build).
    ///
    /// Adapted from the updater session's sketch (UO-Unity-Updater docs/unity/EncryptedAssetBundleProvider.cs);
    /// check it against the installed Addressables version when it first compiles.
    /// </summary>
    [DisplayName("Encrypted AssetBundle Provider (UOClient)")]
    public sealed class EncryptedAssetBundleProvider : ResourceProviderBase
    {
        public override void Provide(ProvideHandle handle)
        {
            string path = handle.ResourceManager.TransformInternalId(handle.Location);
            EncryptedBundleResource resource = null;
            try
            {
                resource = new EncryptedBundleResource(path);
                AssetBundleCreateRequest request = resource.BeginLoad();
                request.completed += _ =>
                {
                    if (request.assetBundle == null)
                    {
                        resource.Unload();
                        handle.Complete<IAssetBundleResource>(null, false, new Exception($"Couldn't open the protected bundle {path} (wrong key for this build?)"));
                    }
                    else
                    {
                        handle.Complete<IAssetBundleResource>(resource, true, null);
                    }
                };
            }
            catch (Exception e)
            {
                resource?.Unload();
                handle.Complete<IAssetBundleResource>(null, false, e);
            }
        }

        public override Type GetDefaultType(IResourceLocation location) => typeof(IAssetBundleResource);

        public override void Release(IResourceLocation location, object asset)
        {
            (asset as EncryptedBundleResource)?.Unload();
        }

        private sealed class EncryptedBundleResource : IAssetBundleResource
        {
            private readonly string _path;
            private FileStream _file;
            private Stream _stream; // AssetBundle.LoadFromStream reads from it for as long as the bundle is loaded
            private AssetBundleCreateRequest _request;

            public EncryptedBundleResource(string path)
            {
                _path = path;
            }

            public AssetBundleCreateRequest BeginLoad()
            {
                if (_path.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || _path.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("Protected bundles are installed by the updater and read from disk, never downloaded by the game: " + _path);
                }

                _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                var header = new byte[ProtectedContent.HeaderSize];
                bool encrypted = _file.Read(header, 0, header.Length) == header.Length
                                 && header[0] == ProtectedContent.Magic[0] && header[1] == ProtectedContent.Magic[1]
                                 && header[2] == ProtectedContent.Magic[2] && header[3] == ProtectedContent.Magic[3];
                if (!encrypted)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // A content build that skipped the encryption step: fine while developing, refused in releases.
                    Debug.LogWarning("[SecureArt] Protected bundle is not encrypted (development only): " + _path);
                    _file.Position = 0;
                    _stream = _file;
                    _request = AssetBundle.LoadFromStreamAsync(_stream);
                    return _request;
#else
                    throw new InvalidDataException("Not an encrypted bundle: " + _path);
#endif
                }

                if (BitConverter.ToUInt32(header, 4) != ProtectedContent.FormatVersion)
                {
                    throw new InvalidDataException("Protected bundle format this client doesn't understand: " + _path);
                }

                byte[] key = ContentKey.Get();
                if (key == null)
                {
                    throw new InvalidOperationException("This build has no content key, so it can't open protected bundles (see Docs/SecureArt.md).");
                }

                var nonce = new byte[CtrCipherStream.NonceSize];
                Buffer.BlockCopy(header, 8, nonce, 0, nonce.Length);
                _stream = new CtrCipherStream(_file, header.Length, _file.Length - header.Length, key, nonce);
                _request = AssetBundle.LoadFromStreamAsync(_stream);
                return _request;
            }

            public AssetBundle GetAssetBundle() => _request?.assetBundle;

            public void Unload()
            {
                if (_request != null && _request.assetBundle != null)
                {
                    _request.assetBundle.Unload(true);
                }

                if (_stream != null && _stream != _file)
                {
                    _stream.Dispose();
                }

                _file?.Dispose();
                _request = null;
                _stream = null;
                _file = null;
            }
        }
    }
}

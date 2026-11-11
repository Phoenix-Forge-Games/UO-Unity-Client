using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using Object = UnityEngine.Object;

namespace UOClient.Content
{
    /// <summary>
    /// <see cref="SecureArt"/>'s loader on top of Addressables. Registers itself before the first scene loads,
    /// starts Addressables (which reads its catalog from the installed Content folder, see <see cref="UOClient.Patching.PatchPaths"/>)
    /// and preloads everything labelled <see cref="SecureArtAddresses.PreloadLabel"/>.
    ///
    /// Loaded assets are kept for the life of the process: the paid art is a fixed working set (catalogs, the
    /// pieces they point at, icons), so there is nothing to gain from releasing and reloading it.
    /// </summary>
    internal sealed class AddressablesArtBackend : ISecureArtBackend
    {
        private readonly Dictionary<string, bool> _known = new Dictionary<string, bool>();

        public string Name => "Addressables";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var backend = new AddressablesArtBackend();
            SecureArt.Register(backend);
            backend.Start();
        }

        private void Start()
        {
            AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> init = Addressables.InitializeAsync(false);
            init.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError("[SecureArt] Addressables failed to start; paid art will fall back to placeholders. " + op.OperationException);
                    Addressables.Release(op);
                    SecureArt.MarkReady();
                    return;
                }

                Addressables.Release(op);
                Preload();
            };
        }

        private void Preload()
        {
            if (!Has(SecureArtAddresses.PreloadLabel))
            {
                SecureArt.MarkReady();
                return;
            }

            AsyncOperationHandle<IList<Object>> preload = Addressables.LoadAssetsAsync<Object>((object)SecureArtAddresses.PreloadLabel, (Action<Object>)null);
            preload.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError("[SecureArt] Preloading the catalogs failed: " + op.OperationException);
                }
                else
                {
                    Debug.Log($"[SecureArt] Preloaded {op.Result.Count} assets.");
                }

                // The handle is kept: releasing it would unload the bundles we just opened.
                SecureArt.MarkReady();
            };
        }

        public bool Has(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            if (_known.TryGetValue(address, out bool known))
            {
                return known;
            }

            AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync((object)address);
            locations.WaitForCompletion();
            bool found = locations.Status == AsyncOperationStatus.Succeeded && locations.Result != null && locations.Result.Count > 0;
            Addressables.Release(locations);
            _known[address] = found;
            return found;
        }

        public T LoadNow<T>(string address) where T : Object
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>((object)address);
            T asset = handle.WaitForCompletion();
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[SecureArt] Loading '{address}' as {typeof(T).Name} failed: {handle.OperationException}");
                Addressables.Release(handle);
                return null;
            }

            return asset;
        }

        public void Load<T>(string address, Action<T> done) where T : Object
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>((object)address);
            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"[SecureArt] Loading '{address}' as {typeof(T).Name} failed: {op.OperationException}");
                    Addressables.Release(op);
                    done(null);
                    return;
                }

                done(op.Result);
            };
        }

        public void LoadLabel<T>(string label, Action<IList<T>> done) where T : Object
        {
            if (!Has(label))
            {
                done(new List<T>());
                return;
            }

            AsyncOperationHandle<IList<T>> handle = Addressables.LoadAssetsAsync<T>((object)label, (Action<T>)null);
            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"[SecureArt] Loading label '{label}' failed: {op.OperationException}");
                    Addressables.Release(op);
                    done(new List<T>());
                    return;
                }

                done(op.Result);
            };
        }
    }
}

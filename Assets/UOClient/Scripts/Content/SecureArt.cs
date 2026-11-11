using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UOClient.Content
{
    /// <summary>
    /// Where the game gets paid art (asset-pack models, materials, catalogs that point at them, icons) by address,
    /// instead of holding direct references that would put the art into the player's readable data files.
    /// Addresses are in <see cref="SecureArtAddresses"/>; the design is in Docs/SecureArt.md.
    ///
    /// The real loading is done by a backend: the Addressables one (assembly UOClient.SecureArt.Addressables,
    /// compiled only once the Addressables package is installed) registers itself before the first scene loads.
    /// Without a backend (no package yet, or an open-source checkout without the art) every lookup returns null,
    /// or a placeholder for icons, and callers keep working with their own fallbacks.
    /// </summary>
    public static class SecureArt
    {
        private static readonly Dictionary<string, Object> Loaded = new Dictionary<string, Object>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Dictionary<string, List<Action<Object>>> Pending = new Dictionary<string, List<Action<Object>>>();
        private static readonly ConditionalWeakTable<VisualElement, string> IconRequests = new ConditionalWeakTable<VisualElement, string>();
        private static Texture2D _missingIcon;

        /// <summary>The loader in use, or null when this build has none.</summary>
        public static ISecureArtBackend Backend { get; private set; }

        /// <summary>Whether a backend is registered (art may still be missing from it).</summary>
        public static bool Available => Backend != null;

        /// <summary>Raised once the backend has finished its startup preload (catalogs are then ready).</summary>
        public static event Action Ready;

        public static bool IsReady { get; private set; }

        /// <summary>Called by a backend when it loads. A second registration replaces the first.</summary>
        public static void Register(ISecureArtBackend backend)
        {
            Backend = backend;
            Loaded.Clear();
            Missing.Clear();
            IsReady = false;
            Debug.Log($"[SecureArt] Backend: {backend?.Name ?? "none"}");
        }

        /// <summary>Called by the backend when its startup preload is done.</summary>
        public static void MarkReady()
        {
            IsReady = true;
            Ready?.Invoke();
        }

        /// <summary>
        /// The serialized reference when it is set (the editor keeps direct references while developing), otherwise
        /// the asset at <paramref name="address"/>, loaded now if it isn't already. Null when neither exists.
        /// Use at the point a component first needs its art: <c>_catalog = SecureArt.Resolve(_catalog, SecureArtAddresses.CreatureCatalog);</c>
        /// </summary>
        public static T Resolve<T>(T serialized, string address) where T : Object
        {
            return serialized != null ? serialized : Get<T>(address);
        }

        /// <summary>The asset at an address, loading it now (blocking) if needed; null when there isn't one.</summary>
        public static T Get<T>(string address) where T : Object
        {
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            if (Loaded.TryGetValue(address, out Object cached))
            {
                return cached as T;
            }

            if (Missing.Contains(address))
            {
                return null;
            }

            T asset = null;
            if (Backend != null && Backend.Has(address))
            {
                try
                {
                    asset = Backend.LoadNow<T>(address);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
#if UNITY_EDITOR
            else
            {
                asset = EditorFallback<T>(address);
            }
#endif

            Remember(address, asset);
            return asset;
        }

        /// <summary>The asset at an address, without blocking; <paramref name="done"/> gets null when there isn't one.</summary>
        public static void Load<T>(string address, Action<T> done) where T : Object
        {
            if (done == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(address) || Missing.Contains(address))
            {
                done(null);
                return;
            }

            if (Loaded.TryGetValue(address, out Object cached))
            {
                done(cached as T);
                return;
            }

            if (Backend == null || !Backend.Has(address))
            {
                done(Get<T>(address)); // editor fallback or a remembered miss; never blocks on a bundle
                return;
            }

            if (Pending.TryGetValue(address, out List<Action<Object>> waiting))
            {
                waiting.Add(o => done(o as T));
                return;
            }

            waiting = new List<Action<Object>> { o => done(o as T) };
            Pending[address] = waiting;
            try
            {
                Backend.Load<T>(address, asset => Finish(address, asset));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(address, null);
            }
        }

        /// <summary><see cref="Load{T}(string, Action{T})"/> as a task (completes on the main thread).</summary>
        public static Task<T> LoadAsync<T>(string address) where T : Object
        {
            var source = new TaskCompletionSource<T>();
            Load<T>(address, asset => source.TrySetResult(asset));
            return source.Task;
        }

        /// <summary>Every asset carrying a label (e.g. all icons of a category); an empty list without a backend.</summary>
        public static void LoadLabel<T>(string label, Action<IList<T>> done) where T : Object
        {
            if (done == null)
            {
                return;
            }

            if (Backend == null)
            {
                done(new List<T>());
                return;
            }

            Backend.LoadLabel(label, done);
        }

        // ---------------------------------------------------------------- icons

        /// <summary>The image shown while an icon loads or when it doesn't exist: a plain mid-grey square.</summary>
        public static Texture2D MissingIcon
        {
            get
            {
                if (_missingIcon == null)
                {
                    _missingIcon = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "MissingIcon", hideFlags = HideFlags.DontSave };
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(70, 70, 76, 255);
                    }

                    _missingIcon.SetPixels32(pixels);
                    _missingIcon.Apply(false, true);
                }

                return _missingIcon;
            }
        }

        /// <summary>
        /// An icon by address (see <see cref="SecureArtAddresses.SkillIcon"/> and friends). <paramref name="done"/>
        /// gets the texture, or <see cref="MissingIcon"/> when the icon doesn't exist in this build.
        /// </summary>
        public static void Icon(string address, Action<Texture2D> done)
        {
            Load<Texture2D>(address, texture => done?.Invoke(texture != null ? texture : MissingIcon));
        }

        /// <summary>
        /// Shows an icon as a UI Toolkit element's background: the placeholder at once, the icon when it has loaded.
        /// Safe to call again on the same element with another address (the latest call wins).
        /// <paramref name="hideWhenMissing"/> leaves the background empty instead of grey when there's no icon.
        /// </summary>
        public static void SetIcon(VisualElement element, string address, bool hideWhenMissing = false)
        {
            if (element == null)
            {
                return;
            }

            IconRequests.Remove(element);
            IconRequests.Add(element, address ?? string.Empty);
            if (!Loaded.ContainsKey(address ?? string.Empty))
            {
                element.style.backgroundImage = hideWhenMissing ? new StyleBackground(StyleKeyword.None) : new StyleBackground(MissingIcon);
            }

            Load<Texture2D>(address, texture =>
            {
                if (!IconRequests.TryGetValue(element, out string latest) || latest != (address ?? string.Empty))
                {
                    return; // the element was given another icon meanwhile
                }

                if (texture != null)
                {
                    element.style.backgroundImage = new StyleBackground(texture);
                }
                else
                {
                    element.style.backgroundImage = hideWhenMissing ? new StyleBackground(StyleKeyword.None) : new StyleBackground(MissingIcon);
                }
            });
        }

        // ---------------------------------------------------------------- internals

        private static void Finish(string address, Object asset)
        {
            Remember(address, asset);
            if (!Pending.TryGetValue(address, out List<Action<Object>> waiting))
            {
                return;
            }

            Pending.Remove(address);
            foreach (Action<Object> callback in waiting)
            {
                try
                {
                    callback(asset);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void Remember(string address, Object asset)
        {
            if (asset != null)
            {
                Loaded[address] = asset;
            }
            else if (Missing.Add(address) && Backend != null)
            {
                Debug.LogWarning($"[SecureArt] No art at '{address}' in this build; using the fallback.");
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Before Addressables is installed, the editor still finds icons by their file under
        /// <see cref="SecureArtAddresses.IconFolder"/>, so icon work can be tested without it.
        /// </summary>
        private static T EditorFallback<T>(string address) where T : Object
        {
            if (!address.StartsWith("icon/", StringComparison.Ordinal))
            {
                return null;
            }

            string[] parts = address.Split('/');
            if (parts.Length != 3)
            {
                return null;
            }

            string folder = SecureArtAddresses.IconFolder;
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            {
                return null;
            }

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string category = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (SecureArtAddresses.Slug(category) == parts[1] && SecureArtAddresses.Slug(name) == parts[2])
                {
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }

            return null;
        }
#endif
    }

    /// <summary>What actually loads the art. Implemented by the Addressables backend.</summary>
    public interface ISecureArtBackend
    {
        string Name { get; }

        /// <summary>Whether this build has an asset at the address.</summary>
        bool Has(string address);

        /// <summary>Loads an asset and waits for it (bundles are local files, so this is a disk read).</summary>
        T LoadNow<T>(string address) where T : Object;

        /// <summary>Loads an asset without blocking; <paramref name="done"/> runs on the main thread, with null on failure.</summary>
        void Load<T>(string address, Action<T> done) where T : Object;

        /// <summary>Loads every asset with a label; <paramref name="done"/> runs on the main thread.</summary>
        void LoadLabel<T>(string label, Action<IList<T>> done) where T : Object;
    }
}

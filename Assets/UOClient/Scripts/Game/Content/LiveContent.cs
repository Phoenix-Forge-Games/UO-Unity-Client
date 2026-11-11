using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UOGame.Content
{
    /// <summary>
    /// Content changed while the game runs, with no restart: an admin (or an AI agent through the admin API) replaces
    /// one of the content files, the server loads it at once, keeps it in <c>ServerData/Content</c> (so it survives a
    /// restart), and sends it to every client, which loads it the same way. A client that logs in later gets the
    /// server's changed files right after its login.
    /// <para>
    /// Only files whose rules can be loaded again safely are live (each service registers its own loader); the rest
    /// still need a new build. The login check compares the game's own copies, so a live change never turns anyone
    /// away. A change that breaks the file (bad JSON, a loader that throws) is refused and the previous text stays.
    /// </para>
    /// </summary>
    public static class LiveContent
    {
        private sealed class Entry
        {
            public TextAsset BuiltIn;
            public Action<TextAsset> Load;
            public string Override;
        }

        private static readonly Dictionary<string, Entry> Files = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Server: a file changed (or went back to the game's copy): send it to the clients.</summary>
        public static event Action<string> Changed;

        /// <summary>
        /// A service's content file can change live: <paramref name="reload"/> loads a new text into its rules (the
        /// service has already loaded the game's copy itself).
        /// </summary>
        public static void Register(TextAsset builtIn, Action<TextAsset> reload)
        {
            if (builtIn != null && reload != null)
            {
                Files[builtIn.name] = new Entry { BuiltIn = builtIn, Load = reload };
            }
        }

        /// <summary>The files that can change live.</summary>
        public static IEnumerable<string> Names => Files.Keys;

        public static bool IsLive(string name) => name != null && Files.ContainsKey(name);

        public static bool IsChanged(string name) => name != null && Files.TryGetValue(name, out Entry e) && e.Override != null;

        /// <summary>The file's text as the game runs it (changed, or the game's own).</summary>
        public static string Text(string name) =>
            name != null && Files.TryGetValue(name, out Entry e) ? e.Override ?? e.BuiltIn.text : null;

        private static TextAsset Asset(string name, string text) => new TextAsset(text) { name = name };

        /// <summary>Load a new text for a file. Returns null when it took, else why not (the old text stays).</summary>
        public static string Apply(string name, string text)
        {
            if (name == null || !Files.TryGetValue(name, out Entry e))
            {
                return $"'{name}' can't change live. Live files: {string.Join(", ", Names)}.";
            }

            try
            {
                JToken.Parse(text);
                e.Load(Asset(e.BuiltIn.name, text));
                e.Override = text == e.BuiltIn.text ? null : text;
                Changed?.Invoke(e.BuiltIn.name);
                return null;
            }
            catch (Exception ex)
            {
                try
                {
                    e.Load(e.Override != null ? Asset(e.BuiltIn.name, e.Override) : e.BuiltIn);
                }
                catch (Exception)
                {
                    // the previous text loaded before; if it can't now, the next change will show it
                }

                return ex.Message;
            }
        }

        /// <summary>Back to the game's own copy.</summary>
        public static void Revert(string name)
        {
            if (name != null && Files.TryGetValue(name, out Entry e) && e.Override != null)
            {
                e.Override = null;
                e.Load(e.BuiltIn);
                Changed?.Invoke(e.BuiltIn.name);
            }
        }

        // ------------------------------------------------------------------ server: kept in ServerData/Content

        public static string Folder => Path.Combine(Server.AccountStore.DefaultFolder, "Content");

        /// <summary>Server start: load the changes kept from before (one the game's copy now matches is dropped).</summary>
        public static void LoadSaved()
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(Folder, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                if (!Files.TryGetValue(name, out Entry e))
                {
                    Debug.LogWarning($"[Game] Live content: {name} can't change live; ServerData/Content/{name}.json ignored.");
                    continue;
                }

                if (text == e.BuiltIn.text)
                {
                    File.Delete(path); // the build caught up with this change
                    Debug.Log($"[Game] Live content: {name} matches the game's copy now; the kept change was removed.");
                    continue;
                }

                string error = Apply(name, text);
                Debug.Log(error == null ? $"[Game] Live content: {name} changed ({text.Length:N0} characters)." : $"[Game] Live content: {name} kept change refused: {error}");
            }
        }

        /// <summary>Server: change a file and keep the change. Returns null when it took, else why not.</summary>
        public static string Save(string name, string text)
        {
            string error = Apply(name, text);
            if (error != null)
            {
                return error;
            }

            string canonical = Files[name].BuiltIn.name;
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, canonical + ".json");
            if (IsChanged(canonical))
            {
                File.WriteAllText(path + ".tmp", text);
                File.Copy(path + ".tmp", path, true);
                File.Delete(path + ".tmp");
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            Debug.Log($"[Game] Live content: {canonical} {(IsChanged(canonical) ? "changed" : "back to the game's copy")}.");
            return null;
        }

        /// <summary>Server: back to the game's copy, and forget the kept change.</summary>
        public static void Forget(string name)
        {
            if (name == null || !Files.TryGetValue(name, out Entry e))
            {
                return;
            }

            Revert(name);
            string path = Path.Combine(Folder, e.BuiltIn.name + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Debug.Log($"[Game] Live content: {e.BuiltIn.name} back to the game's copy.");
        }

        /// <summary>The changed files (what a client logging in needs).</summary>
        public static IEnumerable<string> ChangedNames()
        {
            foreach (KeyValuePair<string, Entry> e in Files)
            {
                if (e.Value.Override != null)
                {
                    yield return e.Value.BuiltIn.name;
                }
            }
        }

        // ------------------------------------------------------------------ over the network

        private const int PartBytes = 48 * 1024;

        /// <summary>A file as patches to send (gzipped, in parts); none changed: one empty patch (back to the game's copy).</summary>
        public static List<Net.ContentPatch> Patches(string name)
        {
            var list = new List<Net.ContentPatch>();
            if (!IsChanged(name))
            {
                list.Add(new Net.ContentPatch { Name = name, Part = 0, Parts = 0, Data = Array.Empty<byte>() });
                return list;
            }

            byte[] zipped;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                {
                    byte[] raw = Encoding.UTF8.GetBytes(Text(name));
                    gzip.Write(raw, 0, raw.Length);
                }

                zipped = output.ToArray();
            }

            int parts = Math.Max(1, (zipped.Length + PartBytes - 1) / PartBytes);
            for (int i = 0; i < parts; i++)
            {
                int start = i * PartBytes, length = Math.Min(PartBytes, zipped.Length - start);
                var data = new byte[length];
                Buffer.BlockCopy(zipped, start, data, 0, length);
                list.Add(new Net.ContentPatch { Name = name, Part = i, Parts = parts, Data = data });
            }

            return list;
        }

        private static readonly Dictionary<string, List<byte[]>> Arriving = new Dictionary<string, List<byte[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Client: a patch arrived; the last part loads the file.</summary>
        public static void Receive(Net.ContentPatch patch)
        {
            if (patch.Parts == 0)
            {
                Arriving.Remove(patch.Name);
                Revert(patch.Name);
                return;
            }

            if (patch.Part == 0 || !Arriving.TryGetValue(patch.Name, out List<byte[]> parts))
            {
                Arriving[patch.Name] = parts = new List<byte[]>();
            }

            parts.Add(patch.Data);
            if (parts.Count < patch.Parts)
            {
                return;
            }

            Arriving.Remove(patch.Name);
            using (var input = new MemoryStream())
            {
                foreach (byte[] part in parts)
                {
                    input.Write(part, 0, part.Length);
                }

                input.Position = 0;
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    string error = Apply(patch.Name, reader.ReadToEnd());
                    Debug.Log(error == null ? $"[Game] The server changed {patch.Name}." : $"[Game] The server's {patch.Name} didn't load: {error}");
                }
            }
        }

        /// <summary>Client: left the server, so its changes go (the next server sends its own).</summary>
        public static void RevertAll()
        {
            Arriving.Clear();
            foreach (string name in new List<string>(ChangedNames()))
            {
                Revert(name);
            }
        }
    }
}

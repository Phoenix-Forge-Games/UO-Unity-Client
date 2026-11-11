using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UOClient
{
    /// <summary>Per-machine client settings kept in PlayerPrefs.</summary>
    public static class ClientSettings
    {
        private const string DataFolderPref = "uo.dataFolder";

        // The shard's own copy of the 7.0.117.33 client files (the development PC).
        private const string DevDataFolder = @"G:\Ultima Online\ServUO\ClientFiles";

        private static string _found;

        /// <summary>
        /// Folder holding the player's UO data files (Cliloc.enu, hues.mul, tiledata.mul...). The saved choice when it
        /// still holds them; otherwise found on this machine (the SeriousFun launcher's UO folder, the registry, the
        /// usual install folders) and remembered.
        /// </summary>
        public static string DataFolder
        {
            get
            {
                string saved = PlayerPrefs.GetString(DataFolderPref, string.Empty);
                if (HasUoData(saved))
                {
                    return saved;
                }

                if (_found == null)
                {
                    _found = Find() ?? DevDataFolder;
                    if (HasUoData(_found))
                    {
                        PlayerPrefs.SetString(DataFolderPref, _found);
                        Debug.Log($"[Client] UO data folder: {_found}");
                    }
                    else
                    {
                        Debug.LogWarning("[Client] No UO install found (Cliloc.enu): names and texts from UO will be missing. Set it in the game's settings.");
                    }
                }

                return _found;
            }
            set
            {
                _found = null;
                PlayerPrefs.SetString(DataFolderPref, value);
            }
        }

        public static bool HasUoData(string folder) =>
            !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "Cliloc.enu"));

        private static string Find()
        {
            foreach (string candidate in Candidates())
            {
                try
                {
                    if (HasUoData(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // an unreadable candidate: try the next
                }
            }

            return null;
        }

        private static IEnumerable<string> Candidates()
        {
            yield return DevDataFolder;

            // The SeriousFun launcher remembers where it put UO.
            string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SeriousFunLauncher", "launcher.json");
            string fromLauncher = null;
            try
            {
                if (File.Exists(launcher))
                {
                    var json = JsonUtility.FromJson<LauncherSettings>(File.ReadAllText(launcher));
                    fromLauncher = json?.UoDirectory;
                }
            }
            catch (Exception)
            {
                // not readable: carry on
            }

            if (!string.IsNullOrWhiteSpace(fromLauncher))
            {
                yield return fromLauncher;
            }

            // The EA installer's registry entry.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            foreach (string key in new[]
                     {
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Ultima Online Classic",
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\Electronic Arts\EA Games\Ultima Online Classic",
                         @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Origin Worldwide\Ultima Online",
                     })
            {
                string dir = null;
                try
                {
                    // By name: the registry type isn't in this API profile's references, but the runtime has it.
                    Type registry = Type.GetType("Microsoft.Win32.Registry, mscorlib") ?? Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry");
                    dir = registry?.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) })?.Invoke(null, new object[] { key, "InstallDir", null }) as string;
                }
                catch (Exception)
                {
                    // no registry access here
                }

                if (!string.IsNullOrWhiteSpace(dir))
                {
                    yield return dir;
                }
            }
#endif

            // The usual places on every drive.
            foreach (DriveInfo drive in SafeDrives())
            {
                string root = drive.RootDirectory.FullName;
                yield return Path.Combine(root, "Program Files (x86)", "Electronic Arts", "Ultima Online Classic");
                yield return Path.Combine(root, "Program Files", "Electronic Arts", "Ultima Online Classic");
                yield return Path.Combine(root, "Electronic Arts", "Ultima Online Classic");
                yield return Path.Combine(root, "Ultima Online Classic");
                yield return Path.Combine(root, "Games", "Ultima Online Classic");
            }
        }

        private static IEnumerable<DriveInfo> SafeDrives()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (DriveInfo d in drives)
            {
                bool ready;
                try
                {
                    ready = d.DriveType == DriveType.Fixed && d.IsReady;
                }
                catch (Exception)
                {
                    ready = false;
                }

                if (ready)
                {
                    yield return d;
                }
            }
        }

        [Serializable]
        private sealed class LauncherSettings
        {
            public string UoDirectory;
        }
    }
}

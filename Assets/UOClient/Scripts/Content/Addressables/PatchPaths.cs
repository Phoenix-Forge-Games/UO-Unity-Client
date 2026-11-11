using System.IO;
using UnityEngine;

namespace UOClient.Patching
{
    /// <summary>
    /// Where Addressables content lives at runtime. The updater (UO-Unity-Updater) installs it next to the player as
    /// &lt;install&gt;/Content/&lt;BuildTarget&gt;/ (catalog and bundles), and Addressables loads it from there as its
    /// "remote" location: the game itself never downloads anything.
    ///
    /// The Addressables profile "Patch" uses Remote.LoadPath = {UOClient.Patching.PatchPaths.ContentRoot}/[BuildTarget],
    /// evaluated by reflection, so this must stay a public static property with this exact name.
    ///
    /// In the editor ("Use Existing Build" play mode) it is &lt;project&gt;/AddressableContent, the build folder.
    /// Not ServerData: in this project that folder holds the game server's accounts and saves.
    /// </summary>
    public static class PatchPaths
    {
        public const string EditorBuildFolder = "AddressableContent";

        public static string ContentRoot
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", EditorBuildFolder)).Replace('\\', '/');
#else
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Content")).Replace('\\', '/');
#endif
            }
        }
    }
}

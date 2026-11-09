using UnityEngine;

namespace UOClient
{
    /// <summary>Per-machine client settings kept in PlayerPrefs.</summary>
    public static class ClientSettings
    {
        private const string DataFolderPref = "uo.dataFolder";

        // The shard's own copy of the 7.0.117.33 client files, which match what the server uses.
        private const string DefaultDataFolder = @"G:\Ultima Online\ServUO\ClientFiles";

        /// <summary>Folder holding the UO client data files (map*.uop, statics*.mul, tiledata.mul).</summary>
        public static string DataFolder
        {
            get => PlayerPrefs.GetString(DataFolderPref, DefaultDataFolder);
            set => PlayerPrefs.SetString(DataFolderPref, value);
        }
    }
}

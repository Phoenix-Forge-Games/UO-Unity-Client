namespace UOClient.Content
{
    /// <summary>
    /// The content-pack key. Its bytes come from <c>ContentKey.Local.cs</c>, which the release
    /// pipeline generates on the build machine and git ignores, so the key is never in the public
    /// repository. Release builds use IL2CPP, which compiles the bytes into native code as scattered
    /// immediates rather than a readable array. Without the local file the key is empty and only the
    /// editor (which reads the packs directly) works.
    /// </summary>
    internal static partial class ContentKey
    {
        public const int Size = 32;

        static partial void Fill(byte[] key);

        /// <summary>The key, or null when this build has none.</summary>
        public static byte[] Get()
        {
            var key = new byte[Size];
            Fill(key);
            foreach (byte b in key)
            {
                if (b != 0) return key;
            }

            return null;
        }
    }
}

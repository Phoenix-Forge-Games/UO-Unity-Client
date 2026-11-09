namespace UOClient.Net
{
    public enum SessionState
    {
        Idle,
        Connecting,
        VerifyingAccount,
        ServerSelection,
        ConnectingToGameServer,
        CharacterSelection,
        EnteringWorld,
        InWorld,
        Failed,
    }

    public sealed class ServerInfo
    {
        public ushort Index;
        public string Name;
        public byte PercentFull;
    }

    public sealed class CharacterSlot
    {
        public int Slot;
        public string Name;
    }

    public sealed class StartingCity
    {
        public byte Index;
        public string City;
        public string Building;
        public ushort X, Y;
        public sbyte Z;
        public uint Map;
    }

    /// <summary>
    /// Everything 0xF8 sends for a new character. ServUO validates it: each stat 10-60 with a 90
    /// total, and the four skill values summing to 120 with none above 50.
    /// </summary>
    public sealed class NewCharacter
    {
        public string Name;
        public bool Female;
        public byte Strength = 60, Dexterity = 10, Intelligence = 20;
        public byte[] SkillIds = { 40, 27, 1, 17 }; // swordsmanship, tactics, anatomy, healing
        public byte[] SkillValues = { 30, 30, 30, 30 };
        public ushort SkinHue = 0x83EA;
        public ushort HairStyle = 0x203B, HairHue = 0x044E;
        public ushort BeardStyle, BeardHue;
        public ushort ShirtHue = 0x0384, PantsHue = 0x0384;
        public int CityIndex;
    }

    /// <summary>Contents of 0x1B, the first packet after a character enters the world.</summary>
    public sealed class LoginConfirm
    {
        public uint Serial;
        public ushort Body;
        public ushort X, Y;
        public short Z;
        public byte Direction;
        public ushort MapWidth, MapHeight;
    }

    /// <summary>The fixed client version and protocol values this client reports to the server.</summary>
    public static class ClientIdentity
    {
        // The shard's ClientFiles are 7.0.117.33 and ServUO rejects older clients.
        public const byte Major = 7;
        public const byte Minor = 0;
        public const byte Build = 117;
        public const byte Revision = 33;
        public const string VersionString = "7.0.117.33";

        public const int DefaultPort = 2593;
    }

    internal static class LoginMessages
    {
        /// <summary>0x82 login rejection reasons.</summary>
        public static string ForLoginReject(byte code)
        {
            switch (code)
            {
                case 0: return "Incorrect account name or password.";
                case 1: return "Someone is already using this account.";
                case 2: return "This account has been blocked.";
                case 3: return "This account's credentials are invalid.";
                case 4: return "Communication problem with the login server.";
                default: return $"Login rejected (code {code}).";
            }
        }

        /// <summary>0x53 popup codes, sent during character login.</summary>
        public static string ForPopup(byte code)
        {
            switch (code)
            {
                case 0: return "Incorrect password.";
                case 1: return "This character no longer exists.";
                case 2: return "This character already exists.";
                case 5: return "Another character on this account is already logged in.";
                case 6: return "Synchronization error.";
                case 7: return "You were idle for too long.";
                case 9: return "A character transfer is in progress.";
                case 3:
                case 4:
                case 8: return "Could not attach to the game server.";
                default: return $"Server refused the login (code {code}).";
            }
        }
    }
}

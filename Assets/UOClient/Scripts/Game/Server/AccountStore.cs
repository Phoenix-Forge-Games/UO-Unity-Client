using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace UOGame.Server
{
    /// <summary>
    /// The server's accounts and their characters, kept in <c>ServerData/accounts.json</c> next to the server
    /// (the project folder in the editor). Passwords are stored only as salted PBKDF2 hashes. An unknown account name
    /// is created on its first login, the way UO shards usually run. Saved by writing a new file and swapping it in, so
    /// a crash mid-save keeps the last good copy. (Interim: the world save proper comes with the migration work.)
    /// </summary>
    public sealed class AccountStore
    {
        [Serializable]
        public sealed class Character
        {
            public int Id;
            public string Name;
            public int Facet = 1;
            /// <summary>Quests under way, and those done (when: for restart delays and done-once quests).</summary>
            public List<QuestState> Quests = new List<QuestState>();
            public List<QuestDone> QuestsDone = new List<QuestDone>();

            /// <summary>Bulk orders per craft: when the last was offered (UTC ticks) and the points banked.</summary>
            public List<BodState> Bods = new List<BodState>();

            /// <summary>The city chosen at creation (Creation.json StartCities), where a new character first appears.</summary>
            public int StartCity = -1;
            /// <summary>Moved from a ServUO shard (ServUOImport): which (null: the account's) and its serial there (0: made
            /// here). A second import from that shard skips it.</summary>
            public int ImportedSerial;
            public string ImportedFrom;
            public Vector3 Position;
            public float Yaw;
            public bool Placed;
            /// <summary>Everything carried (Slot 0: in the backpack) and worn (Slot: where).</summary>
            public List<Item> Items = new List<Item>();
            /// <summary>The bank box (opened at a banker).</summary>
            public List<Item> Bank = new List<Item>();
            /// <summary>Has had the starting outfit.</summary>
            public bool Outfitted;
            /// <summary>How they look (chosen at creation; rolled once for older characters).</summary>
            public int LookSeed;
            public bool Female;
            /// <summary>A gargoyle (chosen at creation): wings, can fly, can't ride.</summary>
            public bool Gargoyle;
            public int Skin, Hair, HairHue, Beard, BeardHue;
            /// <summary>Stats and skills (0 Str: made before stats existed; given a profession's on entering).</summary>
            public int Str, Dex, Int;
            /// <summary>Stat locks: 0 up, 1 down, 2 locked.</summary>
            public byte StrLock, DexLock, IntLock;
            public int Profession;
            public List<SkillValue> Skills = new List<SkillValue>();
            /// <summary>Pets: those following (they come and go with the character) and those in a stable.</summary>
            public List<Pet> Pets = new List<Pet>();
            /// <summary>UO's fame (0..32000) and karma (-32000..32000): from kills and deeds; Chivalry draws on karma.</summary>
            public int Fame, Karma;
            /// <summary>Tithing points (gold given at an ankh): what Chivalry costs.</summary>
            public int Tithing;
        }

        /// <summary>A pet a character owns, following or stabled (Pets.json rules).</summary>
        [Serializable]
        public sealed class Pet
        {
            public string Type;
            public int Seed;
            public string Name;
            public int Loyalty = 100;
            public bool Bonded, Stabled, Dead;
            public string BondingBegin;
            public List<int> Owners = new List<int>();
            public float SkillScale = 1f;
            public int Hits;
            public byte Order;
            /// <summary>Being ridden (it's carried by its rider, not in the world).</summary>
            public bool Ridden;
            /// <summary>A pack animal's pack.</summary>
            public List<Item> Pack = new List<Item>();
        }

        /// <summary>A skill and its value (one decimal, as UO shows them).</summary>
        [Serializable]
        public sealed class SkillValue
        {
            public string Skill;
            public float Value;
            /// <summary>0 up, 1 down (may drop to make room), 2 locked.</summary>
            public byte Lock;
        }

        /// <summary>A quest under way: who gave it, when, and each objective's count (kills).</summary>
        [Serializable]
        public sealed class QuestState
        {
            public string Quest, Giver;
            public long Started;
            public List<int> Progress = new List<int>();
        }

        [Serializable]
        public sealed class QuestDone
        {
            public string Quest;
            public long When;
        }

        /// <summary>A character's bulk orders for one craft.</summary>
        [Serializable]
        public sealed class BodState
        {
            public string Craft;
            public long Last;
            public double Points;
            /// <summary>The last order's points, waiting to be spent on a reward or banked (and whether it was large).</summary>
            public double Pending;
            public bool PendingLarge;
        }

        /// <summary>An item someone owns: its id, its kind (Items.json type) and how many.</summary>
        [Serializable]
        public sealed class Item
        {
            public uint Id;
            public string Type;
            public int Amount = 1;
            public int Hue;
            public byte Slot;
            /// <summary>What's in it, for items that hold something (a spellbook's spells, one bit each).</summary>
            public long Data;
            /// <summary>Words it carries: a marked recall rune's destination ("facet|x|y|z|place").</summary>
            public string Text;
            /// <summary>Its magic properties, id/value pairs (Magic.json ids), or empty.</summary>
            public List<int> Props = new List<int>();
            /// <summary>Crafted: made exceptional, and the material it was made from ("DullCopper", "Spined"...; empty:
            /// plain). Bulk orders ask for both.</summary>
            public bool Exceptional;
            public string Resource = "";
        }

        [Serializable]
        public sealed class Account
        {
            public string Name;
            public string Salt;
            public string Hash;
            public int Iterations;
            public string Created;
            /// <summary>The character last entered the world as (0: none yet); the menu offers it first.</summary>
            public int LastPlayed;
            /// <summary>An admin account: its characters play normally and can switch to admin mode (the Admin tools).
            /// Granted only through the admin API (Docs/AdminTools.md).</summary>
            public bool Admin;
            /// <summary>Moved from a ServUO shard (ServUOImport): the shard, and its old password hashes, checked once at the first
            /// login here and then replaced by our own (ServUO's: hex SHA-1, SHA-512 or MD5 of name + password).</summary>
            public string ImportedFrom, LegacySha1, LegacySha512, LegacyMd5;
            public List<Character> Characters = new List<Character>();
        }

        [Serializable]
        private sealed class File
        {
            public int NextCharacterId = 1;
            public uint NextItemId = 1;
            /// <summary>The world scale positions were saved at (metres per tile).</summary>
            public float TileMetres;
            public List<Account> Accounts = new List<Account>();
        }

        private const int Iterations = 100_000;
        public const int MaxCharacters = 7;

        private readonly string _path;
        private File _data;

        public AccountStore(string folder)
        {
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "accounts.json");
            _data = System.IO.File.Exists(_path)
                ? JsonUtility.FromJson<File>(System.IO.File.ReadAllText(_path)) ?? new File()
                : new File();

            // Positions are metres: saved at another world scale they'd land in the wrong place, so start over.
            if (!Mathf.Approximately(_data.TileMetres, UOClient.WorldSpace.TileSize))
            {
                int moved = 0;
                foreach (Account account in _data.Accounts)
                {
                    foreach (Character character in account.Characters)
                    {
                        moved += character.Placed ? 1 : 0;
                        character.Placed = false;
                    }
                }

                if (moved > 0)
                {
                    Debug.Log($"[Game] World scale changed ({_data.TileMetres} -> {UOClient.WorldSpace.TileSize} m per tile): {moved} characters back to the start point.");
                }

                _data.TileMetres = UOClient.WorldSpace.TileSize;
            }
        }

        /// <summary>Where server data lives: next to the project in the editor, next to the game in a build.</summary>
        /// <summary>A new item id, unique for good (saved with the accounts).</summary>
        public uint NewItemId() => _data.NextItemId++;

        public static string DefaultFolder =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ServerData"));

        /// <summary>Checks a login; creates the account if the name is new. Null when refused, with the reason.</summary>
        public Account Login(string name, string password, out string reason)
        {
            reason = null;
            name = (name ?? string.Empty).Trim();
            if (name.Length < 2 || name.Length > 24)
            {
                reason = "Account names are 2 to 24 characters.";
                return null;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                reason = "Passwords are at least 4 characters.";
                return null;
            }

            Account account = _data.Accounts.Find(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                byte[] salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                account = new Account
                {
                    Name = name,
                    Salt = Convert.ToBase64String(salt),
                    Iterations = Iterations,
                    Hash = Convert.ToBase64String(Derive(password, salt, Iterations)),
                    Created = DateTime.UtcNow.ToString("o"),
                };
                _data.Accounts.Add(account);
                Save();
                return account;
            }

            if (string.IsNullOrEmpty(account.Hash) && (account.LegacySha1 != null || account.LegacySha512 != null || account.LegacyMd5 != null))
            {
                // Moved from SeriousFun: its old password, checked the way ServUO did, then kept our way.
                if (!LegacyMatches(account, password))
                {
                    reason = "Wrong password.";
                    return null;
                }

                SetPassword(account, password);
                account.LegacySha1 = account.LegacySha512 = account.LegacyMd5 = null;
                Save();
                return account;
            }

            if (string.IsNullOrEmpty(account.Hash))
            {
                // Reset by the operator: the next password given becomes the account's password.
                byte[] fresh = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(fresh);
                }

                account.Salt = Convert.ToBase64String(fresh);
                account.Iterations = Iterations;
                account.Hash = Convert.ToBase64String(Derive(password, fresh, Iterations));
                Save();
                return account;
            }

            byte[] expected = Convert.FromBase64String(account.Hash);
            byte[] actual = Derive(password, Convert.FromBase64String(account.Salt), account.Iterations);
            if (!FixedTimeEquals(expected, actual))
            {
                reason = "Wrong password.";
                return null;
            }

            return account;
        }

        public Character Create(Account account, string name, out string reason)
        {
            reason = null;
            name = (name ?? string.Empty).Trim();
            if (name.Length < 2 || name.Length > 16)
            {
                reason = "Names are 2 to 16 characters.";
                return null;
            }

            foreach (char c in name)
            {
                if (!char.IsLetter(c) && c != ' ' && c != '\'' && c != '-')
                {
                    reason = "Names use letters, spaces, ' and - only.";
                    return null;
                }
            }

            if (account.Characters.Count >= MaxCharacters)
            {
                reason = $"An account has at most {MaxCharacters} characters.";
                return null;
            }

            foreach (Account a in _data.Accounts)
            {
                if (a.Characters.Exists(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "That name is taken.";
                    return null;
                }
            }

            var character = new Character { Id = _data.NextCharacterId++, Name = name };
            account.Characters.Add(character);
            Save();
            return character;
        }

        private void SetPassword(Account account, string password)
        {
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            account.Salt = Convert.ToBase64String(salt);
            account.Iterations = Iterations;
            account.Hash = Convert.ToBase64String(Derive(password, salt, Iterations));
        }

        /// <summary>ServUO's password check: the hash of the account name and password (ASCII, at most 256 bytes),
        /// written as dash-separated hex.</summary>
        private static bool LegacyMatches(Account account, string password)
        {
            byte[] phrase = System.Text.Encoding.ASCII.GetBytes(account.Name + password);
            if (phrase.Length > 256)
            {
                Array.Resize(ref phrase, 256);
            }

            static string Hex(HashAlgorithm h, byte[] data) => BitConverter.ToString(h.ComputeHash(data));
            using (var sha1 = SHA1.Create())
            using (var sha512 = SHA512.Create())
            using (var md5 = MD5.Create())
            {
                return account.LegacySha512 != null ? Hex(sha512, phrase) == account.LegacySha512
                    : account.LegacySha1 != null ? Hex(sha1, phrase) == account.LegacySha1
                    : Hex(md5, phrase) == account.LegacyMd5;
            }
        }

        /// <summary>An account moved from a ServUO shard (no password here yet: its old one works at the first login).</summary>
        public Account AddImported(string name, string created, string sha1, string sha512, string md5, string source)
        {
            var account = new Account
            {
                Name = name,
                Created = created ?? DateTime.UtcNow.ToString("o"),
                ImportedFrom = source,
                LegacySha1 = sha1,
                LegacySha512 = sha512,
                LegacyMd5 = md5,
            };
            _data.Accounts.Add(account);
            return account;
        }

        public int NewCharacterId() => _data.NextCharacterId++;

        public bool NameTaken(string name) =>
            _data.Accounts.Exists(a => a.Characters.Exists(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

        /// <summary>Every account (the admin API lists and changes them).</summary>
        public IReadOnlyList<Account> All => _data.Accounts;

        public Account Find(string name) => _data.Accounts.Find(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        public void Save()
        {
            string temp = _path + ".tmp";
            System.IO.File.WriteAllText(temp, JsonUtility.ToJson(_data, true));
            if (System.IO.File.Exists(_path))
            {
                System.IO.File.Replace(temp, _path, _path + ".bak");
            }
            else
            {
                System.IO.File.Move(temp, _path);
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(32);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }
    }
}

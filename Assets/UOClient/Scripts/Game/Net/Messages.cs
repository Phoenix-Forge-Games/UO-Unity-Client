using FishNet.Broadcast;
using UnityEngine;

namespace UOGame.Net
{
    /// <summary>Protocol constants both sides must agree on.</summary>
    public static class Protocol
    {
        /// <summary>Bump when any message changes shape.</summary>
        public const int Version = 26;

        /// <summary>
        /// What this build's world content is: the chunk format and the palette it was baked against. A client whose
        /// content differs from the server's is turned away at login with a reason, instead of drifting out of step.
        /// </summary>
        public static string ContentHash(UOClient.Bake.WorldPalette palette) =>
            $"c{UOClient.Bake.ChunkFormat.Version}-m{palette.Meshes.Count}-t{palette.Materials.Count}-e{palette.Effects.Count}-d{DataHash}";

        private static readonly System.Collections.Generic.SortedDictionary<string, string> DataFiles =
            new System.Collections.Generic.SortedDictionary<string, string>(System.StringComparer.Ordinal);

        /// <summary>
        /// Content data both sides must hold identically (creatures, items, outfits, names...): only kinds, seeds and
        /// ids cross the network, so a mismatch would show the wrong things. Each file is fingerprinted as it loads.
        /// </summary>
        public static void AddDataFile(UnityEngine.TextAsset file)
        {
            if (file == null)
            {
                return;
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                DataFiles[file.name] = System.Convert.ToBase64String(sha.ComputeHash(file.bytes)).Substring(0, 12);
            }
        }

        private static string DataHash => string.Join(".", DataFiles.Values);
    }

    /// <summary>Client → server, as soon as the connection opens.</summary>
    public struct LoginRequest : IBroadcast
    {
        public int Protocol;
        public string Content;
        public string Account;
        public string Password;
    }

    /// <summary>Server → client: let in (with the account's characters) or turned away (with why).</summary>
    public struct LoginResult : IBroadcast
    {
        public bool Ok;
        public string Reason;
        public CharacterSummary[] Characters;
    }

    public struct CharacterSummary
    {
        public int Id;
        public string Name;
    }

    /// <summary>Client → server: make a new character (and play it).</summary>
    public struct CreateCharacter : IBroadcast
    {
        public string Name;
        /// <summary>Sex, skin, hair and beard (gear is ignored: the server dresses new characters).</summary>
        public Look Look;
        /// <summary>A profession's id, or 0 for your own stats and skills.</summary>
        public int Profession;
        public int Str, Dex, Int;
        public SkillPick[] Skills;
        /// <summary>Where to begin: an index into Creation.json StartCities (out of range: the default).</summary>
        public int City;
    }

    /// <summary>A starting skill and its value.</summary>
    public struct SkillPick
    {
        public string Skill;
        public int Value;
    }

    /// <summary>Client → server: enter the world as this character.</summary>
    public struct PlayCharacter : IBroadcast
    {
        public int Id;
    }

    /// <summary>Server → client: a character request failed.</summary>
    public struct CharacterRefused : IBroadcast
    {
        public string Reason;
    }

    /// <summary>Client → server: say something.</summary>
    public struct SayRequest : IBroadcast
    {
        public string Text;
    }

    /// <summary>Server → clients in earshot: someone said something.</summary>
    public struct SaidMessage : IBroadcast
    {
        public int SpeakerObjectId;
        public string Speaker;
        public string Text;
    }

    /// <summary>Client → server: target this (object id; 0 clears), and whether to attack it.</summary>
    public struct TargetRequest : IBroadcast
    {
        /// <summary>The object id of the one chosen, -1 for nobody (0 is a real id).</summary>
        public int ObjectId;
        public bool Attack;
    }

    /// <summary>Server → clients near it: a swing landed (Amount &gt; 0) or missed (0), for floating numbers.</summary>
    public struct SwingMessage : IBroadcast
    {
        public int TargetId;
        public int AttackerId;
        public int Amount;
    }

    /// <summary>Client → server: use (open) the map door with this stable ID.</summary>
    public struct UseDoor : IBroadcast
    {
        public ulong DoorId;
    }

    /// <summary>Server → clients near it: this map door opened or shut.</summary>
    public struct DoorChanged : IBroadcast
    {
        public ulong DoorId;
        public bool Open;
    }

    // ------------------------------------------------------------------ items

    /// <summary>An item as clients see it: its id, its kind (number in our item content) and how many.</summary>
    public struct ItemView
    {
        public uint Id;
        public ushort Kind;
        public int Amount;
        public int Hue;
        /// <summary>Where it's worn (WearSlot), 0 when it's in a container.</summary>
        public byte Slot;
        /// <summary>What it holds (a spellbook's spells), 0 for most items.</summary>
        public long Data;
        /// <summary>Its own words, when it has some (a marked recall rune: where it leads).</summary>
        public string Label;
        /// <summary>Its magic properties, id/value pairs, or null.</summary>
        public int[] Props;
    }

    /// <summary>Server → owner: everything you carry and wear (sent whole whenever it changes).</summary>
    public struct InventoryState : IBroadcast
    {
        public ItemView[] Items;
    }

    /// <summary>Client → server: wear an item from your backpack, or take a worn one off into the backpack.</summary>
    public struct WearItem : IBroadcast
    {
        public uint Id;
        public bool Wear;
    }

    /// <summary>Client → server: open the corpse with this object id (you must be standing by it).</summary>
    public struct OpenCorpse : IBroadcast
    {
        public int ObjectId;
    }

    /// <summary>Server → client: what's in a corpse you opened (sent again after each take).</summary>
    public struct CorpseContents : IBroadcast
    {
        public int ObjectId;
        public string Name;
        public ItemView[] Items;
    }

    /// <summary>Client → server: take an item (Id) or everything (Id 0) from a corpse into your backpack.</summary>
    public struct TakeFromCorpse : IBroadcast
    {
        public int ObjectId;
        public uint Id;
    }

    // ------------------------------------------------------------------ moving items, the bank, the ground

    /// <summary>Where an item can be moved to.</summary>
    public enum ItemPlace : byte { Backpack = 0, Bank = 1, Ground = 2, Pack = 3 }

    /// <summary>Client → server: move an item (or part of a stack) to your backpack, your bank box, or the ground.</summary>
    public struct MoveItem : IBroadcast
    {
        public uint Id;
        public int Amount;
        public ItemPlace To;
        /// <summary>The pack animal (object id) whose pack the item comes from or goes to, 0 for none.</summary>
        public int Container;
    }

    /// <summary>Client → server: open your bank box (standing by a banker).</summary>
    public struct OpenBank : IBroadcast
    {
        public int BankerId;
    }

    /// <summary>Server → owner: your bank box (sent on opening it and whenever it changes); Open false when it closed.</summary>
    public struct BankState : IBroadcast
    {
        public bool Open;
        public ItemView[] Items;
    }

    /// <summary>Client → server: pick up the item lying on the ground (its object id).</summary>
    public struct PickUp : IBroadcast
    {
        public int ObjectId;
    }

    // ------------------------------------------------------------------ vendors

    /// <summary>One line of a shop: an item kind for sale (Id 0), or one of your items they'd buy (its Id).</summary>
    public struct ShopLine
    {
        public ushort Kind;
        public uint Id;
        public int Price;
        public int Available;
        public ushort Hue;
    }

    /// <summary>Client → server: show me what this vendor sells (Selling false) or would buy from me (true).</summary>
    public struct ShopRequest : IBroadcast
    {
        public int VendorId;
        public bool Selling;
    }

    /// <summary>Server → client: the vendor's offer.</summary>
    public struct ShopOffer : IBroadcast
    {
        public int VendorId;
        public bool Selling;
        public ShopLine[] Lines;
    }

    /// <summary>Client → server: buy (by kind) or sell (by item id) these amounts.</summary>
    public struct ShopOrder : IBroadcast
    {
        public int VendorId;
        public bool Selling;
        public ShopLine[] Lines;
    }

    /// <summary>Server → client: a line of feedback (bought, can't afford, too far...).</summary>
    public struct GameNotice : IBroadcast
    {
        public string Text;
    }

    // ------------------------------------------------------------------ skills and stats

    /// <summary>One skill as its owner sees it (index into Skills.json, value and cap in tenths, lock).</summary>
    public struct SkillView
    {
        public ushort Skill;
        public ushort Value;
        public ushort Cap;
        public byte Lock;
    }

    /// <summary>Server → owner: your skills, stats and locks (sent whole on entering and when they change).</summary>
    public struct SkillState : IBroadcast
    {
        public SkillView[] Skills;
        public ushort TotalCap;
        public int Str, Dex, Int;
        public byte StrLock, DexLock, IntLock;
        public int StatCap;
        public int Fame, Karma, Tithing;
    }

    /// <summary>A ship's course: from Origin at server tick Tick, bow to Yaw (degrees, 0 north), it goes Speed m/s
    /// forward (negative: astern) while turning Turn degrees a second. Server and clients place the hull from it.</summary>
    public struct BoatCourse
    {
        public Vector3 Origin;
        public float Yaw, Speed, Turn;
        public uint Tick;
    }

    /// <summary>Client → server: quests. Action: 0 talk to quest-giver Npc, 1 accept Quest, 2 refuse it, 3 resign
    /// Quest, 4 see your quests.</summary>
    public struct QuestAction : IBroadcast
    {
        public int Npc;
        public byte Action;
        public string Quest;
    }

    /// <summary>Server → client: a quest window. Kind: 0 an offer, 1 under way, 2 done, 3 your quests (the log).</summary>
    public struct QuestView : IBroadcast
    {
        public byte Kind;
        public int Npc;
        public string Json;
    }

    /// <summary>Client → server: bulk orders. Action: 0 ask the vendor for one, 1 accept the offer, 2 decline it,
    /// 3 turn in completed orders, 4 see the rewards, 5 claim reward Index, 6 fill deed Item from the backpack, 7 look at
    /// deed Item.</summary>
    public struct BodAction : IBroadcast
    {
        public int Vendor;
        public byte Action;
        public uint Item;
        public int Index;
    }

    /// <summary>Server → client: a bulk order window. Kind: 0 an offer from Vendor, 1 the deed Item's contents, 2 the
    /// rewards at Vendor. Json carries what to show.</summary>
    public struct BodView : IBroadcast
    {
        public byte Kind;
        public int Vendor;
        public uint Item;
        public string Json;
    }

    /// <summary>Client → server: put the ship from this deed or docked ship on the water here (centre tile, heading).</summary>
    public struct PlaceBoat : IBroadcast
    {
        public uint Deed;
        public int X, Y;
        public byte Heading;
    }

    /// <summary>Client → server: an order to a ship (BoatService.Order), by its object id.</summary>
    public struct BoatCommand : IBroadcast
    {
        public int Boat;
        public byte Order;
    }

    /// <summary>Server → client: your readied Bushido/Ninjitsu move and active stance (spell ids; 0 none).</summary>
    public struct MoveState : IBroadcast
    {
        public ushort Move, Stance;
    }

    /// <summary>Client → server: give this much gold from the bank to the ankh you stand by, as tithing points.</summary>
    public struct Tithe : IBroadcast
    {
        public int Amount;
    }

    /// <summary>Client → server: set a skill's lock (Skill = index), or a stat's (Skill = 1000 + 0/1/2 for Str/Dex/Int).</summary>
    public struct SetLock : IBroadcast
    {
        public ushort Skill;
        public byte Lock;
    }

    /// <summary>Client → server: use an item you carry (a bandage, a potion...).</summary>
    public struct UseItem : IBroadcast
    {
        public uint Id;
    }

    // ------------------------------------------------------------------ gathering and crafting

    /// <summary>Client → server: gather from what's in front of me (E again stops).</summary>
    public struct GatherRequest : IBroadcast
    {
        public byte Unused;
    }

    /// <summary>Client → server: make a recipe (system and recipe by index in Crafting.json, material by index).</summary>
    public struct CraftRequest : IBroadcast
    {
        public byte System;
        public ushort Recipe;
        public byte Material;
    }

    // ------------------------------------------------------------------ magic

    /// <summary>Client → server: cast this spell (id in Spells.json) at my current target (or myself).</summary>
    public struct CastSpell : IBroadcast
    {
        public ushort Spell;
        /// <summary>The item it's cast with or on (a recall rune for Recall, Gate Travel and Mark), 0 for none.</summary>
        public uint Item;
        /// <summary>A choice the spell asks for (Polymorph: which form), 0 otherwise.</summary>
        public ushort Option;
    }

    /// <summary>Server → caster: a cast began (State 0, taking Seconds), finished (1), or fizzled / was disturbed (2).</summary>
    public struct CastNotice : IBroadcast
    {
        public ushort Spell;
        public float Seconds;
        public byte State;
    }

    /// <summary>Client → server: claim the plot at tile (X, Y) with this plot deed (from the backpack).</summary>
    public struct ClaimPlot : IBroadcast
    {
        public uint Deed;
        public int X, Y;
    }

    /// <summary>Client → server: build a piece (Housing.json order) on my house at plot tile (X, Y), level, turned Rot quarters.</summary>
    public struct PlacePiece : IBroadcast
    {
        public int House;
        public ushort Piece;
        public byte X, Y, Level, Rot;
    }

    /// <summary>Client → server: take a piece of my house down (its materials come back).</summary>
    public struct RemovePiece : IBroadcast
    {
        public int House;
        public ushort Uid;
    }

    /// <summary>Client → server: my house's name, wall and roof styles, and whether anyone may open its doors.</summary>
    public struct HouseSettings : IBroadcast
    {
        public int House;
        public string Name;
        public byte WallStyle, RoofStyle;
        public bool Public;
    }

    /// <summary>Client → server: add or remove a friend of my house (a character's name).</summary>
    public struct HouseFriend : IBroadcast
    {
        public int House;
        public string Name;
        public bool Add;
    }

    /// <summary>Client → server: give up my plot: the deed comes back, the house's materials go to the bank.</summary>
    public struct Redeed : IBroadcast
    {
        public int House;
    }

    /// <summary>Client → server: use an active skill (Skills.json id) on my current target, or as the skill does.</summary>
    public struct UseSkill : IBroadcast
    {
        public ushort Skill;
        /// <summary>The item it's used on (Arms Lore, Item Identification: chosen from your things), 0 for none.</summary>
        public uint Item;
    }

    /// <summary>
    /// Client → server: an order to my pet (Pet: its object id; 0 = all my pets). Order: 0 Follow, 1 Stay, 2 Guard,
    /// 3 Attack (my target), 4 Stop, 10 Release, 11 Rename (Name), 12 Feed (from my backpack), 13 Open its pack,
    /// 14 Ride it, 15 Get off my mount (Pet ignored).
    /// </summary>
    public struct PetCommand : IBroadcast
    {
        public int Pet;
        public byte Order;
        public string Name;
    }

    /// <summary>Client → server, at an animal trainer: 0 show the stable, 1 stable this pet (Pet), 2 claim stabled pet
    /// Index, 3 buy the pet for sale at Index.</summary>
    public struct StableRequest : IBroadcast
    {
        public int Trainer;
        public byte Action;
        public int Pet;
        public int Index;
    }

    /// <summary>A pet kept in a stable: its kind and seed (what it is, as everywhere) and its name.</summary>
    public struct StabledPet
    {
        public ushort Kind;
        public int Seed;
        public string Name;
    }

    /// <summary>A pet an animal trainer sells.</summary>
    public struct PetForSale
    {
        public ushort Kind;
        public int Price;
    }

    /// <summary>Server → client: a trainer's stable for this character (stabled pets, stalls, pets for sale).</summary>
    public struct StableState : IBroadcast
    {
        public int Trainer;
        public StabledPet[] Stabled;
        public int Stalls;
        public int Fee;
        public PetForSale[] ForSale;
    }

    /// <summary>Server → client: a pack animal's pack (weights in stones).</summary>
    public struct PackState : IBroadcast
    {
        public int Pet;
        public ItemView[] Items;
        public int Weight;
        public int MaxWeight;
    }

    /// <summary>Server → client: what Animal Lore tells about a creature.</summary>
    public struct LoreInfo : IBroadcast
    {
        public int Creature;
        public string Name;
        public int Hits, HitsMax, Str, Dex, Int, Physical, Slots, Loyalty;
        public int Fire, Cold, Poison, Energy, Mana, ManaMax;
        public float Wrestling, Tactics, Tame;
        public bool Pet;
        public string Foods;
    }

    /// <summary>
    /// Server → client: a live content change (LiveContent): one part of a content file's new text, gzipped. The
    /// client puts the parts together and loads the file as the server did. Parts 0: back to the game's own copy.
    /// </summary>
    public struct ContentPatch : IBroadcast
    {
        public string Name;
        public int Part, Parts;
        public byte[] Data;
    }

    /// <summary>Server → client: whether this account is an admin, and whether its player is in admin mode.</summary>
    public struct AdminState : IBroadcast
    {
        public bool IsAdmin;
        public bool Mode;
    }

    /// <summary>Client → server: an admin tool (AdminService.Tool). Text and Number carry its words and amount; tools
    /// that act on someone act on your current target.</summary>
    public struct AdminRequest : IBroadcast
    {
        public byte Tool;
        public string Text;
        public int Number;
    }

    /// <summary>Server → client: an admin tool's answer (Kind 0: a message; 1: the players online, as JSON).</summary>
    public struct AdminReply : IBroadcast
    {
        public byte Kind;
        public string Text;
    }
}

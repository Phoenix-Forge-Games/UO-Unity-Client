using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Items;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Items. Each character carries a backpack and wears what fits their body's slots; both are saved with the
    /// character. A creature's corpse holds the loot its loot packs roll; standing by a corpse you can open it and
    /// take things into your backpack. What you wear fights for you: the weapon in hand sets your damage, speed and
    /// reach, and worn armour's physical resist cuts the damage you take.
    /// <para>
    /// Clients are sent their whole inventory whenever it changes (it's small), and a corpse's contents when they
    /// open it. Everything is decided here; clients only ask.
    /// </para>
    /// </summary>
    public sealed class ItemService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _items;
        [SerializeField] private TextAsset _lootPacks;
        /// <summary>Magic item properties (Content/Magic.json), on both sides: loot rolls them, tooltips read them.</summary>
        [SerializeField] private TextAsset _magic;

        /// <summary>How near (metres) you must be to a corpse to open it or take from it.</summary>
        public const float CorpseReach = 3.5f;
        /// <summary>Most items a backpack holds.</summary>
        public const int BackpackSize = 125;

        public static ItemService Instance { get; private set; }
        /// <summary>The item kinds (server and clients).</summary>
        public static ItemCatalog Catalog { get; private set; }

        /// <summary>Client side: what we carry and wear changed (the whole list).</summary>
        public static event Action<ItemView[]> InventoryChanged;
        /// <summary>Client side: a corpse we opened, or its contents after a take.</summary>
        public static event Action<CorpseContents> CorpseShown;
        /// <summary>Client side: what we carry and wear now.</summary>
        public static ItemView[] Mine { get; private set; } = Array.Empty<ItemView>();

        private LootTable _loot;
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<NetworkConnection, (AccountStore.Character Character, Combatant Body)> _carriers =
            new Dictionary<NetworkConnection, (AccountStore.Character, Combatant)>();

        private static TextAsset _itemsFile;

        /// <summary>Load the item kinds if they aren't yet (other content needs them first).</summary>
        public static void EnsureCatalog()
        {
            if (Catalog == null && _itemsFile != null)
            {
                Catalog = ItemCatalog.Load(_itemsFile);
                Net.Protocol.AddDataFile(_itemsFile);
            }
        }

        private void Awake()
        {
            Instance = this;
            _itemsFile = _items;
            EnsureCatalog();
            Net.Protocol.AddDataFile(_lootPacks);
            if (_magic != null)
            {
                MagicProps.Load(_magic);
                Net.Protocol.AddDataFile(_magic);
                global::UOGame.Content.LiveContent.Register(_magic, MagicProps.Load);
            }

            ItemEffects.Hook();
            _network.ServerManager.RegisterBroadcast<WearItem>(OnWear);
            _network.ServerManager.RegisterBroadcast<OpenCorpse>(OnOpenCorpse);
            _network.ServerManager.RegisterBroadcast<TakeFromCorpse>(OnTake);
            _network.ServerManager.OnRemoteConnectionState += (c, a) =>
            {
                if (a.ConnectionState == RemoteConnectionState.Stopped)
                {
                    _carriers.Remove(c);
                    _banking.Remove(c);
                }
            };
            _network.ClientManager.RegisterBroadcast<InventoryState>((m, c) =>
            {
                Mine = m.Items ?? Array.Empty<ItemView>();
                InventoryChanged?.Invoke(Mine);
            });
            _network.ClientManager.RegisterBroadcast<CorpseContents>((m, c) => CorpseShown?.Invoke(m));
            RegisterMoving();
        }

        private void Update()
        {
            if (_network.IsServerStarted)
            {
                CheckBanks();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private LootTable Loot => _loot ??= LootTable.Load(_lootPacks, Catalog);

        // ------------------------------------------------------------------ carriers

        /// <summary>Server: a character entered the world (outfit them if new, apply what they wear, tell them).</summary>
        public void Enter(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (!character.Outfitted)
            {
                character.Outfitted = true;
                StartingKit(character);
            }

            character.Items.RemoveAll(i => Catalog.Find(i.Type) == null); // kinds the content no longer has
            if (character.LookSeed == 0)
            {
                // Characters from before looks existed: roll one, once, from their id (the content's hair and hue rules).
                character.LookSeed = new System.Random(character.Id * 7919 + 17).Next(1, int.MaxValue);
                Look rolled = Content.Outfits.RollPlayer(character.LookSeed);
                character.Female = rolled.Female;
                character.Skin = rolled.Skin;
                character.Hair = rolled.Hair;
                character.HairHue = rolled.HairHue;
                character.Beard = rolled.Beard;
                character.BeardHue = rolled.BeardHue;
            }

            _carriers[connection] = (character, body);
            Apply(character, body);
            Send(connection, character);
        }

        /// <summary>
        /// A new character's belongings, by the creation rules and in their order: the everyday backpack, the
        /// profession's kit, the items each starting skill brings, then everyday clothes (for the professions that
        /// get them). Gear whose slot is already taken goes in the backpack; clothes whose slot is taken are skipped.
        /// </summary>
        private void StartingKit(AccountStore.Character character)
        {
            Content.Profession profession = Content.CreationRules.FindProfession(character.Profession);
            int seed = character.LookSeed != 0 ? character.LookSeed : character.Id;
            var taken = new HashSet<WearSlot>();

            void Pack(Newtonsoft.Json.Linq.JArray entries, int salt)
            {
                foreach ((ItemDef def, int amount, ushort hue, long data) in Content.Outfits.RollPack(entries, character.Female, seed + salt))
                {
                    AddWithData(character, def, amount, hue, data);
                }
            }

            void Wear(Newtonsoft.Json.Linq.JArray entries, int salt, bool clothes)
            {
                foreach ((ItemDef def, int amount, ushort hue, long data) in Content.Outfits.RollPack(entries, character.Female, seed + salt))
                {
                    if (def.Wearable && taken.Add(def.Slot))
                    {
                        character.Items.Add(new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = def.Type, Amount = 1, Hue = hue, Slot = (byte)def.Slot, Data = data });
                    }
                    else if (!clothes)
                    {
                        AddWithData(character, def, amount, hue, data);
                    }
                }
            }

            Pack(Content.CreationRules.Backpack, 1);
            if (profession != null)
            {
                Wear(profession.Equip, 2, false);
                Pack(profession.Pack, 3);
            }

            if (profession == null || !profession.SkipSkillItems)
            {
                int salt = 10;
                foreach (AccountStore.SkillValue skill in character.Skills)
                {
                    if (Content.CreationRules.SkillItems[skill.Skill] is Newtonsoft.Json.Linq.JObject items)
                    {
                        if (items["RemoveLayer"]?.Type == Newtonsoft.Json.Linq.JTokenType.String &&
                            Enum.TryParse((string)items["RemoveLayer"], out WearSlot cleared))
                        {
                            taken.Remove(cleared);
                            character.Items.RemoveAll(it => it.Slot == (byte)cleared);
                        }

                        Pack(items["Pack"] as Newtonsoft.Json.Linq.JArray, salt++);
                        Wear(items["Equip"] as Newtonsoft.Json.Linq.JArray, salt++, false);
                    }
                }
            }

            if (Content.CreationRules.ClothesFor.Contains(character.Profession))
            {
                Wear(Content.CreationRules.Clothes, 4, true);
            }
        }

        /// <summary>Into the backpack; an item that holds something (data) is always its own item, never stacked.</summary>
        private static void AddWithData(AccountStore.Character character, ItemDef def, int amount, ushort hue, long data)
        {
            if (data == 0)
            {
                AddToBackpack(character, def.Type, amount, hue);
                return;
            }

            character.Items.Add(new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = def.Type, Amount = 1, Hue = hue, Data = data });
        }

        private void Give(AccountStore.Character character, string type, int amount, WearSlot wear = WearSlot.None)
        {
            ItemDef def = Catalog.Find(type);
            if (def == null)
            {
                return;
            }

            if (wear == WearSlot.None)
            {
                AddToBackpack(character, type, amount, 0);
            }
            else
            {
                character.Items.Add(new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = type, Amount = 1, Slot = (byte)wear });
            }
        }

        /// <summary>Into the backpack, merging with a stack of the same kind. False when the backpack is full.</summary>
        private static bool AddToBackpack(AccountStore.Character character, string type, int amount, int hue, uint id = 0,
            bool exceptional = false, string resource = "")
        {
            ItemDef def = Catalog.Find(type);
            resource ??= "";
            if (def != null && def.Stackable)
            {
                AccountStore.Item stack = character.Items.Find(i => i.Slot == 0 && i.Type == type && i.Hue == hue &&
                                                                    i.Exceptional == exceptional && (i.Resource ?? "") == resource);
                if (stack != null)
                {
                    stack.Amount += amount;
                    return true;
                }
            }

            if (character.Items.FindAll(i => i.Slot == 0).Count >= BackpackSize)
            {
                return false;
            }

            character.Items.Add(new AccountStore.Item
            {
                Id = id != 0 ? id : GameAuthenticator.Store.NewItemId(),
                Type = type,
                Amount = Math.Max(1, amount),
                Hue = hue,
                Data = SpellService.SchoolOfBook(type)?.BookContent ?? 0, // a book of Chivalry comes with its spells
                Exceptional = exceptional,
                Resource = resource,
            });
            return true;
        }

        /// <summary>Server: a crafted item into the backpack, remembering whether it's exceptional and its material.</summary>
        public static bool GiveCrafted(AccountStore.Character character, string type, int amount, int hue, bool exceptional, string resource) =>
            AddToBackpack(character, type, amount, hue, 0, exceptional, resource);

        private void Send(NetworkConnection connection, AccountStore.Character character)
        {
            _network.ServerManager.Broadcast(connection, new InventoryState { Items = Views(character.Items) });
        }

        private static ItemView[] Views(List<AccountStore.Item> items)
        {
            var views = new ItemView[items.Count];
            for (int i = 0; i < views.Length; i++)
            {
                AccountStore.Item item = items[i];
                views[i] = new ItemView { Id = item.Id, Kind = Catalog.Find(item.Type)?.Kind ?? 0, Amount = item.Amount, Hue = item.Hue, Slot = item.Slot, Data = item.Data, Label = LabelOf(item), Props = item.Props != null && item.Props.Count > 0 ? item.Props.ToArray() : null };
            }

            return views;
        }

        /// <summary>Server: work out how a character fights again (their gear, or their form, changed).</summary>
        public static void Reapply(AccountStore.Character character, Combatant body) => Apply(character, body);

        /// <summary>What the character wears decides how they fight: the weapon in hand and armour's resist.</summary>
        private static void Apply(AccountStore.Character character, Combatant body)
        {
            if (body == null)
            {
                return;
            }

            WeaponStats weapon = null;
            AccountStore.Item weaponItem = null;
            var resists = new int[5];
            System.Array.Clear(body.ItemProps, 0, body.ItemProps.Length);
            body.Slayers.Clear();
            foreach (AccountStore.Item item in character.Items)
            {
                ItemDef def = item.Slot != 0 ? Catalog.Find(item.Type) : null;
                if (def == null)
                {
                    continue;
                }

                if (def.Weapon != null)
                {
                    weapon = def.Weapon;
                    if (def.Weapon.MaxDamage > 0)
                    {
                        weaponItem = item;
                    }
                }

                // Worn magic adds up. Elements are damage types on a weapon and resists on anything else.
                bool isWeapon = def.Weapon != null && def.Weapon.MaxDamage > 0;
                for (int i = 0; item.Props != null && i + 1 < item.Props.Count; i += 2)
                {
                    int id = item.Props[i], value = item.Props[i + 1];
                    if (!MagicProps.Props.TryGetValue(id, out PropDef prop))
                    {
                        continue;
                    }

                    if (prop.Group == "AosElementAttribute")
                    {
                        if (!isWeapon)
                        {
                            resists[System.Array.IndexOf(Combatant.Elements, prop.Name)] += value;
                        }

                        continue;
                    }

                    if (prop.Name.StartsWith("Resist", System.StringComparison.Ordinal) && prop.Name.EndsWith("Bonus", System.StringComparison.Ordinal))
                    {
                        int e = System.Array.IndexOf(Combatant.Elements, prop.Name.Substring(6, prop.Name.Length - 11));
                        if (e >= 0)
                        {
                            resists[e] += value;
                        }

                        continue;
                    }

                    if (prop.IsSlayer)
                    {
                        if (isWeapon)
                        {
                            body.Slayers.Add(prop.Name);
                        }

                        continue;
                    }

                    if (id < body.ItemProps.Length)
                    {
                        body.ItemProps[id] += value;
                    }
                }

                if (def.Armor != null)
                {
                    resists[0] += def.Armor.Physical;
                    resists[1] += def.Armor.Fire;
                    resists[2] += def.Armor.Cold;
                    resists[3] += def.Armor.Poison;
                    resists[4] += def.Armor.Energy;
                }
            }

            string skill = weapon != null && weapon.MaxDamage > 0 ? weapon.Skill : "Wrestling";
            float skillValue = CharacterService.SkillOf(character, skill);
            if (body.Prop(MagicProps.Id("UseBestSkill")) > 0)
            {
                // Use Best Weapon Skill: the best of the melee skills.
                foreach (string other in new[] { "Swords", "Macing", "Fencing", "Wrestling" })
                {
                    if (CharacterService.SkillOf(character, other) > skillValue)
                    {
                        skill = other;
                        skillValue = CharacterService.SkillOf(character, other);
                    }
                }
            }

            int mage = weaponItem != null ? MagicProps.Get(weaponItem.Props, MagicProps.Id("MageWeapon")) : 0;
            if (mage > 0 && CharacterService.SkillOf(character, "Magery") - (30 - mage) > skillValue)
            {
                // Mage Weapon: fight with Magery, less (30 - its value).
                skill = "Magery";
                skillValue = CharacterService.SkillOf(character, "Magery") - (30 - mage);
            }

            body.SkillName = skill;
            body.SetSkills(skillValue, CharacterService.SkillOf(character, "Tactics"));

            // A weapon's elemental damage: the rest stays physical.
            var mix = new[] { 100, 0, 0, 0, 0 };
            for (int e = 1; weaponItem != null && e < 5; e++)
            {
                int share = MagicProps.Get(weaponItem.Props, MagicProps.Id("Element" + Combatant.Elements[e]));
                mix[e] = Mathf.Min(share, mix[0]);
                mix[0] -= mix[e];
            }

            body.SetDamageMix(mix);
            body.SetItemBonuses(body.Prop(MagicProps.Id("BonusStr")), body.Prop(MagicProps.Id("BonusDex")), body.Prop(MagicProps.Id("BonusInt")),
                body.Prop(MagicProps.Id("BonusHits")), body.Prop(MagicProps.Id("BonusStam")), body.Prop(MagicProps.Id("BonusMana")));
            if (weapon != null && weapon.MaxDamage > 0)
            {
                float reach = weapon.Ranged ? weapon.Range * UOClient.WorldSpace.TileSize : 2.2f + (weapon.Range - 1) * UOClient.WorldSpace.TileSize;
                body.SetWeapon(new Vector2(weapon.MinDamage, weapon.MaxDamage), weapon.Speed, reach);
            }
            else
            {
                // Bare hands (wrestling); Horrific Beast's claws hit 5-15.
                body.SetWeapon(body.Form == "HorrificBeast" ? new Vector2(5f, 15f) : new Vector2(1f, 8f), 2.5f, 2.2f);
            }

            body.SetBaseResists(resists); // capped at 70 (UO's player cap), spells' shifts on top

            // Everyone sees what they wear.
            PlayerIdentity identity = body.GetComponent<PlayerIdentity>();
            if (identity != null)
            {
                var gear = new List<Gear>();
                foreach (AccountStore.Item item in character.Items)
                {
                    ItemDef def = item.Slot != 0 ? Catalog.Find(item.Type) : null;
                    if (def != null)
                    {
                        gear.Add(new Gear { Item = def.Kind, Hue = (ushort)item.Hue });
                    }
                }

                identity.Look.Value = new Look
                {
                    Seed = character.LookSeed,
                    Female = character.Female,
                    Gargoyle = character.Gargoyle,
                    Skin = (ushort)character.Skin,
                    Hair = (ushort)character.Hair,
                    HairHue = (ushort)character.HairHue,
                    Beard = (ushort)character.Beard,
                    BeardHue = (ushort)character.BeardHue,
                    Gear = gear.ToArray(),
                };
            }
        }

        // ------------------------------------------------------------------ wearing

        private void OnWear(NetworkConnection connection, WearItem request, Channel channel)
        {
            if (!_carriers.TryGetValue(connection, out var carrier) || carrier.Body == null || carrier.Body.Dead.Value)
            {
                return;
            }

            AccountStore.Character character = carrier.Character;
            AccountStore.Item item = character.Items.Find(i => i.Id == request.Id);
            ItemDef def = item != null ? Catalog.Find(item.Type) : null;
            if (def == null)
            {
                return;
            }

            if (!request.Wear)
            {
                if (item.Slot != 0 && character.Items.FindAll(i => i.Slot == 0).Count < BackpackSize)
                {
                    item.Slot = 0;
                }
            }
            else if (item.Slot == 0 && def.Wearable)
            {
                // Whatever is in the way goes back into the backpack: the same slot, and the other hand when a
                // two-handed weapon meets anything held (or anything held meets a two-handed weapon).
                foreach (AccountStore.Item worn in character.Items)
                {
                    if (worn.Slot == 0)
                    {
                        continue;
                    }

                    ItemDef wornDef = Catalog.Find(worn.Type);
                    bool sameSlot = worn.Slot == (byte)def.Slot;
                    bool hands = IsHeld(def) && wornDef != null && IsHeld(wornDef) &&
                                 (def.Weapon?.TwoHanded == true || wornDef.Weapon?.TwoHanded == true);
                    if (sameSlot || hands)
                    {
                        worn.Slot = 0;
                    }
                }

                item.Slot = (byte)def.Slot;
            }

            Apply(character, carrier.Body);
            Send(connection, character);
        }

        private static bool IsHeld(ItemDef def) => def.Slot == WearSlot.OneHanded || def.Slot == WearSlot.TwoHanded;

        // ------------------------------------------------------------------ moving: backpack, bank, ground

        /// <summary>How near a banker you must stay for your bank box to stay open (metres).</summary>
        public const float BankReach = 6f;
        /// <summary>Most items a bank box holds.</summary>
        public const int BankSize = 125;
        /// <summary>How long a dropped item lies before it decays (seconds).</summary>
        public const float GroundSeconds = 20f * 60f;
        public const float PickUpReach = 3f;

        /// <summary>Client side: your bank box opened, changed or closed.</summary>
        public static event Action<BankState> BankChanged;
        /// <summary>Client side: a line of feedback from the server.</summary>
        public static event Action<string> Notice;

        /// <summary>Client: show a line of feedback as if the server had sent it.</summary>
        public static void ShowNotice(string text) => Notice?.Invoke(text);

        private readonly Dictionary<NetworkConnection, NpcMobile> _banking = new Dictionary<NetworkConnection, NpcMobile>();
        private float _nextBankCheck;

        private void RegisterMoving()
        {
            _network.ServerManager.RegisterBroadcast<UseItem>(OnUseItem);
            _network.ServerManager.RegisterBroadcast<MoveItem>(OnMove);
            _network.ServerManager.RegisterBroadcast<OpenBank>(OnOpenBank);
            _network.ServerManager.RegisterBroadcast<PickUp>(OnPickUp);
            _network.ClientManager.RegisterBroadcast<BankState>((m, c) => BankChanged?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<GameNotice>((m, c) => Notice?.Invoke(m.Text));
        }

        /// <summary>Server: take `amount` of a kind from the backpack; false (and nothing taken) if there isn't enough.</summary>
        public bool Consume(AccountStore.Character character, string type, int amount)
        {
            int have = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                have += item.Slot == 0 && string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase) ? item.Amount : 0;
            }

            if (have < amount)
            {
                return false;
            }

            foreach (AccountStore.Item item in new List<AccountStore.Item>(character.Items))
            {
                if (amount <= 0)
                {
                    break;
                }

                if (item.Slot == 0 && string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase))
                {
                    int take = Math.Min(item.Amount, amount);
                    Take(character.Items, item, take);
                    amount -= take;
                }
            }

            foreach (KeyValuePair<NetworkConnection, (AccountStore.Character Character, Combatant Body)> pair in _carriers)
            {
                if (pair.Value.Character == character)
                {
                    Send(pair.Key, character);
                }
            }

            return true;
        }

        /// <summary>Using something you carry: bandages heal (more uses come with their systems).</summary>
        private void OnUseItem(NetworkConnection connection, UseItem request, Channel channel)
        {
            if (!TryCarrier(connection, out AccountStore.Character character, out Combatant body))
            {
                return;
            }

            AccountStore.Item item = character.Items.Find(i => i.Id == request.Id && i.Slot == 0);
            if (item == null)
            {
                return;
            }

            if (string.Equals(item.Type, "Bandage", StringComparison.OrdinalIgnoreCase))
            {
                SkillService.Instance?.Bandage(connection, character, body);
            }
            else if (HarvestService.Instance != null && HarvestService.Instance.IsOre(item.Type))
            {
                HarvestService.Instance.Smelt(connection, character, body, item);
            }
            else if (BulkOrderService.Instance != null && BulkOrderService.Instance.TryShow(connection, character, item))
            {
                // a bulk order deed: its window
            }
            else if (BoatService.Instance != null && BoatService.Instance.TryPlace(connection, character, body, item))
            {
                // a ship deed or a docked ship: its ship goes on the water
            }
            else if (SpellService.TryUseStone(connection, character, body, item, out string stoneSaid))
            {
                if (stoneSaid != null)
                {
                    Tell(connection, stoneSaid);
                }
            }
            else if (SpellService.TryInscribe(character, item, out string inscribed))
            {
                Tell(connection, inscribed);
                Resend(connection, character);
            }
            else
            {
                Tell(connection, "You can't use that yet.");
            }
        }

        /// <summary>Server: a line of feedback to one player.</summary>
        public void Tell(NetworkConnection connection, string text) =>
            _network.ServerManager.Broadcast(connection, new GameNotice { Text = text });

        /// <summary>Server: the character a connection plays and their body, if they're in the world.</summary>
        public bool TryCarrier(NetworkConnection connection, out AccountStore.Character character, out Combatant body)
        {
            bool found = _carriers.TryGetValue(connection, out var carrier);
            character = found ? carrier.Character : null;
            body = found ? carrier.Body : null;
            return found && body != null;
        }

        /// <summary>Server: tell a player what they carry now.</summary>
        public void Resend(NetworkConnection connection, AccountStore.Character character) => Send(connection, character);

        private void OnOpenBank(NetworkConnection connection, OpenBank request, Channel channel)
        {
            if (!TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            NpcMobile banker = _network.ServerManager.Objects.Spawned.TryGetValue(request.BankerId, out NetworkObject nob) ? nob.GetComponent<NpcMobile>() : null;
            if (banker == null || banker.Def == null || !banker.Def.Banker ||
                (banker.transform.position - body.transform.position).magnitude > BankReach)
            {
                Tell(connection, "You need to stand by a banker to use your bank box.");
                return;
            }

            _banking[connection] = banker;
            SendBank(connection, character, true);
        }

        /// <summary>Server: something a player said. "Bank" near a banker opens their bank box, as in UO.</summary>
        public void Heard(NetworkConnection connection, string text)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(text ?? string.Empty, @"bank", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                !TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            NpcMobile nearest = null;
            float best = 12f; // a banker who can hear you
            foreach (NetworkObject nob in _network.ServerManager.Objects.Spawned.Values)
            {
                NpcMobile npc = nob.GetComponent<NpcMobile>();
                float d = npc != null && npc.Def != null && npc.Def.Banker ? (npc.transform.position - body.transform.position).magnitude : float.MaxValue;
                if (d < best)
                {
                    best = d;
                    nearest = npc;
                }
            }

            if (nearest != null)
            {
                _banking[connection] = nearest;
                SendBank(connection, character, true);
            }
        }

        private void SendBank(NetworkConnection connection, AccountStore.Character character, bool open) =>
            _network.ServerManager.Broadcast(connection, new BankState { Open = open, Items = open ? Views(character.Bank) : Array.Empty<ItemView>() });

        private bool BankOpen(NetworkConnection connection, Combatant body) =>
            _banking.TryGetValue(connection, out NpcMobile banker) && banker != null && banker.IsSpawned &&
            (banker.transform.position - body.transform.position).magnitude <= 13f;

        /// <summary>Close the bank boxes of players who walked away from their banker.</summary>
        private void CheckBanks()
        {
            if (Time.time < _nextBankCheck || _banking.Count == 0)
            {
                return;
            }

            _nextBankCheck = Time.time + 0.5f;
            foreach (NetworkConnection connection in new List<NetworkConnection>(_banking.Keys))
            {
                if (!TryCarrier(connection, out AccountStore.Character character, out Combatant body) || !BankOpen(connection, body))
                {
                    _banking.Remove(connection);
                    if (character != null)
                    {
                        SendBank(connection, character, false);
                    }
                }
            }
        }

        private void OnMove(NetworkConnection connection, MoveItem request, Channel channel)
        {
            if (!TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            bool bank = BankOpen(connection, body);
            List<AccountStore.Item> pack = request.Container != 0 ? PetService.Instance?.PackOf(connection, character, body, request.Container) : null;
            List<AccountStore.Item> from = character.Items;
            AccountStore.Item item = character.Items.Find(i => i.Id == request.Id);
            if (item == null && bank)
            {
                item = character.Bank.Find(i => i.Id == request.Id);
                from = character.Bank;
            }

            if (item == null && pack != null)
            {
                item = pack.Find(i => i.Id == request.Id);
                from = pack;
            }

            if (item == null)
            {
                return;
            }

            if (item.Slot != 0)
            {
                Tell(connection, "Take it off first.");
                return;
            }

            int amount = request.Amount <= 0 ? item.Amount : Math.Min(request.Amount, item.Amount);
            switch (request.To)
            {
                case ItemPlace.Backpack when from != character.Items:
                    if (!AddTo(character.Items, BackpackSize, item, amount))
                    {
                        Tell(connection, "Your backpack is full.");
                        return;
                    }

                    break;
                case ItemPlace.Bank when bank && from != character.Bank:
                    if (!AddTo(character.Bank, BankSize, item, amount))
                    {
                        Tell(connection, "Your bank box is full.");
                        return;
                    }

                    break;
                case ItemPlace.Pack when pack != null && from != pack:
                    if (PetService.Weight(pack) + WeightOf(item.Type) * amount > Content.PetRules.PackMaxWeight)
                    {
                        Tell(connection, "That pack animal can't carry that much.");
                        return;
                    }

                    if (!AddTo(pack, 1000, item, amount))
                    {
                        return;
                    }

                    break;
                case ItemPlace.Ground:
                    Drop(body.transform, item, amount);
                    break;
                default:
                    return;
            }

            Take(from, item, amount);
            Send(connection, character);
            if (bank)
            {
                SendBank(connection, character, true);
            }

            if (pack != null)
            {
                PetService.Instance.SendPack(connection, request.Container, pack);
            }
        }

        /// <summary>Put `amount` of an item into a list (merging stacks); false when there's no room.</summary>
        private static bool AddTo(List<AccountStore.Item> list, int size, AccountStore.Item item, int amount)
        {
            ItemDef def = Catalog.Find(item.Type);
            if (def != null && def.Stackable)
            {
                AccountStore.Item stack = list.Find(i => i.Slot == 0 && i.Type == item.Type && i.Hue == item.Hue);
                if (stack != null)
                {
                    stack.Amount += amount;
                    return true;
                }
            }

            if (list.FindAll(i => i.Slot == 0).Count >= size)
            {
                return false;
            }

            bool whole = amount >= item.Amount;
            list.Add(new AccountStore.Item
            {
                Id = whole ? item.Id : GameAuthenticator.Store.NewItemId(),
                Type = item.Type,
                Amount = amount,
                Hue = item.Hue,
                Data = item.Data,
                Text = item.Text,
                Props = item.Props != null ? new List<int>(item.Props) : new List<int>(),
            });
            return true;
        }

        /// <summary>Remove `amount` from an item (the whole item when it's all of it).</summary>
        private static void Take(List<AccountStore.Item> list, AccountStore.Item item, int amount)
        {
            if (amount >= item.Amount)
            {
                list.Remove(item);
            }
            else
            {
                item.Amount -= amount;
            }
        }

        // ------------------------------------------------------------------ the ground

        [SerializeField] private NetworkObject _groundPrefab;

        private void Drop(Transform at, AccountStore.Item item, int amount)
        {
            if (_groundPrefab == null)
            {
                return;
            }

            // Just in front of their feet, settled on whatever is below.
            Vector3 spot = at.position + at.forward * 0.8f + Vector3.up * 1.5f;
            if (Physics.Raycast(spot, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                spot = hit.point;
            }
            else
            {
                spot = at.position;
            }

            NetworkObject nob = _network.GetPooledInstantiated(_groundPrefab, spot, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), true);
            nob.GetComponent<GroundItem>().SetServer(new AccountStore.Item
            {
                Id = amount >= item.Amount ? item.Id : GameAuthenticator.Store.NewItemId(),
                Type = item.Type,
                Amount = amount,
                Hue = item.Hue,
                Data = item.Data,
                Text = item.Text,
                Props = item.Props != null ? new List<int>(item.Props) : new List<int>(),
            }, GroundSeconds);
            _network.ServerManager.Spawn(nob);
        }

        private void OnPickUp(NetworkConnection connection, PickUp request, Channel channel)
        {
            if (!TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value ||
                !_network.ServerManager.Objects.Spawned.TryGetValue(request.ObjectId, out NetworkObject nob))
            {
                return;
            }

            GroundItem ground = nob.GetComponent<GroundItem>();
            if (ground == null || ground.Item == null || (ground.transform.position - body.transform.position).magnitude > PickUpReach)
            {
                return;
            }

            if (!AddTo(character.Items, BackpackSize, ground.Item, ground.Item.Amount))
            {
                Tell(connection, "Your backpack is full.");
                return;
            }

            ground.Item = null;
            _network.ServerManager.Despawn(nob);
            Send(connection, character);
        }

        // ------------------------------------------------------------------ gold

        /// <summary>Gold carried (backpack) and banked.</summary>
        public static (int Carried, int Banked) Gold(AccountStore.Character character)
        {
            int carried = 0, banked = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                carried += item.Slot == 0 && Catalog.Find(item.Type)?.IsGold == true ? item.Amount : 0;
            }

            foreach (AccountStore.Item item in character.Bank)
            {
                banked += Catalog.Find(item.Type)?.IsGold == true ? item.Amount : 0;
            }

            return (carried, banked);
        }

        /// <summary>Pay gold: from the backpack first, then the bank (as UO vendors do). False if there isn't enough.</summary>
        public static bool Pay(AccountStore.Character character, int amount)
        {
            (int carried, int banked) = Gold(character);
            if (carried + banked < amount)
            {
                return false;
            }

            foreach (List<AccountStore.Item> list in new[] { character.Items, character.Bank })
            {
                foreach (AccountStore.Item item in new List<AccountStore.Item>(list))
                {
                    if (amount <= 0)
                    {
                        return true;
                    }

                    if (item.Slot != 0 || Catalog.Find(item.Type)?.IsGold != true)
                    {
                        continue;
                    }

                    int take = Math.Min(item.Amount, amount);
                    Take(list, item, take);
                    amount -= take;
                }
            }

            return amount <= 0;
        }

        /// <summary>Into the backpack, merging with a stack of the same kind (public for trade). False when full.</summary>
        public static bool GiveToBackpack(AccountStore.Character character, string type, int amount, int hue = 0) =>
            AddToBackpack(character, type, amount, hue);

        /// <summary>What an item says about itself on the client (a rune's destination), or null.</summary>
        private static string LabelOf(AccountStore.Item item)
        {
            if (string.IsNullOrEmpty(item.Text))
            {
                return null;
            }

            string[] parts = item.Text.Split('|');
            return parts.Length >= 5 ? $"a recall rune for {parts[4]}" : null;
        }

        /// <summary>An item kind's weight in stones (the catalog's, 1 when unknown).</summary>
        public static float WeightOf(string type) => Catalog?.Find(type)?.Weight ?? 1f;

        /// <summary>Server: the client's views of a list of items.</summary>
        public static ItemView[] ViewsOf(List<AccountStore.Item> items) => Views(items);

        /// <summary>Server: put items in a character's bank box (stacking where they stack).</summary>
        public static void GiveToBank(AccountStore.Character character, string type, int amount, int hue = 0)
        {
            ItemDef def = Catalog.Find(type);
            AccountStore.Item stack = def != null && def.Stackable ? character.Bank.Find(i => i.Type == type && i.Hue == hue) : null;
            if (stack != null)
            {
                stack.Amount += amount;
                return;
            }

            character.Bank.Add(new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = type, Amount = Math.Max(1, amount), Hue = hue });
        }

        // ------------------------------------------------------------------ corpses and loot

        /// <summary>Server: roll a creature's loot packs into a list of items for its corpse.</summary>
        public List<AccountStore.Item> RollLoot(CreatureDef creature, int luck = 0)
        {
            var items = new List<AccountStore.Item>();
            if (creature?.Loot == null || GameAuthenticator.Store == null)
            {
                return items;
            }

            foreach ((string pack, int times) in creature.Loot)
            {
                for (int n = 0; n < Math.Max(1, times); n++)
                {
                    foreach ((string type, int amount) in Loot.Roll(pack, _random))
                    {
                        AccountStore.Item stack = Catalog.Find(type)?.Stackable == true ? items.Find(i => i.Type == type) : null;
                        if (stack != null)
                        {
                            stack.Amount += amount;
                        }
                        else
                        {
                            var rolled = new AccountStore.Item { Id = GameAuthenticator.Store.NewItemId(), Type = type, Amount = amount };
                            MagicLoot.Generate(rolled, Catalog.Find(type), MagicLoot.BaseBudget(creature.Fame), luck, _random); // weapons, armour, jewels...
                            items.Add(rolled);
                        }
                    }
                }
            }

            return items;
        }

        private Corpse NearCorpse(NetworkConnection connection, int objectId)
        {
            NetworkObject me = connection.FirstObject;
            if (me == null || !_network.ServerManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject nob))
            {
                return null;
            }

            Corpse corpse = nob.GetComponent<Corpse>();
            return corpse != null && (corpse.transform.position - me.transform.position).sqrMagnitude <= CorpseReach * CorpseReach ? corpse : null;
        }

        private void OnOpenCorpse(NetworkConnection connection, OpenCorpse request, Channel channel)
        {
            Corpse corpse = NearCorpse(connection, request.ObjectId);
            if (corpse != null)
            {
                ShowCorpse(connection, corpse);
            }
        }

        private void ShowCorpse(NetworkConnection connection, Corpse corpse)
        {
            _network.ServerManager.Broadcast(connection, new CorpseContents
            {
                ObjectId = corpse.ObjectId,
                Name = corpse.Name,
                Items = Views(corpse.Loot),
            });
        }

        private void OnTake(NetworkConnection connection, TakeFromCorpse request, Channel channel)
        {
            Corpse corpse = NearCorpse(connection, request.ObjectId);
            if (corpse == null || !_carriers.TryGetValue(connection, out var carrier) || carrier.Body == null || carrier.Body.Dead.Value)
            {
                return;
            }

            for (int i = corpse.Loot.Count - 1; i >= 0; i--)
            {
                AccountStore.Item item = corpse.Loot[i];
                if (request.Id != 0 && item.Id != request.Id)
                {
                    continue;
                }

                if (AddToBackpack(carrier.Character, item.Type, item.Amount, item.Hue, item.Id))
                {
                    corpse.Loot.RemoveAt(i);
                }
            }

            Send(connection, carrier.Character);
            ShowCorpse(connection, corpse);
        }
    }
}

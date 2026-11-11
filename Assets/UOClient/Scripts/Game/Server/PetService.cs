using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Pets, by UO's rules (Pets.json, <see cref="PetRules"/>):
    /// <list type="bullet">
    /// <item><b>Taming:</b> Animal Taming used with a tamable creature targeted. The tamer coaxes it for 3-4 ticks, then
    /// an Animal Taming check across its requirement decides; success makes it a pet whose skills are scaled down.</item>
    /// <item><b>Orders</b> (buttons, no cursor): follow, stay, guard, attack my target, stop, release, rename, feed. Each is
    /// checked against the control chance (owner's taming and lore against the creature's requirement, less for an unhappy pet).</item>
    /// <item><b>Loyalty</b> falls by the hour; feeding the food it likes restores it; at nothing it goes wild. The owner
    /// feeding it starts <b>bonding</b>; after a week it's bonded and survives death as a ghost until brought back.</item>
    /// <item><b>Followers:</b> at most FollowersMax control slots.</item>
    /// <item><b>Stables</b> at animal trainers (stable, claim, and buy the pets they sell).</item>
    /// </list>
    /// Pets live with their owner: they're saved on the character, leave the world with them and come back beside them.
    /// </summary>
    public sealed class PetService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private NetworkObject _mobilePrefab;
        [SerializeField] private TextAsset _pets;
        [SerializeField] private TextAsset _shops;

        public static PetService Instance { get; private set; }

        /// <summary>Client: the stable window's contents (set when a trainer answers).</summary>
        public static event Action<StableState> StableShown;
        /// <summary>Client: a pack animal's pack, opened or changed.</summary>
        public static event Action<PackState> PackShown;
        /// <summary>Client: what Animal Lore told.</summary>
        public static event Action<LoreInfo> LoreShown;

        private sealed class Taming
        {
            public NpcMobile Creature;
            public bool Paralyzed;
        }

        private readonly Dictionary<NetworkConnection, Taming> _taming = new Dictionary<NetworkConnection, Taming>();
        private readonly HashSet<NpcMobile> _beingTamed = new HashSet<NpcMobile>();
        private readonly Dictionary<int, List<NpcMobile>> _following = new Dictionary<int, List<NpcMobile>>();
        /// <summary>The mount each rider (character id) is on: carried, not in the world.</summary>
        private readonly Dictionary<int, AccountStore.Pet> _riding = new Dictionary<int, AccountStore.Pet>();
        private const float RideReach = 4f;
        private const float PackReach = 4f;
        private readonly System.Random _random = new System.Random();
        private float _nextHour;

        private const float StableReach = 12f;

        private void Awake()
        {
            Instance = this;
            PetRules.Load(_pets, _shops);
            Protocol.AddDataFile(_pets);
            global::UOGame.Content.LiveContent.Register(_pets, f => PetRules.Load(f, _shops));
            SkillService.RegisterUse("AnimalTaming", Tame);
            SkillService.RegisterUse("AnimalLore", Lore);
            _network.ServerManager.RegisterBroadcast<PetCommand>(OnCommand);
            _network.ServerManager.RegisterBroadcast<StableRequest>(OnStable);
            _network.ClientManager.RegisterBroadcast<StableState>((m, c) => StableShown?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<PackState>((m, c) => PackShown?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<LoreInfo>((m, c) => LoreShown?.Invoke(m));
            _nextHour = 3600f;
        }

        private void Update()
        {
            if (!_network.IsServerStarted || Time.time < _nextHour)
            {
                return;
            }

            _nextHour = Time.time + 3600f;
            HourlyLoyalty();
        }

        // ------------------------------------------------------------------ followers

        /// <summary>Server: the pets following a character now.</summary>
        public List<NpcMobile> Following(int characterId)
        {
            if (!_following.TryGetValue(characterId, out List<NpcMobile> list))
            {
                _following[characterId] = list = new List<NpcMobile>();
            }

            list.RemoveAll(p => p == null || !p.IsSpawned || !p.IsPet || p.OwnerCharacter != characterId);
            return list;
        }

        public int FollowerSlots(int characterId)
        {
            int slots = 0;
            foreach (NpcMobile p in Following(characterId))
            {
                slots += p.Slots >= 0 ? p.Slots : Math.Max(1, p.Def.ControlSlots);
            }

            if (_riding.TryGetValue(characterId, out AccountStore.Pet mount) && SpawnService.Content.Creatures.TryGetValue(mount.Type ?? "", out CreatureDef def))
            {
                slots += Math.Max(1, def.ControlSlots); // a ridden mount is still a follower
            }

            return slots;
        }

        private void Adopt(NpcMobile creature, AccountStore.Character character, Combatant body)
        {
            creature.MakePet(body, character.Id);
            Following(character.Id).Add(creature);
        }

        /// <summary>Server: a creature charmed into service (Dryad Allure): the caster's pet.</summary>
        public void Charm(NpcMobile creature, AccountStore.Character character, Combatant body) => Adopt(creature, character, body);

        /// <summary>Server: a pet died for good (not bonded) or was released: no longer the owner's.</summary>
        public void Lost(NpcMobile pet)
        {
            if (_following.TryGetValue(pet.OwnerCharacter, out List<NpcMobile> list))
            {
                list.Remove(pet);
            }
        }

        // ------------------------------------------------------------------ coming and going with the owner

        /// <summary>Server: a character entered the world: the pets that were following them come along.</summary>
        public void Enter(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            foreach (AccountStore.Pet record in character.Pets.ToArray())
            {
                if (record.Ridden)
                {
                    character.Pets.Remove(record);
                    Mount(character, body, record);
                }
                else if (!record.Stabled)
                {
                    Summon(record, character, body);
                }
            }

            body.Died += (self, killer) =>
            {
                if (_riding.ContainsKey(character.Id))
                {
                    Dismount(character, self); // thrown off: the mount stands beside the body
                }
            };
        }

        /// <summary>Server: a character left: remember their pets and take them out of the world with them.</summary>
        public void Leave(AccountStore.Character character)
        {
            Remember(character);
            foreach (NpcMobile pet in Following(character.Id).ToArray())
            {
                if (pet != null && pet.IsSpawned)
                {
                    _network.ServerManager.Despawn(pet.NetworkObject);
                }
            }

            _following.Remove(character.Id);
            _riding.Remove(character.Id);
        }

        /// <summary>Server: write the following pets' state onto the character (saved with it).</summary>
        public void Remember(AccountStore.Character character)
        {
            character.Pets.RemoveAll(p => !p.Stabled);
            foreach (NpcMobile pet in Following(character.Id))
            {
                if (!pet.Summoned)
                {
                    character.Pets.Add(Record(pet));
                }
            }

            if (_riding.TryGetValue(character.Id, out AccountStore.Pet mount))
            {
                mount.Ridden = true;
                mount.Stabled = false;
                character.Pets.Add(mount);
            }
        }

        private static AccountStore.Pet Record(NpcMobile pet)
        {
            var combat = pet.GetComponent<Combatant>();
            return new AccountStore.Pet
            {
                Type = pet.Def.Type,
                Seed = pet.Seed.Value,
                Name = pet.PetName.Value,
                Loyalty = pet.Loyalty.Value,
                Bonded = pet.Bonded.Value,
                Dead = combat.Dead.Value,
                BondingBegin = pet.BondingBegin == DateTime.MinValue ? null : pet.BondingBegin.ToString("o"),
                Owners = new List<int>(pet.Owners),
                SkillScale = pet.SkillScale,
                Hits = combat.Hits.Value,
                Order = pet.Order.Value,
                Pack = new List<AccountStore.Item>(pet.Pack),
            };
        }

        /// <summary>Bring a saved pet into the world beside its owner.</summary>
        private NpcMobile Summon(AccountStore.Pet record, AccountStore.Character character, Combatant body)
        {
            if (SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue(record.Type ?? string.Empty, out CreatureDef def))
            {
                return null;
            }

            Vector3 at = body.transform.position - body.transform.forward * 2f;
            NetworkObject nob = _network.GetPooledInstantiated(_mobilePrefab, at, body.transform.rotation, true);
            var pet = nob.GetComponent<NpcMobile>();
            pet.SetServer(def, at, 0f, record.Seed);
            _network.ServerManager.Spawn(nob);
            Adopt(pet, character, body);
            foreach (int owner in record.Owners)
            {
                pet.Owners.Add(owner);
            }

            pet.PetName.Value = record.Name ?? string.Empty;
            pet.Loyalty.Value = (byte)Mathf.Clamp(record.Loyalty, 0, PetRules.LoyaltyMax);
            pet.Bonded.Value = record.Bonded;
            pet.BondingBegin = DateTime.TryParse(record.BondingBegin, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime b) ? b : DateTime.MinValue;
            pet.Order.Value = record.Order == (byte)NpcMobile.PetOrder.Attack ? (byte)NpcMobile.PetOrder.Follow : record.Order;
            pet.Pack.AddRange(record.Pack ?? new List<AccountStore.Item>());
            ApplyScale(pet, record.SkillScale);
            var combat = pet.GetComponent<Combatant>();
            if (record.Hits > 0)
            {
                combat.Hits.Value = Mathf.Min(record.Hits, combat.HitsMax.Value);
            }

            if (record.Dead && record.Bonded)
            {
                combat.Hits.Value = 0;
                combat.Kill(); // still a ghost
            }

            return pet;
        }

        private static void ApplyScale(NpcMobile pet, float scale)
        {
            var combat = pet.GetComponent<Combatant>();
            float factor = scale / Mathf.Max(0.01f, pet.SkillScale);
            pet.SkillScale = scale;
            combat.SetSkills(combat.WeaponSkill * factor, combat.Tactics * factor);
        }

        // ------------------------------------------------------------------ taming

        private void Tame(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            if (_taming.ContainsKey(connection))
            {
                ItemService.Instance.Tell(connection, "You are already taming something.");
                return;
            }

            NpcMobile creature = body.Target != null ? body.Target.GetComponent<NpcMobile>() : null;
            if (creature == null)
            {
                ItemService.Instance.Tell(connection, "Target the animal you want to tame first (click it, or {CycleTarget}).");
                return;
            }

            string why = CanTame(character, body, creature);
            if (why != null)
            {
                ItemService.Instance.Tell(connection, why);
                return;
            }

            float skill = CharacterService.SkillOf(character, "AnimalTaming");
            if (skill < creature.Def.MinTameSkill && !creature.Owners.Contains(character.Id))
            {
                ItemService.Instance.Tell(connection, "You have no chance of taming this creature.");
                return;
            }

            if (_beingTamed.Contains(creature))
            {
                ItemService.Instance.Tell(connection, "Someone else is already taming this.");
                return;
            }

            if (creature.Def.CanAngerOnTame && _random.NextDouble() <= PetRules.AngerChance)
            {
                ItemService.Instance.Tell(connection, "You seem to anger the beast!");
                var c = creature.GetComponent<Combatant>();
                body.Engage(c);
                c.Engage(body);
                return;
            }

            var taming = new Taming { Creature = creature, Paralyzed = creature.GetComponent<Combatant>().Paralyzed.Value };
            _taming[connection] = taming;
            _beingTamed.Add(creature);
            ItemService.Instance.Tell(connection, "You start to tame the creature.");
            StartCoroutine(Coax(connection, character, body, taming));
        }

        /// <summary>The reasons a creature can't be tamed by this character now, or null.</summary>
        private string CanTame(AccountStore.Character character, Combatant body, NpcMobile creature)
        {
            var c = creature.GetComponent<Combatant>();
            if (!creature.Def.Tamable || creature.Def.Vendor)
            {
                return "That creature cannot be tamed.";
            }

            if (creature.IsPet)
            {
                return "That animal looks tame already.";
            }

            if (c.Dead.Value)
            {
                return "You can't tame that!";
            }

            if (Vector3.Distance(body.transform.position, creature.transform.position) > PetRules.TameReach * UOClient.WorldSpace.TileSize + 1f)
            {
                return "That is too far away.";
            }

            if (FollowerSlots(character.Id) + Math.Max(1, creature.Def.ControlSlots) > PetRules.FollowersMax)
            {
                return "You have too many followers to tame that creature.";
            }

            if (creature.Owners.Count >= PetRules.MaxOwners && !creature.Owners.Contains(character.Id))
            {
                return "This animal has had too many owners and is too upset for you to tame.";
            }

            if (creature.Def.SubdueBeforeTame && creature.Owners.Count == 0 && c.Hits.Value > c.HitsMax.Value * PetRules.SubdueTo)
            {
                return "You must subdue this creature before you can tame it!";
            }

            return null;
        }

        private IEnumerator Coax(NetworkConnection connection, AccountStore.Character character, Combatant body, Taming taming)
        {
            NpcMobile creature = taming.Creature;
            var c = creature.GetComponent<Combatant>();
            int ticks = _random.Next(PetRules.MinTicks, PetRules.MaxTicks + 1);
            int hitsAtStart = c.Hits.Value;
            bool owned = creature.Owners.Contains(character.Id);
            string failure = null;
            for (int tick = 1; tick <= ticks; tick++)
            {
                yield return new WaitForSeconds(PetRules.TickSeconds);
                if (body == null || !body.IsSpawned || creature == null || !creature.IsSpawned)
                {
                    failure = "";
                    break;
                }

                if (Vector3.Distance(body.transform.position, creature.transform.position) > PetRules.KeepReach * UOClient.WorldSpace.TileSize + 1f)
                {
                    failure = "You are too far away to continue taming.";
                }
                else if (body.Dead.Value)
                {
                    failure = "You are dead, and cannot continue taming.";
                }
                else if (!Combatant.LineOfSight(body.transform.position, creature.transform.position))
                {
                    failure = "You do not have a clear path to the animal you are taming, and must cease your attempt.";
                }
                else if (creature.IsPet)
                {
                    failure = "That animal looks tame already.";
                }
                else if (c.Hits.Value < hitsAtStart || c.Dead.Value)
                {
                    failure = "The animal is too angry to continue taming.";
                }

                if (failure != null)
                {
                    break;
                }

                taming.Paralyzed |= c.Paralyzed.Value;
                if (!owned)
                {
                    SkillService.Instance?.CheckSkill(body, "AnimalLore", PetRules.LoreMin, PetRules.LoreMax);
                }

                if (tick < ticks && PetRules.Lines.Count > 0)
                {
                    WorldServices.Instance?.SayFor(body.NetworkObject, character.Name, PetRules.Lines[_random.Next(PetRules.Lines.Count)]);
                }
            }

            _taming.Remove(connection);
            _beingTamed.Remove(creature);
            if (failure != null)
            {
                if (failure.Length > 0)
                {
                    ItemService.Instance.Tell(connection, failure);
                }

                yield break;
            }

            // UO: the check runs from 0.1 below the creature's requirement (raised 6 for every earlier owner) to 49.9 above.
            double min = creature.Def.MinTameSkill + creature.Owners.Count * PetRules.OwnerPenalty;
            bool tamed = owned || SkillService.Instance != null && SkillService.Instance.CheckSkill(body, "AnimalTaming", min - 0.1, min + 49.9);
            if (!tamed)
            {
                ItemService.Instance.Tell(connection, "You fail to tame the creature.");
                yield break;
            }

            if (FollowerSlots(character.Id) + Math.Max(1, creature.Def.ControlSlots) > PetRules.FollowersMax)
            {
                ItemService.Instance.Tell(connection, "You have too many followers to tame that creature.");
                yield break;
            }

            float scale = creature.Owners.Count == 0 ? (taming.Paralyzed ? PetRules.ScaleParalyzed : PetRules.ScaleFirst) : PetRules.ScaleRetame;
            if (!owned)
            {
                ApplyScale(creature, creature.SkillScale * scale);
            }

            Adopt(creature, character, body);
            creature.Loyalty.Value = (byte)PetRules.LoyaltyMax;
            ItemService.Instance.Tell(connection, owned ? "That wasn't even challenging." : "It seems to accept you as master.");
        }

        // ------------------------------------------------------------------ orders

        private void OnCommand(NetworkConnection connection, PetCommand command, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body))
            {
                return;
            }

            if (command.Order == 15)
            {
                if (_riding.ContainsKey(character.Id))
                {
                    Dismount(character, body);
                }

                return;
            }

            var pets = new List<NpcMobile>();
            foreach (NpcMobile p in Following(character.Id))
            {
                if (command.Pet == 0 || p.ObjectId == command.Pet)
                {
                    pets.Add(p);
                }
            }

            if (pets.Count == 0)
            {
                return;
            }

            foreach (NpcMobile pet in pets)
            {
                switch (command.Order)
                {
                    case 10:
                        if (command.Pet != 0 && pet.Summoned)
                        {
                            pet.Unsummon();
                        }
                        else if (command.Pet != 0) // releasing is one at a time, on purpose
                        {
                            Lost(pet);
                            pet.GoWild(600f);
                            ItemService.Instance.Tell(connection, $"{Display(pet)} is free.");
                        }

                        break;
                    case 11:
                        string name = (command.Name ?? string.Empty).Trim();
                        if (name.Length > 0 && name.Length <= 16)
                        {
                            pet.PetName.Value = name;
                        }

                        break;
                    case 12:
                        Feed(connection, character, pet);
                        break;
                    case 13:
                        if (PackOf(connection, character, body, pet.ObjectId) is List<AccountStore.Item> pack)
                        {
                            SendPack(connection, pet.ObjectId, pack);
                        }

                        break;
                    case 14:
                        Ride(connection, character, body, pet);
                        break;
                    default:
                        if (command.Order > 4)
                        {
                            break;
                        }

                        var c = pet.GetComponent<Combatant>();
                        if (c.Dead.Value && command.Order != (byte)NpcMobile.PetOrder.Follow && command.Order != (byte)NpcMobile.PetOrder.Stay)
                        {
                            break; // a ghost only follows or stays
                        }

                        double chance = PetRules.ControlChance(pet.Def.MinTameSkill, CharacterService.SkillOf(character, "AnimalTaming"),
                            CharacterService.SkillOf(character, "AnimalLore"), pet.Loyalty.Value);
                        if (_random.NextDouble() > chance)
                        {
                            ItemService.Instance.Tell(connection, $"{Display(pet)} doesn't obey.");
                            break;
                        }

                        pet.Command((NpcMobile.PetOrder)command.Order);
                        break;
                }
            }
        }

        private static string Display(NpcMobile pet) => pet.DisplayName;

        /// <summary>The owner feeds a pet something it likes from their backpack: loyalty back to full; bonding begins.</summary>
        private void Feed(NetworkConnection connection, AccountStore.Character character, NpcMobile pet)
        {
            AccountStore.Item food = character.Items.Find(i => i.Slot == 0 && PetRules.Eats(pet.Def, i.Type));
            if (food == null)
            {
                ItemService.Instance.Tell(connection, $"You have nothing {Display(pet)} wants to eat ({string.Join(", ", pet.Def.Foods)}).");
                return;
            }

            if (pet.GetComponent<Combatant>().Dead.Value)
            {
                return;
            }

            ItemService.Instance.Consume(character, food.Type, 1);
            bool happier = pet.Loyalty.Value < PetRules.LoyaltyMax;
            pet.Loyalty.Value = (byte)PetRules.LoyaltyMax;
            ItemService.Instance.Tell(connection, happier ? "Your pet looks happier." : $"{Display(pet)} eats.");
            if (pet.Bonded.Value)
            {
                return;
            }

            float taming = CharacterService.SkillOf(character, "AnimalTaming");
            if (pet.Def.MinTameSkill > PetRules.ControlEasy && taming < pet.Def.MinTameSkill)
            {
                ItemService.Instance.Tell(connection, "Your pet cannot form a bond with you until your animal taming ability has risen.");
                return;
            }

            if (pet.BondingBegin == DateTime.MinValue)
            {
                pet.BondingBegin = DateTime.UtcNow;
            }
            else if ((DateTime.UtcNow - pet.BondingBegin).TotalDays >= PetRules.BondDays)
            {
                pet.Bonded.Value = true;
                pet.BondingBegin = DateTime.MinValue;
                ItemService.Instance.Tell(connection, "Your pet has bonded with you!");
            }
        }

        /// <summary>Every hour, pets in the world grow less loyal; at nothing they leave.</summary>
        private void HourlyLoyalty()
        {
            foreach (KeyValuePair<int, List<NpcMobile>> owner in new List<KeyValuePair<int, List<NpcMobile>>>(_following))
            {
                foreach (NpcMobile pet in Following(owner.Key).ToArray())
                {
                    if (pet.GetComponent<Combatant>().Dead.Value || pet.Summoned)
                    {
                        continue;
                    }

                    int loyalty = Math.Max(0, pet.Loyalty.Value - PetRules.LoyaltyLossPerHour);
                    pet.Loyalty.Value = (byte)loyalty;
                    if (loyalty <= 0)
                    {
                        Lost(pet);
                        WorldServices.Instance?.SayFor(pet.NetworkObject, Display(pet), "*runs off*");
                        pet.GoWild(600f);
                    }
                    else if (loyalty < PetRules.LoyaltyWarn)
                    {
                        WorldServices.Instance?.SayFor(pet.NetworkObject, Display(pet), $"* {Display(pet)} looks around desperately *");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ summoned creatures

        /// <summary>Server: call a creature (a summoning spell) at a spot for so long; controlled ones follow the caster as pets.</summary>
        public NpcMobile Summon(CreatureDef def, Vector3 at, float seconds, bool controlled, AccountStore.Character character, Combatant body)
        {
            NetworkObject nob = _network.GetPooledInstantiated(_mobilePrefab, at, body.transform.rotation, true);
            var creature = nob.GetComponent<NpcMobile>();
            creature.SetServer(def, at, 30f, _random.Next(1, int.MaxValue));
            _network.ServerManager.Spawn(nob);
            creature.MakeSummoned(seconds);
            if (controlled)
            {
                Adopt(creature, character, body);
                creature.Loyalty.Value = (byte)PetRules.LoyaltyMax;
            }

            return creature;
        }

        // ------------------------------------------------------------------ riding

        private void Ride(NetworkConnection connection, AccountStore.Character character, Combatant body, NpcMobile pet)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (!pet.Def.Mount)
            {
                ItemService.Instance.Tell(connection, "You can't ride that.");
            }
            else if (identity != null && identity.IsGargoyle)
            {
                ItemService.Instance.Tell(connection, "Gargoyles don't ride; you have wings of your own.");
            }
            else if (body.Dead.Value || identity == null || identity.Mounted)
            {
                ItemService.Instance.Tell(connection, "You are already riding.");
            }
            else if (pet.GetComponent<Combatant>().Dead.Value)
            {
                ItemService.Instance.Tell(connection, "You can't ride a ghost.");
            }
            else if (Vector3.Distance(pet.transform.position, body.transform.position) > RideReach)
            {
                ItemService.Instance.Tell(connection, "That is too far away.");
            }
            else
            {
                AccountStore.Pet record = Record(pet);
                Lost(pet);
                _network.ServerManager.Despawn(pet.NetworkObject);
                Mount(character, body, record);
            }
        }

        /// <summary>Put a character on a mount (carried with them; their identity tells everyone what they ride).</summary>
        private void Mount(AccountStore.Character character, Combatant body, AccountStore.Pet record)
        {
            if (!SpawnService.Content.Creatures.TryGetValue(record.Type ?? "", out CreatureDef def) || body.Dead.Value)
            {
                character.Pets.Add(record); // can't ride now: keep it as a following pet next time
                record.Ridden = false;
                return;
            }

            record.Ridden = true;
            _riding[character.Id] = record;
            var identity = body.GetComponent<PlayerIdentity>();
            identity.MountKind.Value = (ushort)(def.Kind + 1);
            identity.MountSeed.Value = record.Seed;
        }

        /// <summary>Get off: the mount stands beside its rider again, following.</summary>
        public void Dismount(AccountStore.Character character, Combatant body)
        {
            if (!_riding.TryGetValue(character.Id, out AccountStore.Pet record))
            {
                return;
            }

            _riding.Remove(character.Id);
            var identity = body.GetComponent<PlayerIdentity>();
            identity.MountKind.Value = 0;
            identity.MountSeed.Value = 0;
            record.Ridden = false;
            Summon(record, character, body);
        }

        // ------------------------------------------------------------------ packs

        /// <summary>A pack animal's pack, if it's this character's, alive and within reach; else null.</summary>
        public List<AccountStore.Item> PackOf(NetworkConnection connection, AccountStore.Character character, Combatant body, int petObjectId)
        {
            NpcMobile pet = Following(character.Id).Find(p => p.ObjectId == petObjectId);
            if (pet == null || !pet.Def.PackAnimal)
            {
                return null;
            }

            if (pet.GetComponent<Combatant>().Dead.Value || Vector3.Distance(pet.transform.position, body.transform.position) > PackReach)
            {
                ItemService.Instance.Tell(connection, "That is too far away.");
                return null;
            }

            return pet.Pack;
        }

        public static int Weight(List<AccountStore.Item> items)
        {
            float w = 0f;
            foreach (AccountStore.Item i in items)
            {
                w += ItemService.WeightOf(i.Type) * Math.Max(1, i.Amount);
            }

            return Mathf.CeilToInt(w);
        }

        public void SendPack(NetworkConnection connection, int pet, List<AccountStore.Item> pack) =>
            _network.ServerManager.Broadcast(connection, new PackState
            {
                Pet = pet,
                Items = ItemService.ViewsOf(pack),
                Weight = Weight(pack),
                MaxWeight = PetRules.PackMaxWeight,
            });

        // ------------------------------------------------------------------ Animal Lore

        /// <summary>
        /// UO's Animal Lore on the current target: below 100 skill only tamed creatures; from 100 tamable ones too (a
        /// check from 80); from 110 any creature (a check from 100). Every use is also a check for gain.
        /// </summary>
        private void Lore(NetworkConnection connection, AccountStore.Character character, Combatant body)
        {
            NpcMobile creature = body.Target != null ? body.Target.GetComponent<NpcMobile>() : null;
            if (creature == null || creature.Def == null || creature.Def.HumanBody)
            {
                ItemService.Instance.Tell(connection, creature == null ? "Target the animal you want to look at first." : "That's not an animal!");
                return;
            }

            var c = creature.GetComponent<Combatant>();
            if (c.Dead.Value)
            {
                ItemService.Instance.Tell(connection, "The spirits of the dead are not the province of animal lore.");
                return;
            }

            SkillService.Instance.CheckSkill(body, "AnimalLore", 0.0, 120.0);
            float skill = CharacterService.SkillOf(character, "AnimalLore");
            bool known = creature.IsPet;
            if (!known)
            {
                if (skill < 100f)
                {
                    ItemService.Instance.Tell(connection, "At your skill level, you can only lore tamed creatures.");
                    return;
                }

                if (skill < 110f && !creature.Def.Tamable)
                {
                    ItemService.Instance.Tell(connection, "At your skill level, you can only lore tamed or tameable creatures.");
                    return;
                }

                if (!SkillService.Instance.CheckSkill(body, "AnimalLore", creature.Def.Tamable ? 80.0 : 100.0, 120.0))
                {
                    ItemService.Instance.Tell(connection, "You can't think of anything you know offhand.");
                    return;
                }
            }

            _network.ServerManager.Broadcast(connection, new LoreInfo
            {
                Creature = creature.ObjectId,
                Name = creature.DisplayName,
                Hits = c.Hits.Value,
                HitsMax = c.HitsMax.Value,
                Str = c.Str,
                Dex = c.Dex,
                Int = c.Int,
                Physical = c.Resist(0),
                Fire = c.Resist(1),
                Cold = c.Resist(2),
                Poison = c.Resist(3),
                Energy = c.Resist(4),
                Mana = c.Mana.Value,
                ManaMax = c.ManaMax.Value,
                Wrestling = c.WeaponSkill,
                Tactics = c.Tactics,
                Tame = creature.Def.Tamable ? creature.Def.MinTameSkill : -1f,
                Slots = creature.Def.ControlSlots,
                Loyalty = creature.Loyalty.Value,
                Pet = creature.IsPet,
                Foods = string.Join(", ", creature.Def.Foods),
            });
        }

        // ------------------------------------------------------------------ stables

        private void OnStable(NetworkConnection connection, StableRequest request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value ||
                !_network.ServerManager.Objects.Spawned.TryGetValue(request.Trainer, out NetworkObject nob))
            {
                return;
            }

            NpcMobile trainer = nob.GetComponent<NpcMobile>();
            if (trainer == null || trainer.Def == null || !PetRules.TrainerKinds.Contains(trainer.Def.Type) ||
                Vector3.Distance(trainer.transform.position, body.transform.position) > StableReach)
            {
                return;
            }

            int stalls = PetRules.Stalls(CharacterService.SkillOf(character, "AnimalTaming"), CharacterService.SkillOf(character, "AnimalLore"),
                CharacterService.SkillOf(character, "Veterinary"));
            switch (request.Action)
            {
                case 1:
                {
                    NpcMobile pet = Following(character.Id).Find(p => p.ObjectId == request.Pet);
                    if (pet == null)
                    {
                        break;
                    }

                    if (Vector3.Distance(pet.transform.position, trainer.transform.position) > StableReach)
                    {
                        ItemService.Instance.Tell(connection, "Bring your pet closer.");
                    }
                    else if (character.Pets.FindAll(p => p.Stabled).Count >= stalls)
                    {
                        ItemService.Instance.Tell(connection, "You have too many pets in the stables!");
                    }
                    else if (pet.GetComponent<Combatant>().Dead.Value || pet.Summoned)
                    {
                        ItemService.Instance.Tell(connection, pet.Summoned ? "I don't keep summoned creatures." : "Living pets only, please.");
                    }
                    else if (!ItemService.Pay(character, PetRules.StableFee))
                    {
                        ItemService.Instance.Tell(connection, $"Stabling costs {PetRules.StableFee} gold, which you don't have.");
                    }
                    else
                    {
                        AccountStore.Pet record = Record(pet);
                        record.Stabled = true;
                        Lost(pet);
                        _network.ServerManager.Despawn(pet.NetworkObject);
                        character.Pets.Add(record);
                        ItemService.Instance.Resend(connection, character);
                        ItemService.Instance.Tell(connection, $"Very well, thy pet is stabled. Thou mayst recover it by asking me. ({PetRules.StableFee} gold)");
                    }

                    break;
                }
                case 2:
                {
                    List<AccountStore.Pet> stabled = character.Pets.FindAll(p => p.Stabled);
                    if (request.Index < 0 || request.Index >= stabled.Count)
                    {
                        break;
                    }

                    AccountStore.Pet record = stabled[request.Index];
                    CreatureDef def = SpawnService.Content?.Creatures.TryGetValue(record.Type ?? "", out CreatureDef d) == true ? d : null;
                    if (def != null && FollowerSlots(character.Id) + Math.Max(1, def.ControlSlots) > PetRules.FollowersMax)
                    {
                        ItemService.Instance.Tell(connection, "You have too many followers to claim that pet.");
                        break;
                    }

                    character.Pets.Remove(record);
                    record.Stabled = false;
                    record.Loyalty = PetRules.LoyaltyMax; // the trainer fed it
                    if (Summon(record, character, body) != null)
                    {
                        ItemService.Instance.Tell(connection, "Here you go, and have a nice day!");
                    }

                    break;
                }
                case 3:
                {
                    List<(string Type, int Price)> sale = PetRules.ForSale.TryGetValue(trainer.Def.Type, out var list) ? list : null;
                    if (sale == null || request.Index < 0 || request.Index >= sale.Count ||
                        SpawnService.Content == null || !SpawnService.Content.Creatures.TryGetValue(sale[request.Index].Type, out CreatureDef def))
                    {
                        break;
                    }

                    if (FollowerSlots(character.Id) + Math.Max(1, def.ControlSlots) > PetRules.FollowersMax)
                    {
                        ItemService.Instance.Tell(connection, "You have too many followers to buy that.");
                    }
                    else if (!ItemService.Pay(character, sale[request.Index].Price))
                    {
                        ItemService.Instance.Tell(connection, $"That costs {sale[request.Index].Price} gold.");
                    }
                    else
                    {
                        var record = new AccountStore.Pet { Type = def.Type, Seed = _random.Next(1, int.MaxValue), Loyalty = PetRules.LoyaltyMax };
                        record.Owners.Add(character.Id);
                        Summon(record, character, body);
                        ItemService.Instance.Resend(connection, character);
                    }

                    break;
                }
            }

            SendStable(connection, character, trainer, stalls);
        }

        private void SendStable(NetworkConnection connection, AccountStore.Character character, NpcMobile trainer, int stalls)
        {
            var stabled = new List<StabledPet>();
            foreach (AccountStore.Pet p in character.Pets)
            {
                if (p.Stabled && SpawnService.Content.Creatures.TryGetValue(p.Type ?? "", out CreatureDef def))
                {
                    stabled.Add(new StabledPet { Kind = def.Kind, Seed = p.Seed, Name = p.Name ?? string.Empty });
                }
            }

            var forSale = new List<PetForSale>();
            if (PetRules.ForSale.TryGetValue(trainer.Def.Type, out var list))
            {
                foreach ((string type, int price) in list)
                {
                    forSale.Add(new PetForSale
                    {
                        Kind = SpawnService.Content.Creatures.TryGetValue(type, out CreatureDef def) ? def.Kind : (ushort)0,
                        Price = price,
                    });
                }
            }

            _network.ServerManager.Broadcast(connection, new StableState
            {
                Trainer = trainer.ObjectId,
                Stabled = stabled.ToArray(),
                Stalls = stalls,
                Fee = PetRules.StableFee,
                ForSale = forSale.ToArray(),
            });
        }
    }
}

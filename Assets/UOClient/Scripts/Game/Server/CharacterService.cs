using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Characters: a logged-in client creates one or picks one, and the server spawns it where it last stood (a new
    /// character at the start point). Where each character stands is saved when its player leaves and every minute.
    /// </summary>
    public sealed class CharacterService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private NetworkObject _playerPrefab;
        [SerializeField] private Transform _start;

        private const float SaveSeconds = 60f;

        /// <summary>Client side: the server refused a character request.</summary>
        public static event Action<string> Refused;

        private readonly Dictionary<NetworkConnection, (NetworkObject Player, AccountStore.Character Character)> _playing =
            new Dictionary<NetworkConnection, (NetworkObject, AccountStore.Character)>();

        private float _nextSave;

        public static CharacterService Instance { get; private set; }

        /// <summary>Server: everyone in the world (the admin tools and API).</summary>
        public IEnumerable<(NetworkConnection Connection, NetworkObject Player, AccountStore.Character Character)> Playing
        {
            get
            {
                foreach (KeyValuePair<NetworkConnection, (NetworkObject Player, AccountStore.Character Character)> p in _playing)
                {
                    yield return (p.Key, p.Value.Player, p.Value.Character);
                }
            }
        }

        /// <summary>Server: remember where everyone stands and save the accounts now.</summary>
        public void SaveAll()
        {
            foreach ((NetworkObject player, AccountStore.Character character) in _playing.Values)
            {
                Remember(player, character);
            }

            GameAuthenticator.Store?.Save();
        }

        private void Awake()
        {
            Instance = this;
            _network.ServerManager.RegisterBroadcast<CreateCharacter>(OnCreate);
            _network.ServerManager.RegisterBroadcast<PlayCharacter>(OnPlay);
            _network.ServerManager.OnRemoteConnectionState += OnRemoteState;
            _network.ClientManager.RegisterBroadcast<CharacterRefused>((m, c) => Refused?.Invoke(m.Reason));
        }

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.ServerManager.OnRemoteConnectionState -= OnRemoteState;
            }
        }

        private void OnCreate(NetworkConnection connection, CreateCharacter request, Channel channel)
        {
            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            if (account == null)
            {
                return;
            }

            string problem = Check(request);
            if (problem != null)
            {
                _network.ServerManager.Broadcast(connection, new CharacterRefused { Reason = problem });
                return;
            }

            AccountStore.Character character = GameAuthenticator.Store.Create(account, request.Name, out string reason);
            if (character == null)
            {
                _network.ServerManager.Broadcast(connection, new CharacterRefused { Reason = reason });
                return;
            }

            Apply(character, request);
            GameAuthenticator.Store.Save();
            Enter(connection, character);
        }

        /// <summary>Why a new character's choices aren't allowed (our creation rules), or null.</summary>
        private static string Check(CreateCharacter request)
        {
            Look look = request.Look;
            if (!Content.Outfits.HueAllowed(look.Gargoyle ? "GargoyleSkin" : "Skin", look.Skin))
            {
                return "Choose a skin colour from the list.";
            }

            if (look.Gargoyle && (look.Hair != 0 || look.Beard != 0))
            {
                return "Gargoyles have no hair or beard.";
            }

            if (look.Hair != 0 && (!Content.Outfits.HairStyles(look.Female).Contains(look.Hair) || !Content.Outfits.HueAllowed("Hair", look.HairHue)))
            {
                return "Choose a hair style and colour from the list.";
            }

            if (look.Beard != 0 && (look.Female || !Content.Outfits.BeardStyles().Contains(look.Beard) || !Content.Outfits.HueAllowed("Hair", look.BeardHue)))
            {
                return "Choose a beard and colour from the list.";
            }

            if (request.Profession != 0)
            {
                return Content.CreationRules.FindProfession(request.Profession) == null ? "Choose a profession from the list." : null;
            }

            var skills = new List<(string, int)>();
            foreach (SkillPick pick in request.Skills ?? Array.Empty<SkillPick>())
            {
                if (pick.Value > 0)
                {
                    skills.Add((pick.Skill, pick.Value));
                }
            }

            return Content.CreationRules.CheckStats(request.Str, request.Dex, request.Int) ?? Content.CreationRules.CheckSkills(skills);
        }

        /// <summary>The checked choices onto the new character (a profession brings its own stats and skills).</summary>
        private static void Apply(AccountStore.Character character, CreateCharacter request)
        {
            character.StartCity = request.City;
            Look look = request.Look;
            var seed = new System.Random(character.Id * 7919 + 17);
            character.LookSeed = seed.Next(1, int.MaxValue);
            character.Female = look.Female;
            character.Gargoyle = look.Gargoyle;
            character.Skin = look.Skin;
            character.Hair = look.Hair;
            character.HairHue = look.HairHue;
            character.Beard = look.Female ? 0 : look.Beard;
            character.BeardHue = look.Female ? 0 : look.BeardHue;
            character.Profession = request.Profession;
            character.Skills.Clear();
            Content.Profession profession = Content.CreationRules.FindProfession(request.Profession);
            if (profession != null)
            {
                (character.Str, character.Dex, character.Int) = (profession.Str, profession.Dex, profession.Int);
                foreach ((string skill, int value) in profession.Skills)
                {
                    character.Skills.Add(new AccountStore.SkillValue { Skill = skill, Value = value });
                }
            }
            else
            {
                (character.Str, character.Dex, character.Int) = (request.Str, request.Dex, request.Int);
                foreach (SkillPick pick in request.Skills ?? Array.Empty<SkillPick>())
                {
                    if (pick.Value > 0)
                    {
                        character.Skills.Add(new AccountStore.SkillValue { Skill = Content.CreationRules.Skill(pick.Skill).Key, Value = pick.Value });
                    }
                }
            }
        }

        /// <summary>A character made before stats existed gets the first profession's (a warrior's).</summary>
        private static void GiveStatsIfMissing(AccountStore.Character character)
        {
            if (character.Str > 0)
            {
                return;
            }

            Content.Profession first = Content.CreationRules.Professions.Find(p => !p.Custom);
            (character.Str, character.Dex, character.Int) = first != null ? (first.Str, first.Dex, first.Int) : (45, 35, 10);
            character.Profession = first?.Id ?? 0;
            character.Skills.Clear();
            foreach ((string skill, int value) in first?.Skills ?? new List<(string, int)>())
            {
                character.Skills.Add(new AccountStore.SkillValue { Skill = skill, Value = value });
            }
        }

        /// <summary>A character's skill value (0 if untrained).</summary>
        public static float SkillOf(AccountStore.Character character, string skill)
        {
            AccountStore.SkillValue found = character.Skills.Find(s => string.Equals(s.Skill, skill, StringComparison.OrdinalIgnoreCase));
            return Mathf.Max(0f, (found?.Value ?? 0f) + ItemSkillBonus(character, skill) + SkillMod(character.Id, skill));
        }

        /// <summary>Server: skill changes from effects (character id -> "source/skill" -> amount): Animal Form's +20
        /// Stealth, Honorable Execution's lost Magic Resist. Not saved; gone on logout.</summary>
        private static readonly Dictionary<int, Dictionary<string, float>> SkillMods = new Dictionary<int, Dictionary<string, float>>();

        public static void SetSkillMod(int characterId, string source, string skill, float amount)
        {
            if (!SkillMods.TryGetValue(characterId, out Dictionary<string, float> mods))
            {
                SkillMods[characterId] = mods = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            }

            if (amount == 0f)
            {
                mods.Remove(source + "/" + skill);
            }
            else
            {
                mods[source + "/" + skill] = amount;
            }
        }

        public static float SkillMod(int characterId, string skill)
        {
            if (!SkillMods.TryGetValue(characterId, out Dictionary<string, float> mods) || mods.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            foreach (KeyValuePair<string, float> m in mods)
            {
                if (m.Key.EndsWith("/" + skill, StringComparison.OrdinalIgnoreCase))
                {
                    sum += m.Value;
                }
            }

            return sum;
        }

        /// <summary>Worn jewellery's skill bonuses (+N Magery...).</summary>
        public static int ItemSkillBonus(AccountStore.Character character, string skill)
        {
            int id = Items.MagicProps.Id(skill);
            if (id < 0)
            {
                return 0;
            }

            int bonus = 0;
            foreach (AccountStore.Item item in character.Items)
            {
                if (item.Slot != 0 && item.Props != null && item.Props.Count > 0)
                {
                    bonus += Items.MagicProps.Get(item.Props, id);
                }
            }

            return bonus;
        }

        private void OnPlay(NetworkConnection connection, PlayCharacter request, Channel channel)
        {
            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            AccountStore.Character character = account?.Characters.Find(c => c.Id == request.Id);
            if (character == null)
            {
                _network.ServerManager.Broadcast(connection, new CharacterRefused { Reason = "No such character." });
                return;
            }

            Enter(connection, character);
        }

        private void Enter(NetworkConnection connection, AccountStore.Character character)
        {
            if (_playing.ContainsKey(connection))
            {
                _network.ServerManager.Broadcast(connection, new CharacterRefused { Reason = "Already playing." });
                return;
            }

            foreach ((NetworkObject _, AccountStore.Character c) in _playing.Values)
            {
                if (c.Id == character.Id)
                {
                    _network.ServerManager.Broadcast(connection, new CharacterRefused { Reason = "That character is already in the world." });
                    return;
                }
            }

            Vector3 position = character.Placed ? character.Position : _start.position;
            if (!character.Placed && Content.CreationRules.StartCities.Count > 0)
            {
                // A new character: in the city they chose (Creation.json StartCities).
                Content.CreationRules.StartCity city = Content.CreationRules.StartCities[character.StartCity >= 0 && character.StartCity < Content.CreationRules.StartCities.Count
                    ? character.StartCity : Content.CreationRules.DefaultCity];
                position = UOClient.WorldSpace.TileCenter(city.X, city.Y, city.Z) + Vector3.up * 0.5f;
            }
            Quaternion rotation = Quaternion.Euler(0f, character.Yaw, 0f);
            NetworkObject player = _network.GetPooledInstantiated(_playerPrefab, position, rotation, true);
            player.GetComponent<PlayerIdentity>().SetServer(character.Id, character.Name);
            // A new adventurer's stats for now (skills and stats arrive with the migration and the skill system):
            // UO's starting 50 strength gives 75 hits; bare hands do 2-8.
            var combat = player.GetComponent<UOGame.Combat.Combatant>();
            // UO's rules: hits are half strength plus 50; the weapon and its skill are set once gear is applied.
            GiveStatsIfMissing(character);
            combat.SetServer(UOGame.Combat.Combatant.Side.Player, character.Str, character.Dex, 50 + character.Str / 2,
                SkillOf(character, "Wrestling"), SkillOf(character, "Tactics"), new Vector2(1f, 8f), 2.2f);
            combat.SetPools(character.Int, character.Int, character.Dex); // UO: mana is intelligence, stamina dexterity
            combat.Died += (self, killer) =>
                DeathService.Instance?.LeavePlayerCorpse(self.transform, character.Name, player.GetComponent<PlayerIdentity>().Look.Value);
            _network.ServerManager.Spawn(player, connection);
            ItemService.Instance?.Enter(connection, character, combat);
            SkillService.Instance?.Enter(connection, character, combat);
            PetService.Instance?.Enter(connection, character, combat);
            _playing[connection] = (player, character);
            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            if (account != null && account.LastPlayed != character.Id)
            {
                account.LastPlayed = character.Id;
                GameAuthenticator.Store?.Save();
            }

            AdminService.Instance?.Entered(connection);
            Debug.Log($"[Game] {character.Name} entered the world at {position}");
        }

        private void OnRemoteState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped || !_playing.TryGetValue(connection, out var entry))
            {
                return;
            }

            Remember(entry.Player, entry.Character);
            PetService.Instance?.Leave(entry.Character);
            if (entry.Player != null)
            {
                SkillService.Instance?.Leave(entry.Player.GetComponent<UOGame.Combat.Combatant>());
            }
            _playing.Remove(connection);
            GameAuthenticator.Store?.Save();
        }

        private void Update()
        {
            if (!_network.IsServerStarted || Time.unscaledTime < _nextSave)
            {
                return;
            }

            _nextSave = Time.unscaledTime + SaveSeconds;
            if (_playing.Count == 0)
            {
                return;
            }

            foreach ((NetworkObject player, AccountStore.Character character) in _playing.Values)
            {
                Remember(player, character);
            }

            GameAuthenticator.Store?.Save();
        }

        private void OnApplicationQuit()
        {
            foreach ((NetworkObject player, AccountStore.Character character) in _playing.Values)
            {
                Remember(player, character);
            }

            GameAuthenticator.Store?.Save();
        }

        private static void Remember(NetworkObject player, AccountStore.Character character)
        {
            PetService.Instance?.Remember(character);
            if (player == null)
            {
                return;
            }

            character.Position = player.transform.position;
            character.Yaw = player.transform.eulerAngles.y;
            character.Placed = true;
        }
    }
}

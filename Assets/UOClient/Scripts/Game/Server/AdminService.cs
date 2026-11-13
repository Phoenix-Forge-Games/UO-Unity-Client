using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Admin tools (Docs/AdminTools.md). An admin account is a player account with admin access: its characters play
    /// like anyone's, and switch to admin mode (unseen by players, never targeted or fought, unharmed) to do admin work
    /// from the Admin window, which opens from the skill book (and can go on the hotbar). The same work can be done by
    /// AI agents through the admin API (AdminService.Api.cs, loopback only, with a token) and its MCP server.
    /// <para>
    /// Content changes go live without a restart (LiveContent): this service loads the server's kept changes when the
    /// server starts and sends changes to clients as they happen and as they log in.
    /// </para>
    /// Every admin action is written to the server log ("[Game] Admin ...").
    /// </summary>
    public sealed partial class AdminService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [Tooltip("The admin API's port on 127.0.0.1 (0: no API).")]
        [SerializeField] private int _apiPort = 7771;

        public static AdminService Instance { get; private set; }

        /// <summary>Client side: this account is an admin, and its player is in admin mode.</summary>
        public static bool IsAdmin { get; private set; }
        public static bool Mode { get; private set; }
        public static event Action StateChanged;
        public static event Action<AdminReply> Replied;

        /// <summary>The Admin window's tools (AdminRequest.Tool). Those that act on someone act on your current target.</summary>
        public enum Tool : byte
        {
            Mode, Players, GoTo, Bring, Kick, Teleport, Spawn, Create, Kill, Heal, Remove, Broadcast, Save,
        }

        private const float SettleSeconds = 30f, DropHeight = 300f;
        private readonly System.Random _random = new System.Random();
        /// <summary>Server: people moved onto ground that wasn't loaded yet, put on it once it is.</summary>
        private readonly List<(Combatant Body, float Until)> _settling = new List<(Combatant, float)>();
        private bool _serverUp;
        private float _upSince;

        private void Awake()
        {
            Instance = this;
            _network.ServerManager.RegisterBroadcast<AdminRequest>(OnRequest);
            _network.ClientManager.RegisterBroadcast<AdminState>(OnState);
            _network.ClientManager.RegisterBroadcast<AdminReply>((m, c) => Replied?.Invoke(m));
            _network.ClientManager.RegisterBroadcast<ContentPatch>(OnPatch);
            _network.ServerManager.OnServerConnectionState += OnServerState;
            _network.ClientManager.OnClientConnectionState += OnClientState;
            LiveContent.Changed += OnContentChanged;
        }

        private void OnDestroy()
        {
            LiveContent.Changed -= OnContentChanged;
            if (_network != null)
            {
                _network.ServerManager.OnServerConnectionState -= OnServerState;
                _network.ClientManager.OnClientConnectionState -= OnClientState;
            }

            StopApi();
        }

        private void OnApplicationQuit() => StopApi();

        private void OnServerState(ServerConnectionStateArgs args)
        {
            // Once per run: the server's transport can report starting more than once while it binds. The API stays up
            // until the game closes (OnDestroy).
            if (args.ConnectionState == LocalConnectionState.Started && !_serverUp)
            {
                _serverUp = true;
                _upSince = Time.realtimeSinceStartup;
                GameAuthenticator.OpenStore(); // the API lists and changes accounts before anyone logs in
                LiveContent.LoadSaved();
                StartApi();
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
            {
                return;
            }

            IsAdmin = Mode = false;
            StateChanged?.Invoke();
            if (!_network.IsServerStarted)
            {
                LiveContent.RevertAll(); // that server's changes; the next one sends its own
            }
        }

        private void OnState(AdminState state, Channel channel)
        {
            IsAdmin = state.IsAdmin;
            Mode = state.Mode;
            StateChanged?.Invoke();
        }

        private void OnPatch(ContentPatch patch, Channel channel)
        {
            if (!_network.IsServerStarted)
            {
                LiveContent.Receive(patch); // a host already runs the server's content
            }
        }

        // ------------------------------------------------------------------ server: live content to clients

        private void OnContentChanged(string name)
        {
            if (_network == null || !_network.IsServerStarted)
            {
                return;
            }

            foreach (ContentPatch patch in LiveContent.Patches(name))
            {
                _network.ServerManager.Broadcast(patch);
            }
        }

        /// <summary>Server: a client logged in: it gets the server's changed content first.</summary>
        public void LoggedIn(NetworkConnection connection)
        {
            foreach (string name in LiveContent.ChangedNames())
            {
                foreach (ContentPatch patch in LiveContent.Patches(name))
                {
                    _network.ServerManager.Broadcast(connection, patch, false);
                }
            }
        }

        /// <summary>Server: a character entered the world: an admin's client hears it's an admin.</summary>
        public void Entered(NetworkConnection connection)
        {
            if (GameAuthenticator.AccountOf(connection)?.Admin == true)
            {
                _network.ServerManager.Broadcast(connection, new AdminState { IsAdmin = true, Mode = false });
            }
        }

        // ------------------------------------------------------------------ server: who's playing

        private struct Player
        {
            public NetworkConnection Connection;
            public NetworkObject Object;
            public AccountStore.Character Character;
            public Combatant Body;
            public bool Valid => Body != null;
        }

        private static IEnumerable<Player> Players()
        {
            if (CharacterService.Instance == null)
            {
                yield break;
            }

            foreach ((NetworkConnection connection, NetworkObject player, AccountStore.Character character) in CharacterService.Instance.Playing)
            {
                if (player != null)
                {
                    yield return new Player { Connection = connection, Object = player, Character = character, Body = player.GetComponent<Combatant>() };
                }
            }
        }

        private static Player PlayerOf(NetworkConnection connection)
        {
            foreach (Player p in Players())
            {
                if (p.Connection == connection)
                {
                    return p;
                }
            }

            return default;
        }

        /// <summary>A player in the world by character name or character id.</summary>
        private static Player FindPlayer(string who)
        {
            if (string.IsNullOrWhiteSpace(who))
            {
                return default;
            }

            who = who.Trim();
            int.TryParse(who, out int id);
            foreach (Player p in Players())
            {
                if (p.Character.Id == id || string.Equals(p.Character.Name, who, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }

            return default;
        }

        private static JArray PlayersJson()
        {
            var list = new JArray();
            foreach (Player p in Players())
            {
                Vector2Int tile = WorldSpace.ToTile(p.Object.transform.position);
                AccountStore.Account account = GameAuthenticator.AccountOf(p.Connection);
                list.Add(new JObject
                {
                    ["Id"] = p.Character.Id,
                    ["Name"] = p.Character.Name,
                    ["Account"] = account?.Name,
                    ["Admin"] = account?.Admin == true,
                    ["AdminMode"] = p.Body.AdminMode.Value,
                    ["X"] = tile.x,
                    ["Y"] = tile.y,
                    ["Height"] = Math.Round(p.Object.transform.position.y, 1),
                    ["Hits"] = p.Body.Hits.Value,
                    ["HitsMax"] = p.Body.HitsMax.Value,
                    ["Dead"] = p.Body.Dead.Value,
                    ["Address"] = p.Connection.GetAddress(),
                });
            }

            return list;
        }

        // ------------------------------------------------------------------ server: the Admin window's tools

        private void OnRequest(NetworkConnection connection, AdminRequest request, Channel channel)
        {
            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            if (account == null || !account.Admin)
            {
                Debug.LogWarning($"[Game] Admin tool refused: {account?.Name ?? connection.ClientId.ToString()} isn't an admin.");
                return;
            }

            Player me = PlayerOf(connection);
            if (!me.Valid)
            {
                return;
            }

            var tool = (Tool)request.Tool;
            Debug.Log($"[Game] Admin {account.Name} ({me.Character.Name}): {tool} '{request.Text}' {request.Number}");
            string answer = Run(me, tool, request.Text ?? "", request.Number);
            if (answer != null)
            {
                _network.ServerManager.Broadcast(connection, new AdminReply { Kind = 0, Text = answer });
            }
        }

        private string Run(Player me, Tool tool, string text, int number)
        {
            Combatant target = me.Body.Target;
            switch (tool)
            {
                case Tool.Mode:
                    me.Body.SetAdminMode(!me.Body.AdminMode.Value);
                    _network.ServerManager.Broadcast(me.Connection, new AdminState { IsAdmin = true, Mode = me.Body.AdminMode.Value });
                    return me.Body.AdminMode.Value ? "Admin mode: players can't see you, nothing can target or hurt you." : "Admin mode off: you're playing again.";

                case Tool.Players:
                    _network.ServerManager.Broadcast(me.Connection, new AdminReply { Kind = 1, Text = PlayersJson().ToString(Newtonsoft.Json.Formatting.None) });
                    return null;

                case Tool.GoTo:
                {
                    Player other = FindPlayer(number.ToString());
                    if (!other.Valid)
                    {
                        return "They're not in the world.";
                    }

                    Move(me.Body, other.Object.transform.position - other.Object.transform.forward * 2f, false);
                    return $"You go to {other.Character.Name}.";
                }

                case Tool.Bring:
                {
                    Player other = FindPlayer(number.ToString());
                    if (!other.Valid)
                    {
                        return "They're not in the world.";
                    }

                    Move(other.Body, me.Object.transform.position + me.Object.transform.forward * 2f, false);
                    ItemService.Instance?.Tell(other.Connection, "You are summoned.");
                    return $"{other.Character.Name} is brought to you.";
                }

                case Tool.Kick:
                    return Kick(FindPlayer(number.ToString()), text);

                case Tool.Teleport:
                    return Teleport(me, text);

                case Tool.Spawn:
                    return Spawn(text, number, me.Object.transform.position + me.Object.transform.forward * 4f);

                case Tool.Create:
                    return Give(me, text, number);

                case Tool.Kill:
                    if (target == null || target == me.Body)
                    {
                        return "Choose a target first.";
                    }

                    target.Kill();
                    return $"{NameOf(target)} is slain.";

                case Tool.Heal:
                    Heal(target ?? me.Body);
                    return $"{NameOf(target ?? me.Body)} is restored.";

                case Tool.Remove:
                    return Remove(target);

                case Tool.Broadcast:
                    return Announce(text);

                case Tool.Save:
                    return SaveWorld();

                default:
                    return "No such tool.";
            }
        }

        private static string NameOf(Combatant c) =>
            c == null ? "nobody" : c.GetComponent<PlayerIdentity>()?.Name.Value ?? c.GetComponent<NpcMobile>()?.Name ?? c.name;

        // ------------------------------------------------------------------ server: the work itself (window and API)

        /// <summary>Move someone (pets following come along). Ground not loaded yet: dropped from high above once it is.</summary>
        /// <summary>
        /// Server: put someone who fell out of the world back (PlayerMotor's safety net): exactly here, or on the ground
        /// at this spot (waiting for it to load if it hasn't).
        /// </summary>
        public void Rescue(Combatant body, Vector3 to, bool exact) => Move(body, to, findGround: !exact);

        private void Move(Combatant body, Vector3 to, bool findGround)
        {
            body.GetComponent<PlayerMotor>()?.LandSafely();
            if (findGround)
            {
                if (GameWorld.IsReadyAt(to) && Ground(to, out Vector3 ground))
                {
                    to = ground;
                }
                else
                {
                    to.y = DropHeight; // waits there (no gravity until the ground loads), then put on it
                    _settling.RemoveAll(s => s.Body == body);
                    _settling.Add((body, Time.time + SettleSeconds));
                }
            }

            SpellService.MoveBody(body, to);
            int id = body.GetComponent<PlayerIdentity>()?.CharacterId ?? 0;
            if (id > 0 && PetService.Instance != null)
            {
                foreach (NpcMobile pet in PetService.Instance.Following(id))
                {
                    pet.PlaceNear(to - body.transform.forward * 2f);
                }
            }
        }

        /// <summary>The top walkable surface at this spot (cast down from high above).</summary>
        private static bool Ground(Vector3 at, out Vector3 ground)
        {
            if (Physics.Raycast(new Vector3(at.x, DropHeight + 50f, at.z), Vector3.down, out RaycastHit hit, DropHeight + 400f, ~0, QueryTriggerInteraction.Ignore))
            {
                ground = hit.point + Vector3.up * 0.1f;
                return true;
            }

            ground = at;
            return false;
        }

        private void Update()
        {
            DrainApi();
            UpdateShutdown();
            for (int i = _settling.Count - 1; i >= 0; i--)
            {
                (Combatant body, float until) = _settling[i];
                if (body == null || Time.time > until)
                {
                    _settling.RemoveAt(i);
                    continue;
                }

                if (GameWorld.IsReadyAt(body.transform.position) && Ground(body.transform.position, out Vector3 ground))
                {
                    body.GetComponent<PlayerMotor>()?.LandSafely();
                    SpellService.MoveBody(body, ground);
                    _settling.RemoveAt(i);
                }
            }
        }

        /// <summary>Where a place name leads: "x y" or "x y z" (tiles), a starting city's name, or a player's name.</summary>
        private static bool Place(string text, out Vector3 to, out bool findGround)
        {
            to = default;
            findGround = false;
            string[] parts = (text ?? "").Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
            {
                bool hasZ = parts.Length >= 3 && int.TryParse(parts[2], out int _);
                to = WorldSpace.TileCenter(x, y, hasZ ? int.Parse(parts[2]) : 0) + Vector3.up * 0.5f;
                findGround = !hasZ;
                return true;
            }

            string name = (text ?? "").Trim();
            if (name.Length == 0)
            {
                return false;
            }

            foreach (CreationRules.StartCity city in CreationRules.StartCities)
            {
                if (city.Name != null && city.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    to = WorldSpace.TileCenter(city.X, city.Y, city.Z) + Vector3.up * 0.5f;
                    findGround = true;
                    return true;
                }
            }

            Player p = FindPlayer(name);
            if (p.Valid)
            {
                to = p.Object.transform.position - p.Object.transform.forward * 2f;
                return true;
            }

            return false;
        }

        private string Teleport(Player who, string place)
        {
            if (!Place(place, out Vector3 to, out bool findGround))
            {
                return $"No such place: '{place}'. Give tile coordinates (x y, or x y z), a starting city or a player.";
            }

            Move(who.Body, to, findGround);
            Vector2Int tile = WorldSpace.ToTile(to);
            return $"{who.Character.Name} goes to {tile.x} {tile.y}.";
        }

        /// <summary>A creature kind by its name (exact, or the first that contains it).</summary>
        public static CreatureDef FindCreature(string name)
        {
            GameContent content = SpawnService.Content;
            if (content == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            name = name.Trim().Replace(" ", "");
            if (content.Creatures.TryGetValue(name, out CreatureDef def))
            {
                return def;
            }

            foreach (CreatureDef d in content.Kinds)
            {
                if (d.Type.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return d;
                }
            }

            return null;
        }

        /// <summary>An item kind by its name (exact, or the first that contains it).</summary>
        public static Items.ItemDef FindItem(string name)
        {
            Items.ItemCatalog catalog = ItemService.Catalog;
            if (catalog == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            name = name.Trim().Replace(" ", "");
            Items.ItemDef def = catalog.Find(name);
            if (def != null)
            {
                return def;
            }

            foreach (Items.ItemDef d in catalog.Kinds)
            {
                if (d.Type.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return d;
                }
            }

            return null;
        }

        private string Spawn(string creature, int count, Vector3 near)
        {
            CreatureDef def = FindCreature(creature);
            if (def == null || SpawnService.Instance == null)
            {
                return $"No creature called '{creature}'.";
            }

            count = Mathf.Clamp(count, 1, 50);
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = near + new Vector3((float)_random.NextDouble() * 4f - 2f, 0f, (float)_random.NextDouble() * 4f - 2f);
                if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                {
                    at = hit.point + Vector3.up * 0.05f;
                }

                SpawnService.Instance.SpawnAt(def, at);
                made++;
            }

            Debug.Log($"[Game] Admin spawned {made} {def.Type} at {WorldSpace.ToTile(near)}");
            return $"Spawned {made} {def.Type}.";
        }

        private static string Give(Player who, string item, int amount)
        {
            Items.ItemDef def = FindItem(item);
            if (def == null)
            {
                return $"No item called '{item}'.";
            }

            amount = Mathf.Clamp(amount, 1, 60000);
            if (!ItemService.GiveToBackpack(who.Character, def.Type, amount))
            {
                return $"{who.Character.Name}'s backpack is full.";
            }

            ItemService.Instance?.Resend(who.Connection, who.Character);
            return $"{amount} {def.Type} into {who.Character.Name}'s backpack.";
        }

        private static void Heal(Combatant c)
        {
            if (c.Dead.Value)
            {
                c.Revive();
                return;
            }

            c.Hits.Value = c.HitsMax.Value;
            c.Mana.Value = c.ManaMax.Value;
            c.Stam.Value = c.StamMax.Value;
            c.CurePoison();
            c.BreakParalysis();
        }

        private string Remove(Combatant target)
        {
            NpcMobile npc = target != null ? target.GetComponent<NpcMobile>() : null;
            if (npc == null)
            {
                return "Target a creature first (players can be kicked, not removed).";
            }

            if (npc.IsPet)
            {
                return "That's someone's pet: it stays.";
            }

            string name = npc.Name;
            _network.ServerManager.Despawn(npc.NetworkObject);
            return $"{name} is removed.";
        }

        private string Kick(Player who, string reason)
        {
            if (!who.Valid)
            {
                return "They're not in the world.";
            }

            ItemService.Instance?.Tell(who.Connection, string.IsNullOrWhiteSpace(reason) ? "An admin has disconnected you." : $"An admin has disconnected you: {reason}");
            who.Connection.Disconnect(false);
            Debug.Log($"[Game] Admin kicked {who.Character.Name}{(string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason)}");
            return $"{who.Character.Name} is disconnected.";
        }

        private string Announce(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Say something to announce.";
            }

            _network.ServerManager.Broadcast(new GameNotice { Text = $"Announcement: {text.Trim()}" });
            Debug.Log($"[Game] Announcement: {text.Trim()}");
            return null;
        }

        private static string SaveWorld()
        {
            CharacterService.Instance?.SaveAll();
            HousingService.Instance?.SaveNow();
            BoatService.Instance?.SaveNow();
            Debug.Log("[Game] Saved (admin).");
            return "The world is saved.";
        }

        /// <summary>Server: grant or take away admin access (the API). Someone losing it mid-work leaves admin mode.</summary>
        private string SetAdmin(string accountName, bool admin)
        {
            AccountStore.Account account = GameAuthenticator.Store?.Find(accountName);
            if (account == null)
            {
                return null;
            }

            account.Admin = admin;
            GameAuthenticator.Store.Save();
            foreach (Player p in Players())
            {
                if (GameAuthenticator.AccountOf(p.Connection) == account)
                {
                    if (!admin && p.Body.AdminMode.Value)
                    {
                        p.Body.SetAdminMode(false);
                    }

                    _network.ServerManager.Broadcast(p.Connection, new AdminState { IsAdmin = admin, Mode = p.Body.AdminMode.Value });
                }
            }

            Debug.Log($"[Game] Admin: account {account.Name} {(admin ? "is now an admin" : "is no longer an admin")}.");
            return account.Name;
        }

        /// <summary>Server: set a skill (0 to 120), shown to its player at once.</summary>
        private static string SetSkill(Player who, string skill, float value)
        {
            SkillDef def = CreationRules.Skills.Find(s => string.Equals(s.Key, skill, StringComparison.OrdinalIgnoreCase) ||
                                                          string.Equals(s.Name, skill, StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                return null;
            }

            value = Mathf.Clamp((float)Math.Round(value, 1), 0f, 120f);
            AccountStore.SkillValue entry = who.Character.Skills.Find(s => string.Equals(s.Skill, def.Key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                who.Character.Skills.Add(entry = new AccountStore.SkillValue { Skill = def.Key });
            }

            entry.Value = value;
            SkillService.Instance?.MarkDirty(who.Body);
            return $"{who.Character.Name}'s {def.Name} is {value:0.0}.";
        }
    }
}

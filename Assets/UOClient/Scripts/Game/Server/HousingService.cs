using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UOClient;
using UOClient.Buildings.Synty;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// Housing (Docs/BuildingDesign.md): a plot deed claims a square of free land in front of you; on it the owner builds
    /// a house piece by piece, paying each piece's materials from the backpack and getting them back when it comes down.
    /// The rules are data (Housing.json, Regions.json, see <see cref="HousingRules"/>). Houses are saved in
    /// ServerData/houses.json and stand as <see cref="HousePlot"/> objects. A house nobody who may build or visit in it
    /// has stood on for DecayDays falls down.
    /// </summary>
    public sealed class HousingService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _network;
        [SerializeField] private TextAsset _housing;
        [SerializeField] private TextAsset _regions;
        [SerializeField] private NetworkObject _plotPrefab;
        /// <summary>The kit prefabs and style materials the pieces use (so they're in player builds).</summary>
        [SerializeField] private SyntyAssetRegistry _kit;

        [Serializable]
        private sealed class PieceRecord
        {
            public int Uid;
            public string Piece;
            public int X, Y, Level, Rot;
        }

        [Serializable]
        private sealed class HouseRecord
        {
            public int Id;
            public string Account, Owner, Name, Tier;
            public int Facet = 1, X, Y, Size;
            public float BaseY;
            public int WallStyle, RoofStyle;
            public bool Public;
            public List<string> Friends = new List<string>();
            public List<PieceRecord> Pieces = new List<PieceRecord>();
            public int NextUid = 1;
            public string Placed, LastVisit;
        }

        [Serializable]
        private sealed class HouseFile
        {
            public int NextId = 1;
            public List<HouseRecord> Houses = new List<HouseRecord>();
        }

        public static HousingService Instance { get; private set; }

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>(StringComparer.Ordinal);

        private HouseFile _data = new HouseFile();
        private string _path;
        private readonly Dictionary<int, HousePlot> _plots = new Dictionary<int, HousePlot>();
        private bool _spawned, _dirty;
        private float _saveAt, _nextVisit, _nextDecay;

        private void Awake()
        {
            Instance = this;
            HousingRules.Load(_housing, _regions);
            Protocol.AddDataFile(_housing);
            Protocol.AddDataFile(_regions);
            _kit = global::UOClient.Content.SecureArt.Resolve(_kit, global::UOClient.Content.SecureArtAddresses.HouseKit); // paid art by address when the scene holds none (Docs/SecureArt.md)
            Prefabs.Clear();
            Materials.Clear();
            if (_kit != null)
            {
                foreach (GameObject p in _kit.Prefabs)
                {
                    if (p != null)
                    {
                        Prefabs[p.name] = p;
                    }
                }

                foreach (Material m in _kit.Materials)
                {
                    if (m != null)
                    {
                        Materials[m.name] = m;
                    }
                }
            }

            _network.ServerManager.RegisterBroadcast<ClaimPlot>(OnClaim);
            _network.ServerManager.RegisterBroadcast<PlacePiece>(OnPlace);
            _network.ServerManager.RegisterBroadcast<RemovePiece>(OnRemove);
            _network.ServerManager.RegisterBroadcast<HouseSettings>(OnSettings);
            _network.ServerManager.RegisterBroadcast<HouseFriend>(OnFriend);
            _network.ServerManager.RegisterBroadcast<Redeed>(OnRedeed);
            WorldServices.DoorAllowed = DoorAllowed;
        }

        /// <summary>A kit prefab by name (the registry; in the editor, the project as a fallback).</summary>
        public static GameObject Prefab(string name) =>
            name != null && Prefabs.TryGetValue(name, out GameObject p) ? p : SyntyAssets.Prefab(name);

        public static Material Material(string name) =>
            name != null && Materials.TryGetValue(name, out Material m) ? m : SyntyAssets.Material(name);

        private void Update()
        {
            if (!_network.IsServerStarted)
            {
                return;
            }

            if (!_spawned)
            {
                _spawned = true;
                Load();
            }

            if (Time.time >= _nextVisit)
            {
                _nextVisit = Time.time + 30f;
                MarkVisits();
            }

            if (Time.time >= _nextDecay)
            {
                _nextDecay = Time.time + 3600f;
                Decay();
            }

            if (_dirty && Time.time >= _saveAt)
            {
                Save();
            }
        }

        /// <summary>Server: save now (the admin Save tool, and before a planned shutdown).</summary>
        public void SaveNow() => OnApplicationQuit();

        private void OnApplicationQuit()
        {
            if (_dirty)
            {
                Save();
            }
        }

        // ------------------------------------------------------------------ saving

        private void Load()
        {
            _path = Path.Combine(AccountStore.DefaultFolder, "houses.json");
            if (File.Exists(_path))
            {
                _data = JsonUtility.FromJson<HouseFile>(File.ReadAllText(_path)) ?? new HouseFile();
            }

            foreach (HouseRecord house in _data.Houses)
            {
                Spawn(house);
            }

            Debug.Log($"[Game] Houses: {_data.Houses.Count}");
        }

        private void Save()
        {
            _dirty = false;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(_data, true));
            if (File.Exists(_path))
            {
                File.Replace(temp, _path, _path + ".bak");
            }
            else
            {
                File.Move(temp, _path);
            }
        }

        private void Changed()
        {
            if (!_dirty)
            {
                _saveAt = Time.time + 5f;
            }

            _dirty = true;
        }

        private static string Now() => DateTime.UtcNow.ToString("o");

        // ------------------------------------------------------------------ the plot objects

        private void Spawn(HouseRecord house)
        {
            if (_plotPrefab == null)
            {
                Debug.LogWarning("[Game] Housing: no plot prefab");
                return;
            }

            float half = house.Size * WorldSpace.TileSize * 0.5f;
            Vector3 centre = WorldSpace.Corner(house.X, house.Y, 0f) + new Vector3(half, 0f, -half);
            centre.y = house.BaseY;
            NetworkObject obj = Instantiate(_plotPrefab, centre, Quaternion.identity);
            HousePlot plot = obj.GetComponent<HousePlot>();
            plot.HouseId.Value = house.Id;
            plot.OriginX.Value = house.X;
            plot.OriginY.Value = house.Y;
            plot.Size.Value = house.Size;
            plot.BaseY.Value = house.BaseY;
            plot.OwnerName.Value = house.Owner;
            plot.HouseName.Value = house.Name;
            plot.WallStyle.Value = (byte)house.WallStyle;
            plot.RoofStyle.Value = (byte)house.RoofStyle;
            plot.Public.Value = house.Public;
            foreach (string friend in house.Friends)
            {
                plot.Friends.Add(friend);
            }

            foreach (PieceRecord p in house.Pieces)
            {
                HousingRules.PieceDef def = HousingRules.Pieces.Find(d => d.Id == p.Piece);
                if (def != null)
                {
                    plot.Pieces.Add(new HousePiece { Uid = (ushort)p.Uid, Piece = (ushort)def.Index, X = (byte)p.X, Y = (byte)p.Y, Level = (byte)p.Level, Rot = (byte)p.Rot });
                }
            }

            _network.ServerManager.Spawn(obj);
            _plots[house.Id] = plot;
        }

        private void Despawn(HouseRecord house)
        {
            if (_plots.TryGetValue(house.Id, out HousePlot plot) && plot != null)
            {
                _network.ServerManager.Despawn(plot.NetworkObject);
            }

            _plots.Remove(house.Id);
        }

        // ------------------------------------------------------------------ claiming

        private void OnClaim(NetworkConnection connection, ClaimPlot request, Channel channel)
        {
            if (!ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) || body.Dead.Value)
            {
                return;
            }

            AccountStore.Item deed = character.Items.Find(i => i.Id == request.Deed && i.Slot == 0);
            HousingRules.PlotTier tier = deed != null ? HousingRules.TierForDeed(deed.Type) : null;
            if (tier == null)
            {
                return;
            }

            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            if (account == null)
            {
                return;
            }

            if (_data.Houses.FindAll(h => h.Account == account.Name).Count >= HousingRules.AccountLimit)
            {
                ItemService.Instance.Tell(connection, HousingRules.AccountLimit == 1 ? "You already own a house: an account may have only one." : $"An account may have only {HousingRules.AccountLimit} houses.");
                return;
            }

            string why = CheckClaim(character.Facet, request.X, request.Y, tier.Size, body.transform.position, out float baseY);
            if (why != null)
            {
                ItemService.Instance.Tell(connection, why);
                return;
            }

            if (!ItemService.Instance.Consume(character, deed.Type, 1))
            {
                return;
            }

            var house = new HouseRecord
            {
                Id = _data.NextId++,
                Account = account.Name,
                Owner = character.Name,
                Name = $"{character.Name}'s house",
                Tier = tier.Name,
                Facet = character.Facet,
                X = request.X,
                Y = request.Y,
                Size = tier.Size,
                BaseY = baseY,
                Placed = Now(),
                LastVisit = Now(),
            };
            _data.Houses.Add(house);
            Spawn(house);
            Save();
            ItemService.Instance.Tell(connection, "The land is yours. Press {BuildMode} to build.");
            Debug.Log($"[Game] {character.Name} claimed a {tier.Name} plot at {house.X},{house.Y} (house {house.Id})");
        }

        /// <summary>The claim checks: near you, the land rules, the ground's rise. Null when fine; baseY is where the house stands.</summary>
        public static string CheckClaim(int facet, int x, int y, int size, Vector3 claimant, out float baseY)
        {
            baseY = 0f;
            Vector2Int at = WorldSpace.ToTile(claimant);
            int dx = Math.Max(0, Math.Max(x - at.x, at.x - (x + size - 1)));
            int dy = Math.Max(0, Math.Max(y - at.y, at.y - (y + size - 1)));
            if (Math.Max(dx, dy) > HousingRules.ClaimReach)
            {
                return "That's too far away.";
            }

            if (dx == 0 && dy == 0)
            {
                return "Step off the land you want to claim.";
            }

            var others = new List<RectInt>();
            foreach (HousePlot plot in HousePlot.All)
            {
                if (plot != null)
                {
                    others.Add(plot.Area);
                }
            }

            string why = HousingRules.CheckLand(facet, x, y, size, HarvestService.Layers, others);
            if (why != null)
            {
                return why;
            }

            (float Low, float High)? ground = HousingRules.Ground(x, y, size);
            if (ground == null)
            {
                return "That's too far away.";
            }

            if (ground.Value.High - ground.Value.Low > HousingRules.MaxRise + 0.001f)
            {
                return "The ground there is too uneven for a house.";
            }

            baseY = ground.Value.High;
            return null;
        }

        // ------------------------------------------------------------------ building

        private bool TryOwnHouse(NetworkConnection connection, int id, out HouseRecord house, out HousePlot plot,
            out AccountStore.Character character, out Combatant body)
        {
            house = _data.Houses.Find(h => h.Id == id);
            plot = null;
            if (!ItemService.Instance.TryCarrier(connection, out character, out body) || body.Dead.Value || house == null ||
                !_plots.TryGetValue(id, out plot) || plot == null)
            {
                return false;
            }

            AccountStore.Account account = GameAuthenticator.AccountOf(connection);
            if (account == null || account.Name != house.Account || !string.Equals(character.Name, house.Owner, StringComparison.OrdinalIgnoreCase))
            {
                ItemService.Instance.Tell(connection, "This isn't your house.");
                return false;
            }

            return true;
        }

        private void OnPlace(NetworkConnection connection, PlacePiece request, Channel channel)
        {
            if (!TryOwnHouse(connection, request.House, out HouseRecord house, out HousePlot plot, out AccountStore.Character character, out Combatant body))
            {
                return;
            }

            HousingRules.PieceDef def = HousingRules.Piece(request.Piece);
            string why = CheckPiece(plot, def, request.X, request.Y, request.Level, request.Rot, body.transform.position);
            if (why != null)
            {
                ItemService.Instance.Tell(connection, why);
                return;
            }

            foreach ((string item, int amount) in def.Cost)
            {
                if (CraftService.Count(character, item) < amount)
                {
                    ItemService.Instance.Tell(connection, $"You need {amount} {Plural(item)} for that.");
                    return;
                }
            }

            foreach ((string item, int amount) in def.Cost)
            {
                CraftService.Take(character, item, amount);
            }

            int uid = house.NextUid++;
            if (uid > ushort.MaxValue)
            {
                return;
            }

            house.Pieces.Add(new PieceRecord { Uid = uid, Piece = def.Id, X = request.X, Y = request.Y, Level = request.Level, Rot = request.Rot });
            plot.Pieces.Add(new HousePiece { Uid = (ushort)uid, Piece = request.Piece, X = request.X, Y = request.Y, Level = request.Level, Rot = request.Rot });
            house.LastVisit = Now();
            Changed();
        }

        /// <summary>The checks for one piece (shared with build mode's preview). Null when it fits.</summary>
        public static string CheckPiece(HousePlot plot, HousingRules.PieceDef def, int x, int y, int level, int rot, Vector3 builder)
        {
            if (def == null || rot > 3)
            {
                return "You can't build that.";
            }

            (int min, int max) = HousingRules.LevelRange(def);
            if (level < min || level > max)
            {
                return level > max ? $"{def.Name}: not that high up." : $"{def.Name}: not on this level.";
            }

            if (!HousingRules.Inside(def, x, y, rot, plot.Size.Value))
            {
                return "That's outside your land.";
            }

            (Vector3 at, Quaternion turn) = HousingRules.Frame(plot.Origin, plot.BaseY.Value, def, x, y, level, rot);
            if ((at - builder).magnitude > HousingRules.BuildReach)
            {
                return "That's too far away.";
            }

            HashSet<long> taken = plot.TakenSlots();
            foreach (long slot in HousingRules.Slots(def, x, y, level, rot))
            {
                if (taken.Contains(slot))
                {
                    return "Something's already built there.";
                }
            }

            return null;
        }

        private void OnRemove(NetworkConnection connection, RemovePiece request, Channel channel)
        {
            if (!TryOwnHouse(connection, request.House, out HouseRecord house, out HousePlot plot, out AccountStore.Character character, out Combatant body))
            {
                return;
            }

            int index = house.Pieces.FindIndex(p => p.Uid == request.Uid);
            int netIndex = -1;
            for (int i = 0; i < plot.Pieces.Count; i++)
            {
                if (plot.Pieces[i].Uid == request.Uid)
                {
                    netIndex = i;
                    break;
                }
            }

            if (index < 0 || netIndex < 0)
            {
                return;
            }

            HousingRules.PieceDef def = HousingRules.Pieces.Find(d => d.Id == house.Pieces[index].Piece);
            PieceRecord rec = house.Pieces[index];
            if (def != null)
            {
                (Vector3 at, Quaternion _) = HousingRules.Frame(plot.Origin, plot.BaseY.Value, def, rec.X, rec.Y, rec.Level, rec.Rot);
                if ((at - body.transform.position).magnitude > HousingRules.BuildReach)
                {
                    ItemService.Instance.Tell(connection, "That's too far away.");
                    return;
                }

                Refund(character, def, false);
                ItemService.Instance.Resend(connection, character);
            }

            house.Pieces.RemoveAt(index);
            plot.Pieces.RemoveAt(netIndex);
            Changed();
        }

        /// <summary>A piece's materials back (RefundShare of each), to the backpack or the bank.</summary>
        private static void Refund(AccountStore.Character character, HousingRules.PieceDef def, bool toBank)
        {
            foreach ((string item, int amount) in def.Cost)
            {
                int back = Mathf.FloorToInt(amount * HousingRules.RefundShare);
                if (back <= 0)
                {
                    continue;
                }

                if (toBank)
                {
                    ItemService.GiveToBank(character, item, back);
                }
                else
                {
                    ItemService.GiveToBackpack(character, item, back);
                }
            }
        }

        private static string Plural(string item) => System.Text.RegularExpressions.Regex.Replace(item, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant() + "s";

        // ------------------------------------------------------------------ the owner's settings

        private void OnSettings(NetworkConnection connection, HouseSettings request, Channel channel)
        {
            if (!TryOwnHouse(connection, request.House, out HouseRecord house, out HousePlot plot, out _, out _))
            {
                return;
            }

            string name = (request.Name ?? string.Empty).Trim();
            if (name.Length > 40)
            {
                name = name.Substring(0, 40);
            }

            house.Name = name.Length > 0 ? name : house.Name;
            house.WallStyle = Mathf.Clamp(request.WallStyle, 0, Math.Max(0, HousingRules.Styles.Count - 1));
            house.RoofStyle = Mathf.Clamp(request.RoofStyle, 0, Math.Max(0, HousingRules.RoofStyles.Count - 1));
            house.Public = request.Public;
            plot.HouseName.Value = house.Name;
            plot.WallStyle.Value = (byte)house.WallStyle;
            plot.RoofStyle.Value = (byte)house.RoofStyle;
            plot.Public.Value = house.Public;
            Changed();
        }

        private void OnFriend(NetworkConnection connection, HouseFriend request, Channel channel)
        {
            if (!TryOwnHouse(connection, request.House, out HouseRecord house, out HousePlot plot, out AccountStore.Character character, out _))
            {
                return;
            }

            string name = (request.Name ?? string.Empty).Trim();
            if (name.Length == 0 || string.Equals(name, character.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int at = house.Friends.FindIndex(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (request.Add && at < 0 && house.Friends.Count < 50)
            {
                house.Friends.Add(name);
                plot.Friends.Add(name);
                ItemService.Instance.Tell(connection, $"{name} is now a friend of the house.");
            }
            else if (!request.Add && at >= 0)
            {
                house.Friends.RemoveAt(at);
                for (int i = 0; i < plot.Friends.Count; i++)
                {
                    if (string.Equals(plot.Friends[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        plot.Friends.RemoveAt(i);
                        break;
                    }
                }
            }

            Changed();
        }

        private void OnRedeed(NetworkConnection connection, Redeed request, Channel channel)
        {
            if (!TryOwnHouse(connection, request.House, out HouseRecord house, out HousePlot plot, out AccountStore.Character character, out _))
            {
                return;
            }

            HousingRules.PlotTier tier = HousingRules.Plots.Find(t => t.Name == house.Tier);
            foreach (PieceRecord p in house.Pieces)
            {
                HousingRules.PieceDef def = HousingRules.Pieces.Find(d => d.Id == p.Piece);
                if (def != null)
                {
                    Refund(character, def, true);
                }
            }

            if (tier != null)
            {
                ItemService.GiveToBackpack(character, tier.Deed, 1);
            }

            ItemService.Instance.Resend(connection, character);
            Despawn(house);
            _data.Houses.Remove(house);
            Save();
            ItemService.Instance.Tell(connection, "Your house is taken down: the deed is in your backpack and the materials are in your bank box.");
        }

        // ------------------------------------------------------------------ doors, visits and decay

        /// <summary>World doors are free to open; a house's doors open for its owner and friends, or anyone when it's public.</summary>
        private bool DoorAllowed(NetworkConnection connection, ulong doorId)
        {
            HousePlot plot = HousePlot.ForDoor(doorId);
            if (plot == null || plot.Public.Value)
            {
                return true;
            }

            ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out _);
            return character != null && (plot.OwnedBy(character.Name) || plot.IsFriend(character.Name));
        }

        /// <summary>The owner or a friend standing on the plot keeps the house from decaying.</summary>
        private void MarkVisits()
        {
            foreach (HouseRecord house in _data.Houses)
            {
                if (!_plots.TryGetValue(house.Id, out HousePlot plot) || plot == null)
                {
                    continue;
                }

                foreach (NetworkConnection connection in _network.ServerManager.Clients.Values)
                {
                    if (ItemService.Instance.TryCarrier(connection, out AccountStore.Character character, out Combatant body) &&
                        (plot.OwnedBy(character.Name) || plot.IsFriend(character.Name)) &&
                        plot.Area.Contains(WorldSpace.ToTile(body.transform.position)))
                    {
                        house.LastVisit = Now();
                        Changed();
                        break;
                    }
                }
            }
        }

        private void Decay()
        {
            foreach (HouseRecord house in new List<HouseRecord>(_data.Houses))
            {
                if (DateTime.TryParse(house.LastVisit, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last) &&
                    (DateTime.UtcNow - last).TotalDays > HousingRules.DecayDays)
                {
                    Debug.Log($"[Game] House {house.Id} ({house.Owner}) fell down: not visited since {house.LastVisit}");
                    Despawn(house);
                    _data.Houses.Remove(house);
                    Changed();
                }
            }
        }
    }
}

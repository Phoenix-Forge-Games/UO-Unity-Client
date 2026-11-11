using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.Server
{
    /// <summary>
    /// The admin API, for AI agents (Tools/GameMcp is its MCP server) and scripts. A TCP socket on 127.0.0.1 only
    /// (reached from elsewhere through SSH, never through the firewall): each connection sends one line of JSON,
    /// <c>{"token": ..., "op": ..., ...}</c>, and gets one line back, <c>{"ok": true, ...}</c> or
    /// <c>{"ok": false, "error": ...}</c>. The token is in <c>ServerData/admin-token.txt</c>, made on the first start.
    /// Requests run on the game's main thread between frames. The operations are listed in Docs/AdminTools.md.
    /// </summary>
    public sealed partial class AdminService
    {
        private sealed class Pending
        {
            public string Line;
            public string Reply;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _apiRunning;
        private string _token;
        private readonly ConcurrentQueue<Pending> _queue = new ConcurrentQueue<Pending>();

        public static string TokenPath => Path.Combine(AccountStore.DefaultFolder, "admin-token.txt");

        private void StartApi()
        {
            if (_apiPort <= 0 || _apiRunning)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(AccountStore.DefaultFolder);
                if (!File.Exists(TokenPath))
                {
                    var bytes = new byte[24];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(bytes);
                    }

                    File.WriteAllText(TokenPath, BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant());
                }

                _token = File.ReadAllText(TokenPath).Trim();
                // Exclusive: a second server on this machine (a stale process, a test run) must fail to bind rather than
                // share the port and answer requests meant for this one.
                _listener = new TcpListener(IPAddress.Loopback, _apiPort) { ExclusiveAddressUse = true };
                _listener.Start();
                _apiRunning = true;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Admin API" };
                _acceptThread.Start();
                Debug.Log($"[Game] Admin API on 127.0.0.1:{_apiPort}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Game] Admin API didn't start on port {_apiPort}: {e.Message}");
                _apiRunning = false;
            }
        }

        private void StopApi()
        {
            if (!_apiRunning)
            {
                return;
            }

            _apiRunning = false;
            try
            {
                _listener?.Stop();
            }
            catch (Exception)
            {
                // closing anyway
            }

            while (_queue.TryDequeue(out Pending p))
            {
                p.Reply = Error("The server is stopping.");
                p.Done.Set();
            }
        }

        private void AcceptLoop()
        {
            while (_apiRunning)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return; // stopped
                }

                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    NetworkStream stream = client.GetStream();
                    var encoding = new UTF8Encoding(false);
                    var reader = new StreamReader(stream, encoding);
                    var writer = new StreamWriter(stream, encoding) { NewLine = "\n", AutoFlush = true };
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        return;
                    }

                    var pending = new Pending { Line = line };
                    _queue.Enqueue(pending);
                    writer.WriteLine(pending.Done.Wait(120000) ? pending.Reply : Error("The game didn't answer in time."));
                }
                catch (Exception)
                {
                    // the caller went away
                }
            }
        }

        /// <summary>Main thread: answer what's waiting (a few each frame).</summary>
        private void DrainApi()
        {
            for (int i = 0; i < 8 && _queue.TryDequeue(out Pending p); i++)
            {
                try
                {
                    p.Reply = Handle(p.Line);
                }
                catch (Exception e)
                {
                    p.Reply = Error(e.Message);
                }

                p.Done.Set();
            }
        }

        private static string Error(string message) => new JObject { ["ok"] = false, ["error"] = message }.ToString(Newtonsoft.Json.Formatting.None);

        private static string Ok(JObject body = null)
        {
            body ??= new JObject();
            body["ok"] = true;
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static bool SameToken(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
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

        private string Handle(string line)
        {
            JObject req = JObject.Parse(line);
            if (!SameToken((string)req["token"], _token))
            {
                Debug.LogWarning("[Game] Admin API: a request with a wrong token was refused.");
                return Error("Wrong token.");
            }

            string op = (string)req["op"] ?? "";
            if (op != "status" && op != "players" && op != "accounts" && !op.StartsWith("content.get") && op != "content.list")
            {
                Debug.Log($"[Game] Admin API: {op} {Describe(req)}");
            }

            Player who = default;
            string playerName = (string)req["player"];
            if (playerName != null)
            {
                who = FindPlayer(playerName);
                if (!who.Valid)
                {
                    return Error($"No player called '{playerName}' is in the world.");
                }
            }

            string result;
            switch (op)
            {
                case "status":
                {
                    int creatures = 0;
                    foreach (Combatant c in Combatant.All)
                    {
                        creatures += c != null && c.GetComponent<NpcMobile>() != null ? 1 : 0;
                    }

                    var players = PlayersJson();
                    return Ok(new JObject
                    {
                        ["protocol"] = Protocol.Version,
                        ["uptimeSeconds"] = (int)(Time.realtimeSinceStartup - _upSince),
                        ["players"] = players.Count,
                        ["accounts"] = GameAuthenticator.Store?.All.Count ?? 0,
                        ["creatures"] = creatures,
                        ["liveFiles"] = new JArray(new List<string>(LiveContent.Names).ToArray()),
                        ["changedFiles"] = new JArray(new List<string>(LiveContent.ChangedNames()).ToArray()),
                        ["shutdownInSeconds"] = _shutdownAt > 0f ? (int)(_shutdownAt - Time.realtimeSinceStartup) : -1,
                        ["memoryMB"] = (int)(GC.GetTotalMemory(false) / (1024 * 1024)),
                    });
                }

                case "players":
                    return Ok(new JObject { ["players"] = PlayersJson() });

                case "accounts":
                {
                    var list = new JArray();
                    foreach (AccountStore.Account a in GameAuthenticator.Store?.All ?? new List<AccountStore.Account>())
                    {
                        var chars = new JArray();
                        foreach (AccountStore.Character c in a.Characters)
                        {
                            chars.Add(new JObject { ["Id"] = c.Id, ["Name"] = c.Name });
                        }

                        list.Add(new JObject { ["Name"] = a.Name, ["Admin"] = a.Admin, ["Created"] = a.Created, ["Characters"] = chars });
                    }

                    return Ok(new JObject { ["accounts"] = list });
                }

                case "account.admin":
                {
                    if (GameAuthenticator.Store == null)
                    {
                        return Error("No accounts yet (nobody has logged in since the server started).");
                    }

                    string name = SetAdmin((string)req["account"], (bool?)req["admin"] ?? true);
                    return name == null ? Error($"No account called '{req["account"]}'.") : Ok(new JObject { ["account"] = name, ["admin"] = (bool?)req["admin"] ?? true });
                }

                case "broadcast":
                    result = Announce((string)req["text"]);
                    return result == null ? Ok() : Error(result);

                case "kick":
                    return who.Valid ? Ok(new JObject { ["result"] = Kick(who, (string)req["reason"]) }) : Error("Say which player.");

                case "teleport":
                {
                    if (!who.Valid)
                    {
                        return Error("Say which player.");
                    }

                    if (!Place((string)req["to"], out Vector3 _, out bool _))
                    {
                        return Error($"No such place: '{req["to"]}'. Give tile coordinates (\"x y\" or \"x y z\"), a starting city or a player.");
                    }

                    return Ok(new JObject { ["result"] = Teleport(who, (string)req["to"]) });
                }

                case "give":
                    return who.Valid ? Answer(Give(who, (string)req["item"], (int?)req["amount"] ?? 1), "No item called") : Error("Say which player.");

                case "spawn":
                {
                    Vector3 at;
                    if (who.Valid)
                    {
                        at = who.Object.transform.position + who.Object.transform.forward * 4f;
                    }
                    else if (!Place((string)req["at"], out at, out bool _))
                    {
                        return Error("Say where: a player (\"player\") or a place (\"at\": \"x y\", a starting city).");
                    }

                    return Answer(Spawn((string)req["creature"], (int?)req["count"] ?? 1, at), "No creature called");
                }

                case "kill":
                    if (!who.Valid)
                    {
                        return Error("Say which player.");
                    }

                    who.Body.Kill();
                    return Ok(new JObject { ["result"] = $"{who.Character.Name} is slain." });

                case "heal":
                    if (!who.Valid)
                    {
                        return Error("Say which player.");
                    }

                    Heal(who.Body);
                    return Ok(new JObject { ["result"] = $"{who.Character.Name} is restored." });

                case "skill":
                {
                    if (!who.Valid)
                    {
                        return Error("Say which player.");
                    }

                    string done = SetSkill(who, (string)req["skill"], (float?)req["value"] ?? 0f);
                    return done == null ? Error($"No skill called '{req["skill"]}'.") : Ok(new JObject { ["result"] = done });
                }

                case "save":
                    return Ok(new JObject { ["result"] = SaveWorld() });

                case "shutdown":
                    return Ok(new JObject { ["result"] = PlanShutdown((int?)req["seconds"] ?? 60, (string)req["message"]) });

                case "content.list":
                {
                    var files = new JArray();
                    foreach (string name in LiveContent.Names)
                    {
                        files.Add(new JObject { ["Name"] = name, ["Changed"] = LiveContent.IsChanged(name), ["Characters"] = LiveContent.Text(name)?.Length ?? 0 });
                    }

                    return Ok(new JObject { ["files"] = files });
                }

                case "content.get":
                {
                    string file = (string)req["file"];
                    string text = LiveContent.Text(file);
                    if (text == null)
                    {
                        return Error($"No live content file '{file}'. Live files: {string.Join(", ", LiveContent.Names)}.");
                    }

                    string path = (string)req["path"];
                    if (string.IsNullOrEmpty(path))
                    {
                        return Ok(new JObject { ["file"] = file, ["changed"] = LiveContent.IsChanged(file), ["text"] = text });
                    }

                    var found = new JArray();
                    foreach (JToken t in JToken.Parse(text).SelectTokens(path))
                    {
                        found.Add(t.DeepClone());
                    }

                    return Ok(new JObject { ["file"] = file, ["path"] = path, ["matches"] = found });
                }

                case "content.set":
                {
                    string file = (string)req["file"], path = (string)req["path"];
                    string text = LiveContent.Text(file);
                    if (text == null)
                    {
                        return Error($"No live content file '{file}'. Live files: {string.Join(", ", LiveContent.Names)}.");
                    }

                    JToken root = JToken.Parse(text);
                    JToken value = req["value"];
                    bool append = (bool?)req["append"] ?? false;
                    var targets = new List<JToken>(root.SelectTokens(path ?? "$"));
                    if (targets.Count == 0)
                    {
                        return Error($"Nothing in {file} matches '{path}'.");
                    }

                    foreach (JToken t in targets)
                    {
                        if (append)
                        {
                            if (!(t is JArray array))
                            {
                                return Error($"'{path}' isn't a list, so nothing can be appended to it.");
                            }

                            array.Add(value.DeepClone());
                        }
                        else if (t == root)
                        {
                            root = value.DeepClone();
                        }
                        else
                        {
                            t.Replace(value.DeepClone());
                        }
                    }

                    string error = LiveContent.Save(file, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    return error == null ? Ok(new JObject { ["file"] = file, ["changedTokens"] = targets.Count, ["live"] = true }) : Error($"Refused (nothing changed): {error}");
                }

                case "content.put":
                {
                    string file = (string)req["file"];
                    string error = LiveContent.Save(file, (string)req["text"] ?? "");
                    return error == null ? Ok(new JObject { ["file"] = file, ["live"] = true }) : Error($"Refused (nothing changed): {error}");
                }

                case "content.revert":
                {
                    string file = (string)req["file"];
                    if (!LiveContent.IsLive(file))
                    {
                        return Error($"No live content file '{file}'.");
                    }

                    LiveContent.Forget(file);
                    return Ok(new JObject { ["file"] = file, ["changed"] = false });
                }

                case "import.servuo":
                case "import.seriousfun":
                {
                    // Dry run unless told otherwise: moving players over is done once, on purpose.
                    JObject report = ServUOImport.Import((string)req["file"], (string)req["config"], (bool?)req["dryRun"] ?? true, (bool?)req["replace"] ?? false);
                    return Ok(new JObject { ["report"] = report });
                }

                default:
                    return Error($"Unknown op '{op}'. See Docs/AdminTools.md.");
            }
        }

        private static string Answer(string result, string failurePrefix) =>
            result.StartsWith(failurePrefix, StringComparison.Ordinal) || result.EndsWith("backpack is full.", StringComparison.Ordinal)
                ? Error(result)
                : Ok(new JObject { ["result"] = result });

        /// <summary>What a request asked for, for the log (no token; long texts shortened).</summary>
        private static string Describe(JObject req)
        {
            var copy = (JObject)req.DeepClone();
            copy.Remove("token");
            copy.Remove("op");
            if (copy["text"] is JValue t && t.Type == JTokenType.String && ((string)t).Length > 120)
            {
                copy["text"] = $"({((string)t).Length:N0} characters)";
            }

            string s = copy.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }

        // ------------------------------------------------------------------ planned shutdown

        private static readonly int[] Warnings = { 600, 300, 120, 60, 30, 10, 5 };
        private float _shutdownAt = -1f;
        private int _lastWarning;
        private string _shutdownMessage;

        private static string Left(int seconds) => seconds >= 60
            ? $"{Mathf.CeilToInt(seconds / 60f)} minute{(seconds > 60 ? "s" : "")}"
            : $"{seconds} second{(seconds == 1 ? "" : "s")}";

        /// <summary>
        /// Stop the server in a while, warning everyone (a minute before, then every ten seconds for the last half
        /// minute), and save everything before it goes. Negative seconds: call it off. The process then exits, so
        /// whatever restarts the server (the deploy, the scheduled task) brings the new one up.
        /// </summary>
        private string PlanShutdown(int seconds, string message)
        {
            if (seconds < 0)
            {
                bool was = _shutdownAt > 0f;
                _shutdownAt = -1f;
                if (was)
                {
                    Announce("The server restart is called off.");
                }

                return was ? "Shutdown called off." : "No shutdown was planned.";
            }

            _shutdownAt = Time.realtimeSinceStartup + seconds;
            _shutdownMessage = string.IsNullOrWhiteSpace(message) ? "The server is restarting" : message.Trim();
            _lastWarning = seconds;
            if (seconds > 0)
            {
                Announce($"{_shutdownMessage} in {Left(seconds)}.");
            }

            return seconds == 0 ? "Saving and stopping now." : $"Stopping in {seconds} seconds.";
        }

        private void UpdateShutdown()
        {
            if (_shutdownAt <= 0f)
            {
                return;
            }

            float left = _shutdownAt - Time.realtimeSinceStartup;
            if (left <= 0f)
            {
                _shutdownAt = -1f;
                Announce($"{_shutdownMessage} now.");
                SaveWorld();
                Debug.Log("[Game] Planned shutdown: saved, stopping.");
                StopApi();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }

            int s = Mathf.CeilToInt(left);
            foreach (int w in Warnings)
            {
                if (s <= w && w < _lastWarning)
                {
                    _lastWarning = w;
                    Announce($"{_shutdownMessage} in {Left(s)}.");
                    break;
                }
            }
        }
    }
}

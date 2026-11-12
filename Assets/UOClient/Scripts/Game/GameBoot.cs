using System;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UOClient;
using UOGame.Net;
using UOGame.Server;

namespace UOGame
{
    /// <summary>
    /// Starts the game one of four ways and connects it; the main menu (<see cref="Menu.MenuDirector"/>, in the
    /// MainMenu scene) is what the player sees and asks this to play.
    /// <list type="bullet">
    /// <item><b>Headless server:</b> a build started with <c>-server</c> (or in batch mode) listens on <c>-port</c>
    /// (default 7770) and never shows a menu.</item>
    /// <item><b>Solo:</b> server and client in this process, listening only on this machine, on a local account.</item>
    /// <item><b>Host:</b> the same, open to friends on the network (UDP port allowed in the firewall).</item>
    /// <item><b>Join:</b> a client connecting to a host or a headless server.</item>
    /// </list>
    /// A client loads the MainMenu scene over this one at start, and again whenever the player leaves the world.
    /// </summary>
    public sealed class GameBoot : MonoBehaviour
    {
        public const ushort DefaultPort = 7770;
        /// <summary>The shard players connect to unless they type another address.</summary>
        public const string DefaultServer = "seriousfun.tplinkdns.com";
        public const string MenuScene = "MainMenu";

        /// <summary>The account Solo play logs in to (the server only listens on this machine then).</summary>
        private const string SoloAccount = "Solo";
        private const string SoloPassword = "solo-local";
        private const float ConnectSeconds = 30f; // a busy server (or Solo starting its own) can take a while to answer

        [SerializeField] private NetworkManager _network;

        private bool _answered, _inWorld, _wasInWorld, _leaving;
        private float _giveUpAt = float.PositiveInfinity;

        public static GameBoot Instance { get; private set; }

        /// <summary>The server let us in: the account's characters (and which was played last).</summary>
        public event Action<LoginResult> LoggedIn;
        /// <summary>Couldn't connect, or the server turned us away or dropped us before we were in the world: why.</summary>
        public event Action<string> Failed;
        /// <summary>A character request was refused (name taken, already playing...): why.</summary>
        public event Action<string> Refused;

        /// <summary>Connecting or connected (logged in or not).</summary>
        public bool Busy => _network != null && (_network.ClientManager.Started || _giveUpAt < float.PositiveInfinity);

        private Tugboat Transport => _network.TransportManager.GetTransport<Tugboat>();

        private void Awake() => Instance = this;

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Application.isBatchMode || Array.IndexOf(args, "-server") >= 0)
            {
                ushort port = DefaultPort;
                int i = Array.IndexOf(args, "-port");
                if (i >= 0 && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort p))
                {
                    port = p;
                }

                Application.targetFrameRate = 60; // the HUD panels stay off: a server has no screen
                Transport.SetServerBindAddress(string.Empty, IPAddressType.IPv4);
                Transport.SetPort(port);
                _network.ServerManager.StartConnection();
                Debug.Log($"[Game] Headless server listening on port {port}");
                return;
            }

            // Panels start switched off in the scene (a headless server never builds them); a client turns them on.
            foreach (UnityEngine.UIElements.PanelRenderer panel in FindObjectsByType<UnityEngine.UIElements.PanelRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                panel.enabled = true;
            }

            _network.ClientManager.OnClientConnectionState += OnClientState;
            GameAuthenticator.Answered += OnLoginAnswered;
            CharacterService.Refused += OnRefused;
            ShowMenu();
        }

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.ClientManager.OnClientConnectionState -= OnClientState;
            }

            GameAuthenticator.Answered -= OnLoginAnswered;
            CharacterService.Refused -= OnRefused;
        }

        /// <summary>The main menu scene over this one (unless it's already there).</summary>
        public static void ShowMenu()
        {
            if (!SceneManager.GetSceneByName(MenuScene).isLoaded)
            {
                SceneManager.LoadSceneAsync(MenuScene, LoadSceneMode.Additive);
            }
        }

        /// <summary>The main menu goes (once the player is in the world and the loading screen is done).</summary>
        public static void HideMenu()
        {
            Scene menu = SceneManager.GetSceneByName(MenuScene);
            if (menu.isLoaded)
            {
                SceneManager.UnloadSceneAsync(menu);
            }
        }

        // ------------------------------------------------------------------ connecting

        /// <summary>Server and client here, the server listening only on this machine, on the local account.</summary>
        public void PlaySolo() => Play(host: true, open: false, "127.0.0.1", DefaultPort, SoloAccount, SoloPassword);

        /// <summary>Host a game friends can join (not offered in the menu yet).</summary>
        public void Host(ushort port, string account, string password) => Play(host: true, open: true, "127.0.0.1", port, account, password);

        public void Join(string address, ushort port, string account, string password) =>
            Play(host: false, open: false, string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim(), port, account, password);

        private void Play(bool host, bool open, string address, ushort port, string account, string password)
        {
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                Failed?.Invoke("Enter your account and password.");
                return;
            }

            Transport.SetPort(port);
            GameAuthenticator.Account = account.Trim();
            GameAuthenticator.Password = password;
            if (host)
            {
                // Solo listens only on this machine; a hosted game on every network interface.
                Transport.SetServerBindAddress(open ? string.Empty : "127.0.0.1", IPAddressType.IPv4);
                _network.ServerManager.StartConnection();
            }

            Transport.SetClientAddress(address);
            _answered = false;
            _leaving = false;
            _giveUpAt = Time.unscaledTime + ConnectSeconds;
            _network.ClientManager.StartConnection();
        }

        /// <summary>Send a request to the server (choose or create a character...).</summary>
        public void Send<T>(T message) where T : struct, IBroadcast => _network.ClientManager.Broadcast(message);

        /// <summary>Disconnect (and stop our own server, when we were hosting).</summary>
        public void Leave()
        {
            _giveUpAt = float.PositiveInfinity;
            _leaving = true; // our own doing: nothing to report
            _network.ClientManager.StopConnection();
            if (_network.IsServerStarted)
            {
                _network.ServerManager.StopConnection(true);
            }
        }

        private void OnLoginAnswered(LoginResult result)
        {
            _answered = true;
            _giveUpAt = float.PositiveInfinity;
            if (!result.Ok)
            {
                Leave();
                Failed?.Invoke(result.Reason);
                return;
            }

            LoggedIn?.Invoke(result);
        }

        private void OnRefused(string reason) => Refused?.Invoke(reason);

        private void Update()
        {
            if (!_answered && Time.unscaledTime >= _giveUpAt)
            {
                Leave();
                Failed?.Invoke("No answer from the server. Check the address and port, and that the server is running.");
            }

            // Out of the world again (logged out, dropped): back to the menu.
            _inWorld = PlayerMotor.Local != null;
            if (_wasInWorld && !_inWorld)
            {
                GameInput.SetTyping(false);
                ShowMenu();
            }

            _wasInWorld = _inWorld;
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
            {
                return;
            }

            if (_network.IsServerStarted)
            {
                _network.ServerManager.StopConnection(true);
            }

            bool waiting = _giveUpAt < float.PositiveInfinity;
            _giveUpAt = float.PositiveInfinity;
            if (_leaving)
            {
                _leaving = false;
            }
            else if (!_answered && waiting)
            {
                Failed?.Invoke("Couldn't reach the server, or it closed the connection.");
            }
            else if (_answered && !_inWorld)
            {
                Failed?.Invoke("Disconnected.");
            }
        }
    }
}

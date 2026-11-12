using System;
using System.Collections.Generic;
using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UOClient.Bake;
using UOGame.Server;

namespace UOGame.Net
{
    /// <summary>
    /// The handshake. As soon as a client connects it sends its protocol version, its content hash and its account
    /// login; the server turns it away with a reason when any of them doesn't match, and otherwise lets it in with the
    /// account's characters. Nothing else reaches a connection that hasn't passed this.
    /// </summary>
    public sealed class GameAuthenticator : Authenticator
    {
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        [SerializeField] private WorldPalette _palette;

        /// <summary>The palette, by address when the scene holds none (paid art, Docs/SecureArt.md).</summary>
        private WorldPalette Palette => _palette = global::UOClient.Content.SecureArt.Resolve(_palette, global::UOClient.Content.SecureArtAddresses.WorldPalette);

        /// <summary>Set by the menu before connecting: the login this client sends.</summary>
        public static string Account, Password;

        /// <summary>Client side: the server's answer to our login.</summary>
        public static event Action<LoginResult> Answered;

        /// <summary>Server side: the accounts, opened on the first login.</summary>
        public static AccountStore Store { get; private set; }

        /// <summary>Server side: open the accounts now (the admin API needs them before anyone logs in).</summary>
        public static AccountStore OpenStore() => Store ??= new AccountStore(AccountStore.DefaultFolder);

        private static readonly Dictionary<NetworkConnection, AccountStore.Account> Accounts =
            new Dictionary<NetworkConnection, AccountStore.Account>();

        /// <summary>The account a connection logged in as (server side), or null.</summary>
        public static AccountStore.Account AccountOf(NetworkConnection connection) =>
            connection != null && Accounts.TryGetValue(connection, out AccountStore.Account account) ? account : null;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            NetworkManager.ClientManager.OnClientConnectionState += OnClientState;
            NetworkManager.ServerManager.OnRemoteConnectionState += OnRemoteState;
            NetworkManager.ServerManager.RegisterBroadcast<LoginRequest>(OnLogin, false);
            NetworkManager.ClientManager.RegisterBroadcast<LoginResult>(OnResult);
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
            {
                return;
            }

            NetworkManager.ClientManager.Broadcast(new LoginRequest
            {
                Protocol = Protocol.Version,
                Content = Protocol.ContentHash(Palette),
                Account = Account,
                Password = Password,
            });
        }

        private void OnRemoteState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                Accounts.Remove(connection);
            }
        }

        private void OnLogin(NetworkConnection connection, LoginRequest request, Channel channel)
        {
            if (connection.IsAuthenticated)
            {
                connection.Disconnect(true); // a second login on one connection
                return;
            }

            OpenStore();
            string reason = null;
            AccountStore.Account account = null;
            if (request.Protocol != Protocol.Version)
            {
                reason = $"Version mismatch (server {Protocol.Version}, game {request.Protocol}): update the game.";
            }
            else if (request.Content != Protocol.ContentHash(Palette))
            {
                reason = "This game's world content doesn't match the server's: update the game.";
            }
            else
            {
                account = Store.Login(request.Account, request.Password, out reason);
            }

            bool ok = account != null;
            var result = new LoginResult { Ok = ok, Reason = reason, Characters = Array.Empty<CharacterSummary>() };
            if (ok)
            {
                Accounts[connection] = account;
                result.Characters = Summaries(account);
                result.LastPlayed = account.LastPlayed;
                Debug.Log($"[Game] {connection.ClientId} logged in as {account.Name}");
            }
            else
            {
                Debug.Log($"[Game] {connection.ClientId} ({connection.GetAddress()}) refused as '{request.Account}': {reason}");
            }

            NetworkManager.ServerManager.Broadcast(connection, result, false);
            if (ok)
            {
                AdminService.Instance?.LoggedIn(connection); // the server's live content changes, before anything uses them
                OnAuthenticationResult?.Invoke(connection, true);
            }
            else
            {
                // Refusing disconnects at once, which can beat the answer out the door: give it a moment to arrive.
                StartCoroutine(RefuseLater(connection));
            }
        }

        private System.Collections.IEnumerator RefuseLater(NetworkConnection connection)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (connection.IsActive)
            {
                OnAuthenticationResult?.Invoke(connection, false);
            }
        }

        public static CharacterSummary[] Summaries(AccountStore.Account account)
        {
            var list = new CharacterSummary[account.Characters.Count];
            for (int i = 0; i < list.Length; i++)
            {
                AccountStore.Character c = account.Characters[i];
                list[i] = new CharacterSummary { Id = c.Id, Name = c.Name, Look = ItemService.LookOf(c) };
            }

            return list;
        }

        private void OnResult(LoginResult result, Channel channel) => Answered?.Invoke(result);
    }
}

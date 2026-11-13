using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UOGame.Server
{
    /// <summary>
    /// A hosted shard's accounts are the website's (britanniareborn.com): with <c>BR_ACCOUNT_API</c> (the API base, e.g.
    /// <c>https://britanniareborn.com/api</c>) and <c>BR_SHARD_KEY</c> both set, a login is checked with the website's
    /// <c>POST {api}/shard/login</c> (PhoenixForgeWeb docs/API.md) instead of the local passwords. Without them (solo,
    /// a listen host, the editor) the server keeps its own accounts as before. Read once. Passwords and the key are
    /// never logged.
    /// </summary>
    public static class WebsiteAccounts
    {
        public const int TimeoutSeconds = 10;
        public const string Unreachable = "The account service is unreachable; try again shortly.";

        private static readonly string Api = Read("BR_ACCOUNT_API")?.TrimEnd('/');
        private static readonly string Key = Read("BR_SHARD_KEY");

        /// <summary>Logins go to the website.</summary>
        public static bool Enabled => Api != null && Key != null;

        /// <summary>The website's answer. <see cref="ok"/> false with <see cref="reason"/> when it turned the login away.</summary>
        [Serializable]
        public sealed class Answer
        {
            public bool ok;
            public string username;
            public bool admin;
            public string reason;
        }

        [Serializable]
        private sealed class Request
        {
            public string username;
            public string password;
        }

        private static string Read(string variable)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Checks a login with the website, then calls <paramref name="done"/> on the main thread with its answer, or
        /// with null and the reason when the website couldn't be asked (unreachable, timed out, or an unexpected reply).
        /// </summary>
        public static IEnumerator Check(string username, string password, Action<Answer, string> done)
        {
            string body = JsonUtility.ToJson(new Request { username = username ?? string.Empty, password = password ?? string.Empty });
            using (var request = new UnityWebRequest(Api + "/shard/login", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" };
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-shard-key", Key);
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 401/403 here means the shard key is wrong (or missing on the website): an operator problem.
                    Debug.LogWarning($"[Game] Account service: {request.result} (HTTP {request.responseCode}) {request.error}");
                    done(null, Unreachable);
                    yield break;
                }

                Answer answer = null;
                try
                {
                    answer = JsonUtility.FromJson<Answer>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Game] Account service: unreadable answer ({e.Message}).");
                }

                if (answer == null)
                {
                    done(null, Unreachable);
                    yield break;
                }

                done(answer, null);
            }
        }
    }
}

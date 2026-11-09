using UnityEngine;
using UnityEngine.SceneManagement;
using UOClient.Net;
using UOClient.World;

namespace UOClient
{
    /// <summary>
    /// Owns the single <see cref="UoSession"/> and <see cref="WorldState"/> for the app and pumps
    /// the session every frame. Lives across scene loads; switches to the World scene when a
    /// character enters the world and back to Login when the session fails.
    /// </summary>
    public sealed class UoClientRuntime : MonoBehaviour
    {
        private const string WorldScene = "World";
        private const string LoginScene = "Login";

        public static UoClientRuntime Instance { get; private set; }

        public UoSession Session { get; private set; }
        public WorldState World { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Session = new UoSession();
            World = new WorldState(Session.Send);
            Session.PacketReceived += World.Handle;
            Session.EnteredWorld += OnEnteredWorld;
            Session.Failed += OnFailed;
        }

        private void Update() => Session.Poll();

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Session.Dispose();
                Instance = null;
            }
        }

        private void OnEnteredWorld(LoginConfirm confirm)
        {
            World.Begin(confirm);
            SceneManager.LoadScene(WorldScene);
        }

        private void OnFailed(string message)
        {
            if (SceneManager.GetActiveScene().name == WorldScene)
            {
                Debug.LogWarning($"[UOClient] {message}");
                SceneManager.LoadScene(LoginScene);
            }
        }
    }
}

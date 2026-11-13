using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UOGame
{
    /// <summary>
    /// Black over everything from the game's first frame, so nobody sees the empty sky of the game scene while the
    /// main menu loads over it. The menu lifts it once its own black (which fades up when the town behind it is in)
    /// is on screen; if nothing does, it lifts by itself after a while.
    /// </summary>
    public static class StartupCurtain
    {
        /// <summary>Longest it stays down if nothing lifts it (seconds).</summary>
        private const float GiveUpSeconds = 30f;

        private static GameObject _curtain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Lower()
        {
            if (Application.isBatchMode || _curtain != null)
            {
                return; // the dedicated server draws nothing
            }

            _curtain = new GameObject("Startup curtain");
            Object.DontDestroyOnLoad(_curtain);
            var canvas = _curtain.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // over every panel
            var black = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
            black.transform.SetParent(_curtain.transform, false);
            black.color = Color.black;
            black.raycastTarget = false;
            var rect = (RectTransform)black.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _curtain.AddComponent<Lifter>().Begin(GiveUpSeconds);
            Debug.Log("[Boot] Startup curtain down");
        }

        /// <summary>Lift it (after a couple of frames, so whatever takes over from it is drawn first).</summary>
        public static void Lift()
        {
            if (_curtain != null)
            {
                _curtain.GetComponent<Lifter>().Begin(0f);
            }
        }

        private sealed class Lifter : MonoBehaviour
        {
            private Coroutine _running;

            public void Begin(float after)
            {
                if (_running != null)
                {
                    StopCoroutine(_running);
                }

                _running = StartCoroutine(Go(after));
            }

            private IEnumerator Go(float after)
            {
                if (after > 0f)
                {
                    yield return new WaitForSecondsRealtime(after);
                }

                yield return null;
                yield return null;
                Destroy(gameObject);
                _curtain = null;
                Debug.Log($"[Boot] Startup curtain lifted at {Time.realtimeSinceStartup:0.0}s");
            }
        }
    }
}

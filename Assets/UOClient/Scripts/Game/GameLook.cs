using System;
using UnityEngine;
using UOClient;

namespace UOGame
{
    /// <summary>
    /// The client's dresser: the Sidekick wardrobe and the player's UO data (tiledata names say what a worn item is
    /// made of; hues colour it). Built the first time a human is shown; without UO data, humans stay undressed.
    /// </summary>
    public sealed class GameLook : MonoBehaviour
    {
        [SerializeField] private SidekickWardrobe _wardrobe;
        [SerializeField] private RuntimeAnimatorController _humanoidAnimator;

        private static GameLook _instance;
        private static SidekickDresser _dresser;
        private static bool _tried;

        private void Awake() => _instance = this;

        public static SidekickDresser Dresser
        {
            get
            {
                if (_dresser == null && !_tried && _instance != null)
                {
                    // Paid art by address when the scene holds none (Docs/SecureArt.md).
                    _instance._wardrobe = global::UOClient.Content.SecureArt.Resolve(_instance._wardrobe, global::UOClient.Content.SecureArtAddresses.SidekickWardrobe);
                    _instance._humanoidAnimator = global::UOClient.Content.SecureArt.Resolve(_instance._humanoidAnimator, global::UOClient.Content.SecureArtAddresses.HumanAnimator);
                }

                if (_dresser == null && !_tried && _instance != null && _instance._wardrobe != null)
                {
                    _tried = true;
                    try
                    {
                        UOClient.Data.TileData tiles = UoData.Tiles;
                        if (tiles != null)
                        {
                            string huesPath = System.IO.Path.Combine(ClientSettings.DataFolder, "hues.mul");
                            var hues = System.IO.File.Exists(huesPath) ? new UOClient.Data.Hues(huesPath) : null;
                            _dresser = new SidekickDresser(_instance._wardrobe, tiles, hues, _instance._humanoidAnimator);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Game] Couldn't set up the character dresser ({e.Message}); humans stay undressed.");
                    }
                }

                return _dresser;
            }
        }
    }
}

using UnityEngine;
using UOClient;
using UOGame.Content;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Placing a ship (a ship deed or a docked ship from the backpack): a ghost of its hull follows the mouse over the
    /// water, green where it may go (all water, near the coast, within reach: <see cref="Boat.CheckPlace"/>) and red
    /// where it may not, with the reason on the banner. Rotate turns it a quarter; a click places it (the server checks
    /// again); Escape cancels.
    /// </summary>
    public sealed partial class BuildMode
    {
        private BoatRules.BoatDef _ship;
        private int _shipHeading;
        private GameObject _shipGhost;
        private Vector2Int _shipAt;

        private bool TryShipDeed(ItemView item)
        {
            BoatRules.BoatDef def = BoatRules.ByItem(ItemService.Catalog?.Get(item.Kind)?.Type);
            if (def == null)
            {
                return false;
            }

            Stop();
            _deed = item.Id;
            _ship = def;
            PlayerMotor me = PlayerMotor.Local;
            Vector3 f = me != null ? me.transform.forward : Vector3.forward;
            _shipHeading = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? (f.x > 0 ? 1 : 3) : (f.z > 0 ? 0 : 2); // bow away from you
            SetBuildInput(true);
            Status = "Choose where the ship goes on the water.";
            Changed?.Invoke();
            return true;
        }

        private void StopShip()
        {
            _ship = null;
            if (_shipGhost != null)
            {
                Destroy(_shipGhost);
                _shipGhost = null;
            }
        }

        private void UpdateShip(PlayerMotor me)
        {
            if (System.Array.Find(ItemService.Mine, i => i.Id == _deed).Id == 0)
            {
                Stop(); // the deed is gone
                return;
            }

            UOControls.BuildActions build = GameInput.Controls.Build;
            if (!GameInput.Typing && build.Rotate.WasPressedThisFrame())
            {
                _shipHeading = (_shipHeading + 1) % 4;
            }

            if (!WaterUnderPointer(out Vector2Int tile, out float surface))
            {
                if (_shipGhost != null)
                {
                    _shipGhost.SetActive(false);
                }

                Valid = false;
                Status = "Point at the water where the ship should go. {Build/Rotate} turns it; {Back} cancels.";
                Changed?.Invoke();
                return;
            }

            if (_shipGhost == null)
            {
                _shipGhost = new GameObject("Ship ghost");
                GameObject hull = Boat.MakeHull(_ship, _shipGhost.transform);
                if (hull != null)
                {
                    Ghostly(hull);
                }
            }

            _shipGhost.SetActive(true);
            Vector3 centre = WorldSpace.TileCenter(tile.x, tile.y, 0);
            centre.y = surface;
            _shipGhost.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, _shipHeading * 90f, 0f));
            string why = Boat.CheckPlace(_ship, tile, _shipHeading, me.transform.position);
            Valid = why == null;
            Tint(_shipGhost, Valid);
            Status = why ?? "{Build/Place} to place the ship here. {Build/Rotate} turns it; {Back} cancels.";
            _shipAt = tile;
            Changed?.Invoke();

            if (Clicked(build))
            {
                if (Valid)
                {
                    _network.ClientManager.Broadcast(new PlaceBoat { Deed = _deed, X = _shipAt.x, Y = _shipAt.y, Heading = (byte)_shipHeading });
                    Stop();
                }
                else
                {
                    ItemService.ShowNotice(Status);
                }
            }
        }

        /// <summary>The water tile under the mouse: walk the pointer's ray until it dips under a water surface (water
        /// has no collider), stopping at anything solid first.</summary>
        private static bool WaterUnderPointer(out Vector2Int tile, out float surface)
        {
            tile = default;
            surface = 0f;
            Camera camera = Camera.main;
            if (camera == null || GameInput.IsPointerOverUI(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>()))
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(GameInput.Controls.Gameplay.Point.ReadValue<Vector2>());
            float solid = Physics.Raycast(ray, out RaycastHit hit, 300f, ~(1 << 2), QueryTriggerInteraction.Ignore) ? hit.distance : 300f;
            for (float d = 0f; d <= solid; d += 0.5f)
            {
                Vector3 p = ray.GetPoint(d);
                if (WaterMap.TryGet(p, out float s) && p.y <= s + 0.05f)
                {
                    tile = WorldSpace.ToTile(p);
                    surface = s;
                    return true;
                }
            }

            return false;
        }
    }
}

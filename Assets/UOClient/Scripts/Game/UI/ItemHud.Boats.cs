using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Content;
using UOGame.Net;
using ShipOrder = UOGame.Server.BoatService.Order;

namespace UOGame.UI
{
    /// <summary>
    /// The ship window: it opens when you're on a ship or close enough to board one. Anyone may board or go ashore;
    /// the owner sails it (UO's ship orders as buttons: forward, back, sideways, diagonals, turns, speed, stop) and can
    /// dry-dock it back into the backpack. Closed by hand, it stays shut until you leave that ship.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _ship;
        private int _shipShown = -1, _shipDismissed = -1;
        private bool _shipAboard, _shipOwner, _shipHelm;
        private byte _shipPose;

        private void UpdateBoats(PlayerMotor me)
        {
            Boat under = Boat.Under(me.transform.position);
            Boat boat = under != null ? under : Boat.Near(me.transform.position, BoatRules.BoardReach);
            if (boat == null)
            {
                _shipDismissed = -1;
                if (_ship != null && _ship.IsOpen)
                {
                    _ship.Close();
                }

                _shipShown = -1;
                return;
            }

            bool aboard = under == boat;
            int myId = me.GetComponent<PlayerIdentity>()?.CharacterId ?? -1;
            bool owner = boat.OwnerId.Value == myId; // the server checks it too
            bool helm = boat.Helm.Value == myId + 1;
            byte pose = me.GetComponent<PlayerIdentity>()?.Pose.Value ?? PlayerIdentity.Standing;
            if (boat.ObjectId == _shipDismissed)
            {
                return;
            }

            if (_ship == null)
            {
                _ship = new Window(_layer, "Ship", 300, 330, new Vector2(40, 360));
                _ship.Closed += () => _shipDismissed = _shipShown;
            }

            if (_shipShown != boat.ObjectId || _shipAboard != aboard || _shipOwner != owner || _shipHelm != helm || _shipPose != pose || !_ship.IsOpen)
            {
                _shipPose = pose;
                _shipShown = boat.ObjectId;
                _shipAboard = aboard;
                _shipOwner = owner;
                _shipHelm = helm;
                BuildShip(boat, aboard, owner, helm);
                _ship.Open();
            }
        }

        private void BuildShip(Boat boat, bool aboard, bool owner, bool helm)
        {
            _ship.SetTitle(string.IsNullOrEmpty(boat.ShipName.Value) ? "Ship" : boat.ShipName.Value);
            _ship.Body.Clear();
            void Send(ShipOrder order) => _network.ClientManager.Broadcast(new BoatCommand { Boat = boat.ObjectId, Order = (byte)order });
            void B(string text, ShipOrder order)
            {
                var b = new Button(() => Send(order)) { text = text };
                b.AddToClassList("npc-option");
                _ship.Body.Add(b);
            }

            void Note(string text) => _ship.Body.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.93f, 0.89f, 0.8f) } });
            Note($"Owner: {boat.OwnerName.Value}");
            if (!aboard)
            {
                B("Board", ShipOrder.Board);
                if (owner)
                {
                    B("Dry dock", ShipOrder.DryDock);
                }

                return;
            }

            if (owner)
            {
                if (helm)
                {
                    Note("At the helm: forward and back for speed, left and right to turn.");
                    B("Leave the helm", ShipOrder.LeaveHelm);
                }
                else
                {
                    B("Take the helm", ShipOrder.TakeHelm);
                }

                B("Drop anchor", ShipOrder.Anchor);
            }
            else
            {
                Note("Only the owner can sail this ship.");
            }

            if (!helm)
            {
                if (_shipPose == PlayerIdentity.Sitting)
                {
                    Note("Sitting on the deck: a movement key stands you up.");
                    B("Stand up", ShipOrder.Stand);
                }
                else
                {
                    B("Sit down", ShipOrder.Sit);
                }
            }

            B("Go ashore", ShipOrder.Ashore);
            if (owner)
            {
                B("Dry dock", ShipOrder.DryDock);
            }
        }
    }
}

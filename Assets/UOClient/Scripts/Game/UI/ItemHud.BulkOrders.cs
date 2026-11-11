using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Net;

namespace UOGame.UI
{
    /// <summary>
    /// The bulk order window (no server gumps): a craftsman's offer to accept or decline, a deed's contents with "Add
    /// from backpack", and the rewards a craftsman has for your banked points. The server sends what to show
    /// (<see cref="BodView"/>); the buttons go back as <see cref="BodAction"/>.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _bod;

        private void OnEnableBulkOrders() => Server.BulkOrderService.Shown += ShowBod;
        private void OnDisableBulkOrders() => Server.BulkOrderService.Shown -= ShowBod;

        private void SendBod(byte action, int vendor = 0, uint item = 0, int index = 0) =>
            _network.ClientManager.Broadcast(new BodAction { Action = action, Vendor = vendor, Item = item, Index = index });

        private void ShowBod(BodView view)
        {
            if (_bod == null)
            {
                _bod = new Window(_layer, "Bulk order", 380, 440, new Vector2(520, 200));
            }

            JObject j = string.IsNullOrEmpty(view.Json) ? new JObject() : JObject.Parse(view.Json);
            _bod.Body.Clear();
            void Line(string text, bool heading = false)
            {
                var l = new Label(text) { style = { whiteSpace = WhiteSpace.Normal } };
                if (heading)
                {
                    l.AddToClassList("stats-heading");
                }

                _bod.Body.Add(l);
            }

            void B(string text, System.Action click)
            {
                var b = new Button(click) { text = text };
                b.AddToClassList("npc-option");
                _bod.Body.Add(b);
            }

            if (view.Kind == 2)
            {
                _bod.SetTitle($"{j["Craft"]} rewards");
                double pending = (double?)j["Pending"] ?? 0;
                if (pending > 0)
                {
                    Line($"Thy order earned {pending:N0} points: choose a reward costing no more, or bank them as {(double?)j["BankAs"] ?? 0:N2} points.");
                    B("Bank the points", () => SendBod(8, view.Vendor));
                }
                else
                {
                    Line($"You have {(double?)j["Points"] ?? 0:N0} points banked for {j["Craft"]} orders.");
                }

                var list = new ScrollView { style = { flexGrow = 1 } };
                var rewards = j["Rewards"] as JArray ?? new JArray();
                for (int i = 0; i < rewards.Count; i++)
                {
                    int index = i;
                    JToken r = rewards[i];
                    string name = GameText.Get((int?)r["Cliloc"] ?? 0) ?? (string)r["Name"] ?? "a reward";
                    var b = new Button(() => SendBod(5, view.Vendor, 0, index)) { text = $"{GameText.Capital(name)}: {(int)r["Points"]:N0} points" };
                    b.AddToClassList("npc-option");
                    b.SetEnabled((bool?)r["Can"] ?? false);
                    list.Add(b);
                }

                _bod.Body.Add(list);
                _bod.Open();
                return;
            }

            bool large = (bool?)j["Large"] ?? false;
            _bod.SetTitle($"{(large ? "Large" : "Small")} {j["Craft"]} bulk order");
            string material = (string)j["Material"];
            Line($"Make {j["Quantity"]} of {(large ? "each of these" : "this")}" +
                 ((bool?)j["Exceptional"] == true ? ", exceptional" : "") +
                 (!string.IsNullOrEmpty(material) && material != "None" ? $", from {material}" : "") + ":");
            foreach (JToken e in j["Entries"] as JArray ?? new JArray())
            {
                Line($"  {GameText.Capital((string)e["Name"])}: {e["Have"]} / {j["Quantity"]}");
            }

            if (j["Gold"] != null)
            {
                Line($"Reward: about {(int)j["Gold"]:N0} gold, {(int?)j["Points"] ?? 0:N0} points toward the craftsman's rewards.");
            }

            if (view.Kind == 0)
            {
                B("Accept", () =>
                {
                    SendBod(1, view.Vendor);
                    _bod.Close();
                });
                B("Decline", () =>
                {
                    SendBod(2, view.Vendor);
                    _bod.Close();
                });
            }
            else
            {
                if ((bool?)j["Complete"] == true)
                {
                    Line("Complete: hand it to a craftsman of this trade.");
                }
                else
                {
                    B(large ? "Add items and filled small orders from backpack" : "Add items from backpack", () => SendBod(6, 0, view.Item));
                }
            }

            _bod.Open();
        }

        /// <summary>The bulk order options for a craftsman who hands them out (in the NPC dialogue).</summary>
        private void AddBulkOrderOptions(NpcMobile npc)
        {
            if (Content.BulkOrderRules.CraftsOfVendor(npc.Def.Type).Count == 0)
            {
                return;
            }

            _talkOptions.Add(Option("Ask for a bulk order", () => SendBod(0, npc.ObjectId)));
            _talkOptions.Add(Option("Turn in bulk orders", () => SendBod(3, npc.ObjectId)));
            _talkOptions.Add(Option("Bulk order rewards", () => SendBod(4, npc.ObjectId)));
        }
    }
}

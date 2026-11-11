using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Combat;
using UOGame.Content;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Pet windows. Pets (P): every pet following you, with its health, loyalty and current order, and buttons for UO's
    /// spoken orders (follow, stay, guard, attack my target, stop), feeding, renaming and releasing; plus a row that
    /// orders all of them at once. Stable: an animal trainer's stalls for your pets, and the pets the trainer sells.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _pets, _stable, _animalPack, _lore;
        private VisualElement _animalGrid;
        private Label _loreText;
        private int _animalPet;
        private ScrollView _petList, _stableList;
        private float _nextPets;
        private int _releaseArmed;
        private float _releaseUntil;
        private StableState _stableState;

        private void BuildPetWindows()
        {
            _pets = new Window(_layer, "Pets", 520, 470, new Vector2(60, 520));
            var all = new VisualElement();
            all.style.flexDirection = FlexDirection.Row;
            all.style.flexWrap = Wrap.Wrap;
            all.Add(new Label("All:") { style = { unityTextAlign = TextAnchor.MiddleLeft, marginRight = 6 } });
            foreach ((string label, byte order) in new[] { ("Follow", (byte)0), ("Stay", (byte)1), ("Guard", (byte)2), ("Attack", (byte)3), ("Stop", (byte)4) })
            {
                byte o = order;
                all.Add(PetButton(label, () => _network.ClientManager.Broadcast(new PetCommand { Pet = 0, Order = o })));
            }

            all.Add(PetButton("Get off my mount", () => _network.ClientManager.Broadcast(new PetCommand { Order = 15 })));
            _pets.Body.Add(all);
            _petList = new ScrollView();
            _petList.AddToClassList("list-scroll");
            _petList.style.flexGrow = 1;
            _pets.Body.Add(_petList);

            _stable = new Window(_layer, "Stable", 520, 560, new Vector2(620, 220));
            _stableList = new ScrollView();
            _stableList.AddToClassList("list-scroll");
            _stableList.style.flexGrow = 1;
            _stable.Body.Add(_stableList);

            _animalPack = new Window(_layer, "Pack", 470, 430, new Vector2(880, 260));
            _animalGrid = Grid(_animalPack.Body);
            _animalPack.Closed += () => _animalPet = 0;
            _lore = new Window(_layer, "Animal Lore", 380, 400, new Vector2(620, 160));
            _loreText = new Label { enableRichText = true };
            _loreText.style.whiteSpace = WhiteSpace.Normal;
            _lore.Body.Add(_loreText);

            PetService.StableShown -= OnStableShown;
            PetService.StableShown += OnStableShown;
            PetService.PackShown -= OnPackShown;
            PetService.PackShown += OnPackShown;
            PetService.LoreShown -= OnLoreShown;
            PetService.LoreShown += OnLoreShown;
        }

        private Button PetButton(string text, System.Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("hud-button");
            b.style.marginRight = 3;
            b.style.marginBottom = 3;
            return b;
        }

        /// <summary>The pets following the local player that this client can see.</summary>
        private static List<NpcMobile> MyPets()
        {
            var mine = new List<NpcMobile>();
            int me = PlayerMotor.Local != null ? PlayerMotor.Local.ObjectId : -1;
            foreach (NpcMobile npc in Object.FindObjectsByType<NpcMobile>())
            {
                if (npc.IsSpawned && me >= 0 && npc.IsPetOf(me))
                {
                    mine.Add(npc);
                }
            }

            mine.Sort((a, b) => a.ObjectId.CompareTo(b.ObjectId));
            return mine;
        }

        private void UpdatePets()
        {
            if (!GameInput.Typing && GameInput.Controls.Gameplay.Pets.WasPressedThisFrame())
            {
                _pets.Toggle();
                _nextPets = 0f;
            }

            if (_animalPack.IsOpen && _animalPet != 0 && MyPets().Find(p => p.ObjectId == _animalPet) is NpcMobile animal &&
                PlayerMotor.Local != null && Vector3.Distance(animal.transform.position, PlayerMotor.Local.transform.position) > 5f)
            {
                _animalPack.Close(); // walked away from it
            }

            if (_pets.IsOpen && Time.unscaledTime >= _nextPets)
            {
                _nextPets = Time.unscaledTime + 0.5f;
                RefreshPets();
            }
        }

        private void RefreshPets()
        {
            float scroll = _petList.scrollOffset.y;
            _petList.Clear();
            List<NpcMobile> pets = MyPets();
            int slots = 0;
            foreach (NpcMobile pet in pets)
            {
                slots += pet.Def != null ? Mathf.Max(1, pet.Def.ControlSlots) : 1;
                var combat = pet.GetComponent<Combatant>();
                var box = new VisualElement();
                box.AddToClassList("shop-row");
                box.style.flexDirection = FlexDirection.Column;
                box.style.height = StyleKeyword.Auto;
                box.style.paddingTop = 4;
                box.style.paddingBottom = 4;
                string kind = pet.Def != null && pet.PetName.Value.Length > 0 ? $" ({pet.Name})" : string.Empty;
                string state = combat.Dead.Value ? "<color=#a9b8d8>ghost</color>" : $"{combat.Hits.Value}/{combat.HitsMax.Value} hits";
                string[] orders = { "following", "staying", "guarding", "attacking", "stopped" };
                var title = new Label($"<b>{pet.DisplayName}</b>{kind}   {state}   ·   {PetRules.LoyaltyWord(pet.Loyalty.Value)}   ·   {orders[Mathf.Clamp(pet.Order.Value, 0, 4)]}" +
                                      (pet.Bonded.Value ? "   ·   bonded" : string.Empty)) { enableRichText = true };
                title.style.whiteSpace = WhiteSpace.Normal;
                box.Add(title);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                int id = pet.ObjectId;
                foreach ((string label, byte order) in new[] { ("Follow", (byte)0), ("Stay", (byte)1), ("Guard", (byte)2), ("Attack", (byte)3), ("Stop", (byte)4), ("Feed", (byte)12) })
                {
                    byte o = order;
                    row.Add(PetButton(label, () => _network.ClientManager.Broadcast(new PetCommand { Pet = id, Order = o })));
                }

                if (pet.Def != null && pet.Def.Mount)
                {
                    row.Add(PetButton("Ride", () => _network.ClientManager.Broadcast(new PetCommand { Pet = id, Order = 14 })));
                }

                if (pet.Def != null && pet.Def.PackAnimal)
                {
                    row.Add(PetButton("Pack", () => _network.ClientManager.Broadcast(new PetCommand { Pet = id, Order = 13 })));
                }

                var rename = new TextField { maxLength = 16, value = string.Empty };
                rename.style.width = 110;
                rename.RegisterCallback<FocusInEvent>(_ => GameInput.SetTyping(true));
                rename.RegisterCallback<FocusOutEvent>(_ => GameInput.SetTyping(false));
                row.Add(rename);
                row.Add(PetButton("Rename", () =>
                {
                    if (!string.IsNullOrWhiteSpace(rename.value))
                    {
                        _network.ClientManager.Broadcast(new PetCommand { Pet = id, Order = 11, Name = rename.value.Trim() });
                    }
                }));
                bool armed = _releaseArmed == id && Time.unscaledTime < _releaseUntil;
                row.Add(PetButton(armed ? "Really release?" : "Release", () =>
                {
                    if (_releaseArmed == id && Time.unscaledTime < _releaseUntil)
                    {
                        _network.ClientManager.Broadcast(new PetCommand { Pet = id, Order = 10 });
                        _releaseArmed = 0;
                    }
                    else
                    {
                        _releaseArmed = id;
                        _releaseUntil = Time.unscaledTime + 4f;
                    }

                    _nextPets = 0f;
                }));
                box.Add(row);
                _petList.Add(box);
            }

            if (pets.Count == 0)
            {
                _petList.Add(new Label("No pets follow you. Tame one (Animal Taming, on the Skills tab, with an animal targeted) or buy one from an animal trainer.")
                    { style = { whiteSpace = WhiteSpace.Normal } });
            }

            _pets.SetTitle($"Pets ({slots}/{PetRules.FollowersMax} followers)");
            _petList.schedule.Execute(() => _petList.scrollOffset = new Vector2(0, scroll));
        }

        // ------------------------------------------------------------------ a pack animal's pack

        private void OnPackShown(PackState state)
        {
            if (_root == null)
            {
                return;
            }

            _animalPet = state.Pet;
            NpcMobile pet = MyPets().Find(p => p.ObjectId == state.Pet);
            FillGrid(_animalGrid, state.Items ?? new ItemView[0], null, "The pack is empty.", draggable: true, animal: true);
            _animalPack.SetTitle($"{(pet != null ? pet.DisplayName : "Pack")} · {state.Weight}/{state.MaxWeight} stones");
            _animalPack.Open();
            _backpack.Open();
        }

        // ------------------------------------------------------------------ Animal Lore

        private void OnLoreShown(LoreInfo info)
        {
            if (_root == null)
            {
                return;
            }

            string tame = info.Tame >= 0f ? $"{info.Tame:0.0} to tame, {info.Slots} control slot{(info.Slots == 1 ? "" : "s")}" : "Not tamable";
            _loreText.text =
                $"<b>{info.Name}</b>\n\n" +
                $"Hits {info.Hits}/{info.HitsMax}" + (info.ManaMax > 0 ? $"   Mana {info.Mana}/{info.ManaMax}" : "") + "\n" +
                $"Strength {info.Str}   Dexterity {info.Dex}   Intelligence {info.Int}\n\n" +
                $"Resists: physical {info.Physical}%, fire {info.Fire}%, cold {info.Cold}%, poison {info.Poison}%, energy {info.Energy}%\n\n" +
                $"Wrestling {info.Wrestling:0.0}   Tactics {info.Tactics:0.0}\n\n" +
                $"{tame}\n" + (info.Pet ? $"Loyalty: {PetRules.LoyaltyWord(info.Loyalty)}\n" : "") +
                $"Eats: {info.Foods}";
            _lore.SetTitle("Animal Lore");
            _lore.Open();
        }

        // ------------------------------------------------------------------ the stable

        private void OnStableShown(StableState state)
        {
            if (_root == null)
            {
                return;
            }

            _stableState = state;
            _talk.Close();
            RefreshStable();
            _stable.Open();
        }

        private void RefreshStable()
        {
            StableState s = _stableState;
            _stableList.Clear();
            _stableList.Add(StableHeading($"Stabled ({(s.Stabled?.Length ?? 0)}/{s.Stalls} stalls)"));
            for (int i = 0; i < (s.Stabled?.Length ?? 0); i++)
            {
                StabledPet p = s.Stabled[i];
                int index = i;
                string kind = SpawnService.Content?.Kind(p.Kind) is CreatureDef def ? SpawnService.Content.Resolve(def, p.Seed).Name : "a pet";
                _stableList.Add(StableRow(p.Name.Length > 0 ? $"{p.Name} ({kind})" : kind, "Claim",
                    () => _network.ClientManager.Broadcast(new StableRequest { Trainer = s.Trainer, Action = 2, Index = index })));
            }

            _stableList.Add(StableHeading($"Your pets here (stabling costs {s.Fee} gold each)"));
            foreach (NpcMobile pet in MyPets())
            {
                int id = pet.ObjectId;
                _stableList.Add(StableRow(pet.DisplayName, "Stable",
                    () => _network.ClientManager.Broadcast(new StableRequest { Trainer = s.Trainer, Action = 1, Pet = id })));
            }

            _stableList.Add(StableHeading("For sale"));
            for (int i = 0; i < (s.ForSale?.Length ?? 0); i++)
            {
                PetForSale p = s.ForSale[i];
                int index = i;
                CreatureDef def = SpawnService.Content?.Kind(p.Kind);
                string name = def != null ? SpawnService.Content.Resolve(def, 1).Name : "a pet";
                _stableList.Add(StableRow($"{name}   <color=#e8c66a>{p.Price:N0} gold</color>", "Buy",
                    () => _network.ClientManager.Broadcast(new StableRequest { Trainer = s.Trainer, Action = 3, Index = index })));
            }
        }

        private static Label StableHeading(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 8;
            return l;
        }

        private VisualElement StableRow(string text, string action, System.Action click)
        {
            var row = new VisualElement();
            row.AddToClassList("shop-row");
            var label = new Label(text) { enableRichText = true };
            label.AddToClassList("shop-name");
            row.Add(label);
            row.Add(PetButton(action, click));
            return row;
        }
    }
}

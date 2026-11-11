using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UOGame.Net;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// The crafting window, opened by double-clicking a tool: the system's groups down the side, the chosen group's
    /// recipes with your chance to make each (and to make it exceptional) and what each takes, a material picker for
    /// systems that work materials, and Make. Names come from the player's UO text (cliloc), or the item's name.
    /// Chances are worked out here from your skills with the same rules the server uses; the server decides.
    /// </summary>
    public sealed partial class ItemHud
    {
        private Window _craft;
        private VisualElement _craftGroups;
        private ScrollView _craftList;
        private DropdownField _craftMaterial;
        private int _craftSystem = -1, _craftGroup;

        private void BuildCraftWindow()
        {
            _craft = new Window(_layer, "Crafting", 760, 600, new Vector2(600, 160));
            var top = new VisualElement();
            top.AddToClassList("menu-row");
            _craftMaterial = new DropdownField("Material", new List<string>(), 0);
            _craftMaterial.style.flexGrow = 1;
            _craftMaterial.RegisterValueChangedCallback(_ => RefreshCraft());
            top.Add(_craftMaterial);
            _craft.Body.Add(top);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1;
            var groupScroll = new ScrollView();
            groupScroll.style.width = 190;
            _craftGroups = new VisualElement();
            groupScroll.Add(_craftGroups);
            row.Add(groupScroll);
            _craftList = new ScrollView();
            _craftList.AddToClassList("list-scroll");
            _craftList.style.flexGrow = 1;
            row.Add(_craftList);
            _craft.Body.Add(row);
            CraftService.OpenWindow += OpenCraft;
            SkillService.Changed += _ =>
            {
                if (_craft.IsOpen)
                {
                    RefreshCraft();
                }
            };
        }

        private void OpenCraft(int system)
        {
            if (_root == null || system < 0 || system >= CraftService.Systems.Count)
            {
                return;
            }

            CraftService.CraftSystem s = CraftService.Systems[system];
            if (_craftSystem != system)
            {
                _craftSystem = system;
                _craftGroup = 0;
                var materials = new List<string>();
                foreach (CraftService.Material m in s.Materials)
                {
                    string menu = Cliloc(m.MenuName);
                    string label = menu == null || menu.Contains("~") ? System.Text.RegularExpressions.Regex.Replace(m.Name, "(?<=[a-z])(?=[A-Z])", " ") : menu;
                    materials.Add($"{label} ({m.ReqSkill:0} skill)");
                }

                _craftMaterial.choices = materials;
                _craftMaterial.index = materials.Count > 0 ? 0 : -1;
                _craftMaterial.style.display = materials.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _craftGroups.Clear();
                for (int g = 0; g < s.Groups.Count; g++)
                {
                    if (!s.Recipes.Exists(r => r.Group == g))
                    {
                        continue;
                    }

                    int group = g;
                    var b = new Button(() =>
                    {
                        _craftGroup = group;
                        RefreshCraft();
                    }) { text = Cliloc(s.Groups[g].Cliloc) ?? s.Groups[g].Text ?? $"Group {g + 1}" };
                    b.AddToClassList("tab");
                    b.style.width = 180;
                    b.userData = group;
                    _craftGroups.Add(b);
                }

                foreach (VisualElement child in _craftGroups.Children())
                {
                    _craftGroup = (int)child.userData;
                    break;
                }
            }

            _craft.SetTitle(SystemTitle(s));
            RefreshCraft();
            _craft.Open();
        }

        private static string SystemTitle(CraftService.CraftSystem s) =>
            System.Text.RegularExpressions.Regex.Replace(s.Name, "(?<=[a-z])(?=[A-Z])", " ");

        /// <summary>The recipes of the chosen group, with chances and needs for the chosen material.</summary>
        private void RefreshCraft()
        {
            if (_craftSystem < 0)
            {
                return;
            }

            CraftService.CraftSystem s = CraftService.Systems[_craftSystem];
            CraftService.Material material = _craftMaterial.index >= 0 && _craftMaterial.index < s.Materials.Count ? s.Materials[_craftMaterial.index] : null;
            foreach (VisualElement child in _craftGroups.Children())
            {
                child.EnableInClassList("tab-active", (int)child.userData == _craftGroup);
            }

            var skills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillView v in SkillService.Mine.Skills ?? Array.Empty<SkillView>())
            {
                Content.SkillDef def = Content.CreationRules.Skills.Find(d => d.Id == v.Skill);
                if (def != null)
                {
                    skills[def.Key] = v.Value / 10.0;
                }
            }

            double Skill(string key) => skills.TryGetValue(key, out double value) ? value : 0;
            float scroll = _craftList.scrollOffset.y;
            _craftList.Clear();
            for (int i = 0; i < s.Recipes.Count; i++)
            {
                CraftService.Recipe recipe = s.Recipes[i];
                if (recipe.Group != _craftGroup)
                {
                    continue;
                }

                double chance = CraftService.SuccessChance(s, recipe, Skill);
                double exceptional = CraftService.ExceptionalChance(s, recipe, chance, Skill(s.Skill));
                var row = new VisualElement();
                row.AddToClassList("shop-row");
                row.style.height = StyleKeyword.Auto;
                row.style.minHeight = 44;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                if (chance <= 0)
                {
                    row.AddToClassList("skill-zero");
                }

                string name = Cliloc(recipe.NameCliloc) ?? ItemNames.Name(ItemService.Catalog?.Find(recipe.Type));
                var needs = new List<string>();
                foreach (var line in recipe.Resources)
                {
                    needs.Add($"{line.Amount} {ItemNames.Name(ItemService.Catalog?.Find(CraftService.ResourceType(line, material))).ToLowerInvariant()}");
                }

                var label = new Label($"{name}\n<size=12>{string.Join(", ", needs)}</size>") { enableRichText = true };
                label.AddToClassList("shop-name");
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);
                var odds = new Label(chance > 0 ? $"{chance:P0}\n<size=12>exc. {exceptional:P0}</size>" : $"needs {recipe.MinSkill:0.0}") { enableRichText = true };
                odds.AddToClassList("shop-price");
                row.Add(odds);
                int index = i;
                var make = new Button(() => _network.ClientManager.Broadcast(new CraftRequest
                {
                    System = (byte)_craftSystem,
                    Recipe = (ushort)index,
                    Material = (byte)Mathf.Max(0, _craftMaterial.index),
                })) { text = "Make" };
                make.AddToClassList("hud-button");
                make.SetEnabled(chance > 0);
                row.Add(make);
                _craftList.Add(row);
            }

            _craftList.schedule.Execute(() => _craftList.scrollOffset = new Vector2(0, scroll));
        }

        /// <summary>A text from the player's UO text file (cliloc), or null.</summary>
        private static string Cliloc(int number)
        {
            return GameText.Capital(GameText.Get(number));
        }
    }
}

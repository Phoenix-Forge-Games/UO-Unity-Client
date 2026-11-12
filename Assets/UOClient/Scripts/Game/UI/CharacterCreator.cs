using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UOClient;
using UOGame.Content;
using UOGame.Net;

namespace UOGame.UI
{
    /// <summary>
    /// Making a character: a live preview you can turn, and the choices beside it: name, sex, skin, hair and beard
    /// (styles and colours from our content's rules), and a profession (ready-made stats, skills and kit) or your own
    /// stats and starting skills. It checks the same rules the server does, so Create is only offered when they hold;
    /// the server checks again and says why if it refuses.
    /// Embedded (the main menu): it fills a panel the menu gives it, with no preview or buttons of its own; the menu
    /// shows the look on a character in the scene (<see cref="LookChanged"/>) and submits (<see cref="Submit"/>) once
    /// <see cref="Valid"/>.
    /// </summary>
    public sealed class CharacterCreator
    {
        private readonly VisualElement _overlay, _menu;
        private readonly Action<CreateCharacter> _send;
        private readonly PaperdollStage _stage;
        private readonly GameObject _holder;
        private readonly bool _embedded;
        private readonly TextField _name;
        private readonly Label _problem, _statTotal, _skillTotal;
        private readonly VisualElement _custom, _beardRows, _professions;
        private readonly Dictionary<int, Button> _professionButtons = new Dictionary<int, Button>();
        private readonly Button _male, _female, _create, _human, _gargoyleChoice;
        private readonly VisualElement _hairBlock;
        private readonly SliderInt _skin, _hairHue, _beardHue, _str, _dex, _int;
        private readonly Label _hairName, _beardName;
        private readonly List<(DropdownField Skill, SliderInt Value)> _skillRows = new List<(DropdownField, SliderInt)>();

        private bool _isFemale, _isGargoyle;
        private int _hairIndex, _beardIndex;
        private int _profession;
        private int _city;
        private VisualElement _cities;
        private readonly Dictionary<int, Button> _cityButtons = new Dictionary<int, Button>();
        private bool _balancing;
        private string _built;

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        /// <summary>Do the choices hold (a name, and stats and skills within the rules)?</summary>
        public bool Valid { get; private set; }

        /// <summary>Embedded: the look changed (dressed as the character would start).</summary>
        public event Action<Look> LookChanged;

        /// <summary>Embedded: <see cref="Valid"/> changed.</summary>
        public event Action<bool> ValidityChanged;

        public CharacterCreator(VisualElement screen, Action<CreateCharacter> send, bool embedded = false)
        {
            _send = send;
            _embedded = embedded;
            _overlay = new VisualElement();
            _overlay.AddToClassList(embedded ? "creator-embedded" : "creator");
            _overlay.style.display = DisplayStyle.None;
            screen.Add(_overlay);

            if (!embedded)
            {
                _stage = new PaperdollStage();
                _holder = new GameObject("Creation Preview");
                _holder.transform.position = new Vector3(0f, -2100f, 0f);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _menu = screen.Q(className: "panel");
                var doll = new VisualElement();
                doll.AddToClassList("creator-doll");
                doll.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
                doll.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if ((e.pressedButtons & 1) != 0)
                    {
                        _stage.Turn(-e.deltaPosition.x * 0.8f);
                    }
                });
                _overlay.Add(doll);
            }

            var side = new ScrollView();
            side.AddToClassList(embedded ? "creator-side-embedded" : "creator-side");
            _overlay.Add(side);

            side.Add(Heading("New character"));
            _name = new TextField("Name") { maxLength = 16 };
            _name.AddToClassList("field");
            _name.RegisterCallback<FocusInEvent>(_ => GameInput.SetTyping(true));
            _name.RegisterCallback<FocusOutEvent>(_ => GameInput.SetTyping(false));
            _name.RegisterValueChangedCallback(_ => Validate());
            side.Add(_name);

            side.Add(Heading("Look"));
            var raceRow = Row();
            _human = Choice("Human", () => SetRace(false));
            _gargoyleChoice = Choice("Gargoyle", () => SetRace(true));
            raceRow.Add(_human);
            raceRow.Add(_gargoyleChoice);
            side.Add(Labelled("Race", raceRow));

            var sexRow = Row();
            _male = Choice("Male", () => SetSex(false));
            _female = Choice("Female", () => SetSex(true));
            sexRow.Add(_male);
            sexRow.Add(_female);
            side.Add(Labelled("Sex", sexRow));

            (int skinMin, int skinCount) = Outfits.HueRange("Skin");
            _skin = HueSlider(skinMin, skinCount);
            side.Add(Labelled("Skin", _skin));

            _hairBlock = new VisualElement();
            side.Add(_hairBlock);
            var hairRow = Row();
            hairRow.Add(Choice("◀", () => StepHair(-1)));
            _hairName = new Label();
            _hairName.AddToClassList("creator-style");
            hairRow.Add(_hairName);
            hairRow.Add(Choice("▶", () => StepHair(+1)));
            _hairBlock.Add(Labelled("Hair", hairRow));
            (int hairMin, int hairCount) = Outfits.HueRange("Hair");
            _hairHue = HueSlider(hairMin, hairCount);
            _hairBlock.Add(Labelled("Hair colour", _hairHue));

            _beardRows = new VisualElement();
            var beardRow = Row();
            beardRow.Add(Choice("◀", () => StepBeard(-1)));
            _beardName = new Label();
            _beardName.AddToClassList("creator-style");
            beardRow.Add(_beardName);
            beardRow.Add(Choice("▶", () => StepBeard(+1)));
            _beardRows.Add(Labelled("Beard", beardRow));
            _beardHue = HueSlider(hairMin, hairCount);
            _beardRows.Add(Labelled("Beard colour", _beardHue));
            _hairBlock.Add(_beardRows);

            side.Add(Heading("Profession"));
            _professions = new VisualElement();
            _professions.AddToClassList("creator-professions");
            side.Add(_professions);

            side.Add(Heading("Begin in"));
            _cities = new VisualElement();
            _cities.AddToClassList("creator-professions");
            side.Add(_cities);

            _custom = new VisualElement();
            side.Add(_custom);
            _custom.Add(Heading($"Stats ({CreationRules.StatTotal} in all)"));
            _str = StatSlider();
            _dex = StatSlider();
            _int = StatSlider();
            _custom.Add(Labelled("Strength", _str));
            _custom.Add(Labelled("Dexterity", _dex));
            _custom.Add(Labelled("Intelligence", _int));
            _statTotal = new Label();
            _statTotal.AddToClassList("creator-note");
            _custom.Add(_statTotal);
            _custom.Add(Heading($"Skills (up to {CreationRules.SkillMaxCount}, at most {CreationRules.SkillMaxEach} each)"));
            var skillNames = new List<string> { "—" };
            skillNames.AddRange(CreationRules.Skills.Where(CreationRules.Pickable).Select(s => s.Name).OrderBy(n => n));
            for (int i = 0; i < CreationRules.SkillMaxCount; i++)
            {
                var skill = new DropdownField(skillNames, 0);
                skill.AddToClassList("creator-skill");
                var value = new SliderInt(0, CreationRules.SkillMaxEach) { showInputField = true };
                value.AddToClassList("creator-skill-value");
                skill.RegisterValueChangedCallback(_ => Validate());
                value.RegisterValueChangedCallback(_ => Validate());
                var row = Row();
                row.Add(skill);
                row.Add(value);
                _custom.Add(row);
                _skillRows.Add((skill, value));
            }

            _skillTotal = new Label();
            _skillTotal.AddToClassList("creator-note");
            _custom.Add(_skillTotal);

            _problem = new Label();
            _problem.AddToClassList("status");
            side.Add(_problem);
            var buttons = Row();
            _create = new Button(Create) { text = "Create" };
            _create.AddToClassList("primary-button");
            _create.style.flexGrow = 1;
            var cancel = new Button(Close) { text = "Cancel" };
            cancel.AddToClassList("secondary-button");
            buttons.Add(_create);
            buttons.Add(cancel);
            if (!embedded)
            {
                side.Add(buttons); // embedded: the menu has its own Start Game and Back
            }

            foreach (SliderInt slider in new[] { _skin, _hairHue, _beardHue })
            {
                slider.RegisterValueChangedCallback(_ => Validate());
            }

            _str.RegisterValueChangedCallback(e => Balance(_str, e.previousValue, e.newValue));
            _dex.RegisterValueChangedCallback(e => Balance(_dex, e.previousValue, e.newValue));
            _int.RegisterValueChangedCallback(e => Balance(_int, e.previousValue, e.newValue));
        }

        // ------------------------------------------------------------------ open / close

        public void Open(string name)
        {
            BuildProfessions();
            BuildCities();
            var random = new System.Random();
            _name.value = name ?? string.Empty;
            _isFemale = random.Next(2) == 0;
            SetRace(false);
            _skin.value = _skin.lowValue + random.Next(_skin.highValue - _skin.lowValue + 1);
            _hairHue.value = _hairHue.lowValue + random.Next(_hairHue.highValue - _hairHue.lowValue + 1);
            _beardHue.value = _hairHue.value;
            _hairIndex = 1 + random.Next(Math.Max(1, Outfits.HairStyles(_isFemale).Count));
            _beardIndex = 0;
            Profession first = CreationRules.Professions.Find(p => !p.Custom);
            SetProfession(first != null ? first.Id : 0);
            SetSex(_isFemale);
            _overlay.style.display = DisplayStyle.Flex;
            if (_menu != null)
            {
                _menu.style.display = DisplayStyle.None;
            }

            if (_stage != null)
            {
                _stage.Showing = true;
            }
        }

        public void Close()
        {
            _overlay.style.display = DisplayStyle.None;
            if (_menu != null)
            {
                _menu.style.display = DisplayStyle.Flex;
            }

            if (_stage != null)
            {
                _stage.Showing = false;
            }

            GameInput.SetTyping(false);
        }

        /// <summary>The server refused (name taken, rules): say why and stay open.</summary>
        public void Refused(string reason) => _problem.text = reason;

        public void Dispose()
        {
            _stage?.Dispose();
            if (_holder != null)
            {
                UnityEngine.Object.Destroy(_holder);
            }
        }

        /// <summary>Embedded: make the character (when the choices hold).</summary>
        public void Submit()
        {
            if (Valid)
            {
                Create();
            }
        }

        // ------------------------------------------------------------------ choices

        /// <summary>Human or gargoyle: gargoyles have their own skin colours (stone greys) and no hair or beard.</summary>
        private void SetRace(bool gargoyle)
        {
            _isGargoyle = gargoyle;
            _human.EnableInClassList("creator-chosen", !gargoyle);
            _gargoyleChoice.EnableInClassList("creator-chosen", gargoyle);
            (int min, int count) = Outfits.HueRange(gargoyle ? "GargoyleSkin" : "Skin");
            if (count > 0)
            {
                _skin.lowValue = min;
                _skin.highValue = min + count - 1;
                _skin.SetValueWithoutNotify(Mathf.Clamp(_skin.value, min, min + count - 1));
            }

            _hairBlock.style.display = gargoyle ? DisplayStyle.None : DisplayStyle.Flex;
            Validate();
        }

        private void SetSex(bool female)
        {
            _isFemale = female;
            _male.EnableInClassList("creator-chosen", !female);
            _female.EnableInClassList("creator-chosen", female);
            int count = Outfits.HairStyles(female).Count;
            _hairIndex = Mathf.Clamp(_hairIndex, 0, count);
            _beardRows.style.display = female ? DisplayStyle.None : DisplayStyle.Flex;
            Validate();
        }

        /// <summary>Hair: 0 is bald, then the content's styles in order.</summary>
        private void StepHair(int delta)
        {
            int count = Outfits.HairStyles(_isFemale).Count + 1;
            _hairIndex = ((_hairIndex + delta) % count + count) % count;
            Validate();
        }

        private void StepBeard(int delta)
        {
            int count = Outfits.BeardStyles().Count + 1;
            _beardIndex = ((_beardIndex + delta) % count + count) % count;
            Validate();
        }

        private int Hair => _isGargoyle || _hairIndex == 0 ? 0 : Outfits.HairStyles(_isFemale)[_hairIndex - 1];
        private int Beard => _isGargoyle || _isFemale || _beardIndex == 0 ? 0 : Outfits.BeardStyles()[_beardIndex - 1];

        private void BuildProfessions()
        {
            _professions.Clear();
            _professionButtons.Clear();
            foreach (Profession p in CreationRules.Professions.Where(p => !p.Custom))
            {
                int id = p.Id;
                var b = Choice(p.Name, () => SetProfession(id));
                _professionButtons[id] = b;
                _professions.Add(b);
            }

            var custom = Choice(CreationRules.CustomName, () => SetProfession(0));
            _professionButtons[0] = custom;
            _professions.Add(custom);
        }

        /// <summary>The starting cities (Creation.json StartCities), one button each; the default chosen.</summary>
        private void BuildCities()
        {
            _cities.Clear();
            _cityButtons.Clear();
            for (int i = 0; i < CreationRules.StartCities.Count; i++)
            {
                int index = i;
                CreationRules.StartCity c = CreationRules.StartCities[i];
                var b = Choice(c.Name, () => SetCity(index));
                b.tooltip = c.Place;
                _cityButtons[i] = b;
                _cities.Add(b);
            }

            SetCity(CreationRules.DefaultCity);
        }

        private void SetCity(int index)
        {
            _city = index;
            foreach (KeyValuePair<int, Button> pair in _cityButtons)
            {
                pair.Value.EnableInClassList("creator-chosen", pair.Key == index);
            }
        }

        private void SetProfession(int id)
        {
            _profession = id;
            foreach (KeyValuePair<int, Button> pair in _professionButtons)
            {
                pair.Value.EnableInClassList("creator-chosen", pair.Key == id);
            }

            Profession p = CreationRules.FindProfession(id);
            _custom.style.display = p == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (p != null)
            {
                _balancing = true;
                _str.SetValueWithoutNotify(p.Str);
                _dex.SetValueWithoutNotify(p.Dex);
                _int.SetValueWithoutNotify(p.Int);
                _balancing = false;
                for (int i = 0; i < _skillRows.Count; i++)
                {
                    bool has = i < p.Skills.Count;
                    _skillRows[i].Skill.SetValueWithoutNotify(has ? CreationRules.Skill(p.Skills[i].Skill)?.Name ?? "—" : "—");
                    _skillRows[i].Value.SetValueWithoutNotify(has ? p.Skills[i].Value : 0);
                }
            }

            Validate();
        }

        /// <summary>Keep the stats adding up: the other two give or take what the moved one changed.</summary>
        private void Balance(SliderInt moved, int before, int after)
        {
            if (_balancing)
            {
                return;
            }

            _balancing = true;
            SliderInt[] others = new[] { _str, _dex, _int }.Where(s => s != moved).ToArray();
            int excess = _str.value + _dex.value + _int.value - CreationRules.StatTotal;
            for (int pass = 0; pass < 2 && excess != 0; pass++)
            {
                foreach (SliderInt other in others.OrderByDescending(o => excess > 0 ? o.value : -o.value))
                {
                    int target = Mathf.Clamp(other.value - excess, CreationRules.StatMin, CreationRules.StatMax);
                    excess -= other.value - target;
                    other.SetValueWithoutNotify(target);
                    if (excess == 0)
                    {
                        break;
                    }
                }
            }

            if (excess != 0)
            {
                moved.SetValueWithoutNotify(moved.value - excess); // nothing left to give: the moved one stops short
            }

            _balancing = false;
            Validate();
        }

        private List<(string, int)> ChosenSkills()
        {
            var skills = new List<(string, int)>();
            foreach ((DropdownField skill, SliderInt value) in _skillRows)
            {
                SkillDef def = CreationRules.Skills.Find(s => s.Name == skill.value);
                if (def != null && value.value > 0)
                {
                    skills.Add((def.Key, value.value));
                }
            }

            return skills;
        }

        /// <summary>Check the choices against the rules, update the labels and preview, and offer Create when they hold.</summary>
        private void Validate()
        {
            _hairName.text = Outfits.StyleName(Hair);
            _beardName.text = Outfits.StyleName(Beard);
            int stats = _str.value + _dex.value + _int.value;
            _statTotal.text = $"{stats} of {CreationRules.StatTotal}";
            List<(string, int)> skills = ChosenSkills();
            _skillTotal.text = $"{skills.Sum(s => s.Item2)} points (allowed: {string.Join(" or ", CreationRules.SkillTotals)})";

            string problem = null;
            string name = _name.value?.Trim() ?? string.Empty;
            if (name.Length < 2)
            {
                problem = "Give your character a name.";
            }
            else if (_profession == 0)
            {
                problem = CreationRules.CheckStats(_str.value, _dex.value, _int.value) ?? CreationRules.CheckSkills(skills);
            }

            _problem.text = problem ?? string.Empty;
            _create.SetEnabled(problem == null);
            if (Valid != (problem == null))
            {
                Valid = problem == null;
                ValidityChanged?.Invoke(Valid);
            }

            Preview();
        }

        private Look CurrentLook() => new Look
        {
            Seed = 1,
            Female = _isFemale,
            Gargoyle = _isGargoyle,
            Skin = (ushort)_skin.value,
            Hair = (ushort)Hair,
            HairHue = (ushort)_hairHue.value,
            Beard = (ushort)Beard,
            BeardHue = (ushort)_beardHue.value,
        };

        /// <summary>Dress the preview as the character would start: the profession's gear and the everyday clothes.</summary>
        private void Preview()
        {
            Look look = CurrentLook();
            Profession p = CreationRules.FindProfession(_profession);
            var gear = new List<Gear>();
            if (p != null)
            {
                gear.AddRange(Outfits.RollItems(p.Equip, _isFemale, 1));
            }

            if (p == null || !p.SkipSkillItems)
            {
                foreach ((string skill, int value) in p != null ? p.Skills : ChosenSkills())
                {
                    if (value > 0 && CreationRules.SkillItems[skill]?["Equip"] is Newtonsoft.Json.Linq.JArray equip)
                    {
                        gear.AddRange(Outfits.RollItems(equip, _isFemale, 1));
                    }
                }
            }

            if (CreationRules.ClothesFor.Contains(_profession))
            {
                gear.AddRange(Outfits.RollItems(CreationRules.Clothes, _isFemale, 1));
            }
            var bySlot = new Dictionary<Items.WearSlot, Gear>();
            foreach (Gear g in gear)
            {
                Items.ItemDef def = Server.ItemService.Catalog?.Get(g.Item);
                if (def != null && def.Wearable && !bySlot.ContainsKey(def.Slot))
                {
                    bySlot[def.Slot] = g;
                }
            }

            look.Gear = bySlot.Values.ToArray();
            string signature = $"{look.Female}|{look.Gargoyle}|{look.Skin}|{look.Hair}|{look.HairHue}|{look.Beard}|{look.BeardHue}|{_profession}|{string.Join(",", ChosenSkills())}";
            if (_embedded)
            {
                if (signature != _built)
                {
                    _built = signature;
                    LookChanged?.Invoke(look);
                }

                return;
            }

            if (signature != _built && GameLook.Dresser != null)
            {
                _built = signature;
                foreach (Transform child in _holder.transform)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }

                GameLook.Dresser.Build(Looks.ToMobile(look, Server.ItemService.Catalog), _holder.transform);
            }

            _stage.Update(signature, _holder.transform);
        }

        /// <summary>Called every frame while open (the preview catches up after a rebuild).</summary>
        public void Tick()
        {
            if (IsOpen && _stage != null)
            {
                _stage.Update(_built, _holder.transform);
            }
        }

        private void Create()
        {
            var skills = ChosenSkills();
            _send(new CreateCharacter
            {
                Name = _name.value.Trim(),
                Look = CurrentLook(),
                Profession = _profession,
                Str = _str.value,
                Dex = _dex.value,
                Int = _int.value,
                Skills = skills.Select(s => new SkillPick { Skill = s.Item1, Value = s.Item2 }).ToArray(),
                City = _city,
            });
        }

        // ------------------------------------------------------------------ building blocks

        private static Label Heading(string text)
        {
            var l = new Label(text);
            l.AddToClassList("page-heading");
            return l;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("creator-row");
            return row;
        }

        private static Button Choice(string text, Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("secondary-button");
            b.AddToClassList("creator-choice");
            return b;
        }

        private static VisualElement Labelled(string label, VisualElement control)
        {
            var row = Row();
            var l = new Label(label);
            l.AddToClassList("creator-label");
            row.Add(l);
            control.style.flexGrow = 1;
            row.Add(control);
            return row;
        }

        private static SliderInt HueSlider(int min, int count) =>
            new SliderInt(min, Math.Max(min, min + count - 1)) { value = min };

        private static SliderInt StatSlider() =>
            new SliderInt(CreationRules.StatMin, CreationRules.StatMax) { value = CreationRules.StatTotal / 3, showInputField = true };
    }
}

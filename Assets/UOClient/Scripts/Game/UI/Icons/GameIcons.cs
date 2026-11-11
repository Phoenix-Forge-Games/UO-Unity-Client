using UnityEngine;
using UnityEngine.UIElements;
using UOClient.Content;
using UOGame.Server;

namespace UOGame.UI
{
    /// <summary>
    /// Skill, spell and weapon-ability icons in the UI. The art is chosen in Content/Icons.json and lives in the
    /// secured icon group (Tools/Icons/place_icons.py, Docs/SecureArt.md); this only knows the addresses:
    /// icon/skill/&lt;skill key&gt;, icon/spell/&lt;spell or racial ability name&gt;, icon/ability/&lt;weapon ability name&gt;.
    /// Every icon sits in the same rounded frame as the hotbar and bag slots.
    /// </summary>
    public static class GameIcons
    {
        public const string AbilityCategory = "ability";

        /// <summary>The hotbar and item slots' frame (Hud.uss .hotbar-slot / .item-tile).</summary>
        public static readonly Color FrameColour = new Color32(80, 66, 44, 255);
        public const float FrameRadius = 5f;

        public static string Skill(string skillKey) => string.IsNullOrEmpty(skillKey) ? null : SecureArtAddresses.SkillIcon(skillKey);

        public static string Spell(SpellService.SpellDef spell) => spell == null ? null : SecureArtAddresses.SpellIcon(spell.Name);

        public static string Spell(int spellId) => Spell(SpellService.Find(spellId));

        public static string Ability(string abilityName) => string.IsNullOrEmpty(abilityName) ? null : SecureArtAddresses.Icon(AbilityCategory, abilityName);

        /// <summary>A square icon in the slot frame: black behind, 1px border, rounded corners.</summary>
        public static VisualElement Frame(float size)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ui-icon");
            icon.style.width = size;
            icon.style.height = size;
            icon.style.flexShrink = 0;
            Style(icon);
            return icon;
        }

        /// <summary>Gives an element the icon frame (for an icon that fills a slot rather than having a fixed size).</summary>
        public static void Style(VisualElement icon)
        {
            icon.style.backgroundColor = Color.black;
            icon.style.borderTopWidth = icon.style.borderBottomWidth = icon.style.borderLeftWidth = icon.style.borderRightWidth = 1;
            icon.style.borderTopColor = icon.style.borderBottomColor = icon.style.borderLeftColor = icon.style.borderRightColor = FrameColour;
            icon.style.borderTopLeftRadius = icon.style.borderTopRightRadius = icon.style.borderBottomLeftRadius = icon.style.borderBottomRightRadius = FrameRadius;
            icon.style.overflow = Overflow.Hidden;
            icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        }

        /// <summary>A framed icon for an address, sized <paramref name="size"/>.</summary>
        public static VisualElement Make(string address, float size)
        {
            VisualElement icon = Frame(size);
            Show(icon, address, null);
            return icon;
        }

        /// <summary>
        /// Shows the icon at <paramref name="address"/> on <paramref name="icon"/>. When the build has no such icon
        /// (no art installed), <paramref name="fallback"/> (usually the name) is shown instead and the frame is hidden;
        /// once it loads, the fallback is hidden. The latest call on an element wins.
        /// </summary>
        public static void Show(VisualElement icon, string address, VisualElement fallback)
        {
            if (icon == null)
            {
                return;
            }

            icon.userData = address;
            if (string.IsNullOrEmpty(address))
            {
                icon.style.display = DisplayStyle.None;
                icon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                SetDisplay(fallback, true);
                return;
            }

            SecureArt.Load<Texture2D>(address, texture =>
            {
                if (!Equals(icon.userData, address))
                {
                    return; // given another icon meanwhile
                }

                bool have = texture != null;
                icon.style.backgroundImage = have ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
                icon.style.display = have ? DisplayStyle.Flex : DisplayStyle.None;
                SetDisplay(fallback, !have);
            });
        }

        private static void SetDisplay(VisualElement element, bool shown)
        {
            if (element != null)
            {
                element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}

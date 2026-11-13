using System.Text;

namespace UOClient.Content
{
    /// <summary>
    /// The address, label and group names of the paid art the game loads through <see cref="SecureArt"/>.
    /// They are the contract between the game code, Tools/SecureArt/SetupSecureArt.eval.cs (which makes the
    /// Addressables entries) and anything that adds art (the icons work). See Docs/SecureArt.md.
    /// </summary>
    public static class SecureArtAddresses
    {
        // ---- Groups. Every group whose name starts with ProtectedPrefix is encrypted and never public.

        public const string ProtectedPrefix = "Protected_";
        /// <summary>Project-owned catalogs that point into the paid packs (loaded by address, preloaded).</summary>
        public const string CatalogGroup = "Protected_Catalogs";
        /// <summary>Paid assets the catalogs use, one group per source pack: "Protected_Pack_" + pack folder name.</summary>
        public const string PackGroupPrefix = "Protected_Pack_";
        /// <summary>Every icon under <see cref="IconFolder"/>.</summary>
        public const string IconGroup = "Protected_Icons";

        // ---- Labels.

        /// <summary>Loaded at startup, so <see cref="SecureArt.Resolve{T}"/> never waits on disk for them.</summary>
        public const string PreloadLabel = "preload";
        public const string IconLabel = "icons";
        /// <summary>Per icon category: "icons-skill", "icons-spell", "icons-item", "icons-prompt", ...</summary>
        public static string IconCategoryLabel(string category) => IconLabel + "-" + Slug(category);

        // ---- Catalog addresses (one asset each, in CatalogGroup).

        public const string CreatureCatalog = "catalog/creatures";
        public const string SidekickWardrobe = "catalog/wardrobe";
        public const string BoatCatalog = "catalog/boats";
        public const string HouseKit = "catalog/house-kit";
        public const string PropCatalog = "catalog/props";
        public const string VegetationCatalog = "catalog/vegetation";
        public const string WallKit = "catalog/wall-kit";
        public const string WorldContent = "catalog/world-content";
        public const string WorldPalette = "catalog/world-palette";
        /// <summary>The fixed Synty human used when a body has no look (and before the wardrobe is ready).</summary>
        public const string HumanModel = "art/human/model";
        public const string HumanAnimator = "art/human/animator";
        /// <summary>The pouch drawn for items lying on the ground (GroundItem).</summary>
        public const string GroundPouch = "art/item/pouch";

        // ---- Icons. Files under IconFolder get the address "icon/<category>/<file name>" (lowercase, no
        // extension), the labels IconLabel and IconCategoryLabel(category), and go in IconGroup.

        /// <summary>Drop icons here as &lt;category&gt;/&lt;slug&gt;.png; the folder is git-ignored like all of Assets/Secure.</summary>
        public const string IconFolder = "Assets/Secure/Icons";

        public const string SkillCategory = "skill";
        public const string SpellCategory = "spell";
        public const string ItemCategory = "item";
        public const string PromptCategory = "prompt";

        public static string Icon(string category, string name) => "icon/" + Slug(category) + "/" + Slug(name);

        /// <summary>A skill's icon, by the Skills.json Key (e.g. "AnimalLore" → icon/skill/animallore).</summary>
        public static string SkillIcon(string skillKey) => Icon(SkillCategory, skillKey);

        /// <summary>A spell's icon, by the spell's Name (e.g. "Magic Arrow" → icon/spell/magic-arrow), not its int Id.</summary>
        public static string SpellIcon(string spellName) => Icon(SpellCategory, spellName);

        /// <summary>
        /// An item's chosen icon, by its Items.json Type (e.g. "IronIngot" → icon/item/ironingot; picked in
        /// Content/Icons.json "item"). Items without one keep the picture rendered from their model (ItemIcons).
        /// </summary>
        public static string ItemIcon(string itemId) => Icon(ItemCategory, itemId);

        /// <summary>
        /// A keyboard/mouse/gamepad prompt: device is the Input System layout family ("keyboard", "mouse",
        /// "gamepad"), control the control's name in its path ("space", "leftButton", "buttonSouth").
        /// File: Assets/Secure/Icons/prompt/keyboard-space.png → icon/prompt/keyboard-space.
        /// </summary>
        public static string PromptIcon(string device, string control) => Icon(PromptCategory, device + "-" + control);

        /// <summary>
        /// Lowercase; letters and digits kept, every other run of characters becomes one '-'
        /// ("Leftbutton" → "leftbutton", "Magic Arrow" → "magic-arrow", "<Keyboard>/space" → "keyboard-space").
        /// The setup script names icon addresses with the same rule, so code and files always agree.
        /// </summary>
        public static string Slug(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            bool dash = false;
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (dash && sb.Length > 0)
                    {
                        sb.Append('-');
                    }

                    sb.Append(char.ToLowerInvariant(c));
                    dash = false;
                }
                else
                {
                    dash = true;
                }
            }

            return sb.ToString();
        }
    }
}

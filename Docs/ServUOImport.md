# Importing a ServUO shard's players

Anyone running a ServUO shard can bring its players into this game: their accounts and passwords, characters,
belongings, pets and houses. There are two steps:

1. **Export** from a copy of the shard's save, with ServUO's own loader. Your custom classes deserialize exactly as
   they do on your shard.
2. **Import** into this game's server, with a config file that says how your shard's custom things map onto ours.

Both steps can be repeated as often as you like. Nothing changes on your shard.

## 1. Export (on your ServUO)

The exporter is [`Tools/ServUOExport/ExportForUnity.cs`](../Tools/ServUOExport/ExportForUnity.cs). It uses only stock
ServUO types, plus reflection for magic properties.

1. Copy it into your ServUO's `Scripts/Custom`.
2. Build your scripts, the same way you build them for any change.
3. Save your shard.
4. Copy its `Saves` folder into a second ServUO folder with the same build. Don't run the export on your live shard,
   although it only reads.
5. Start that copy and, as an Owner, run `[ExportForUnity <path>`. Without a path it writes
   `Export/servuo-export.json` beside ServUO.exe.

The file holds:

- **Accounts:** name, access level, created date, banned flag, ServUO's password hash, and account gold.
- **Characters:**
  - body, skin, hair and beard;
  - stats and locks;
  - skills, with base, cap and lock;
  - fame, karma and tithing;
  - map, place and facing.
- **Items:** everything worn, carried and banked, with what's inside. For each item:
  - its class and **the classes it derives from**;
  - amount and hue;
  - quality and material;
  - spellbook contents;
  - a rune's mark, and a runebook's runes;
  - every magic property, written `Group.Name` (ServUO's attribute enum and value).
- **Pets:** following, ridden and stabled pets, with their class and base classes, loyalty, bonding, death, hits and
  pack.
- **Houses:** size, co-owners and friends, and everything locked down or secured in them.

## 2. Import (on this game's server)

Put the export at `ServerData/Import/servuo-export.json`, and optionally your config at
`ServerData/Import/import-config.json`. Then run the admin API op `import.servuo`, or the MCP tool
`game_import_servuo`; its `localFile` and `localConfig` arguments upload both files for you.

**It is a dry run unless you say `dryRun: false`.** The dry run's report lists, in this order:

1. every account and character, and what would happen to each;
2. **unknown items:** classes that don't map yet;
3. **items by base type:** custom classes that will come over as a standard class;
4. **unmapped properties** and **unknown skills**;
5. notes: name clashes, houses, overflowing packs and stables.

Fix the config until the report is right, then import for real.

### What arrives

- **Accounts keep their names and passwords.** ServUO's hash (SHA-1, SHA-512 or MD5 of name + password) is checked
  at the first login here, then replaced by our own. Your config's admin levels (Owner, Developer and Administrator by
  default) become admin accounts. Player accounts are imported before staff accounts, so when two characters share a
  name, the player's character keeps it and the other gets a suffix ("the Second" and so on).
- **Characters stand where they logged out**, facing the same way:
  - **Skin:** clamped to our skin range. Gargoyle skins are mapped from your shard's gargoyle range onto ours.
  - **Hair and beards:** styles we don't have, such as elf hair, become the config's default styles. Gargoyles have
    no hair here.
  - **Stats, skills, locks, fame, karma and tithing** come over as they were.
- **Belongings:**
  - Worn items go back in their slots.
  - Containers are emptied into the pack or bank they were in, because our packs hold no containers.
  - An overfull backpack spills into the bank.
  - **Item details:**
    - Magic properties map by name.
    - Exceptional and material come over.
    - Spellbooks keep their spells.
    - Marked runes keep their place, and a runebook's runes come along as marked runes.
- **Pets** come over following, ridden or stabled. Pets past the followers limit go to the stable.
- **Houses:** ours are built piece by piece on land. Each house becomes the plot deed its footprint fits (12, 18, 24
  or 32 tiles), and everything locked down or secured in it goes to the owner's bank.
- **Account gold** goes into the first character's bank.

**Repeatable:** a character already imported from that shard (matched by `Source` and its serial) is skipped. With
`replace: true` it is replaced, which throws away anything done with it here since the last import.

### The config

Each setting in the file replaces its default; every setting not in the file keeps its default, which suits a stock
ServUO. [`Tools/ServUOExport/import-config.example.json`](../Tools/ServUOExport/import-config.example.json) lists every
setting.

| Setting | Default | What it does |
|---|---|---|
| `Source` | `"ServUO"` | Your shard's name. It's kept on imported accounts and characters, and decides what counts as "already imported". |
| `AdminLevels` | Owner, Developer, Administrator | Access levels that become admin accounts. |
| `StaffLast` | `true` | Import player accounts first, so players win name clashes. |
| `Facets`, `DefaultFacet` | the six standard maps; `1` | Your map names, by our facet number. A map not listed goes to `DefaultFacet`. |
| `Items` | | Your class name → ours (Items.json), or `""` to leave it behind. |
| `ItemsFallBackToBaseType` | `true` | An unmapped class we don't have comes over as the nearest class it derives from that we do have. For example, `FlamingSword : Longsword` comes over as a Longsword, keeping its magic properties. |
| `Containers` | Bag, Backpack, Pouch, BankBox | Containers to empty. Classes deriving from them count too, unless `Items` maps them. |
| `Creatures`, `CreaturesFallBackToBaseType` | `true` | The same as `Items`, for pets (Creatures.json). |
| `Props` | `"ArmorBonus": "AosElementAttribute"` | Magic properties. `"Group.Name": "Group.Name"` maps one property, `"Group": "Group"` renames a whole group, and `""` leaves a property behind without reporting it. Our names are in Magic.json (`Group`, `Name`). |
| `Skills` | | Your skill name → ours (key or name, Skills.json), or `""` to leave it behind. |
| `Materials`, `PlainMaterials`, `MaterialSuffixes` | `{}`; None, Iron, RegularLeather, RegularWood; Leather, Wood | Crafting materials. Exact names are mapped first; plain ones mean no material; then the suffixes come off ("SpinedLeather" becomes "Spined"). |
| `GargoyleSkinStart` | `1755` | Your shard's first gargoyle skin hue. |
| `DefaultHairMale`, `DefaultHairFemale` | `8251`, `8252` | Hair for styles we don't have (short hair, long hair). |
| `Houses` | `"PlotDeed"` | `"PlotDeed"`, `"ContentsOnly"` or `"Skip"`. |
| `HouseContents` | `"Bank"` | `"Bank"` or `"Skip"`. |
| `AccountGold` | `"FirstCharacterBank"` | `"FirstCharacterBank"` or `"Skip"`. |
| `PetOverflow` | `"Stable"` | `"Stable"` or `"Skip"`. |
| `NameSuffixes` | "the Second", "the Third", "the Fourth" | What clashing names get, in turn, then numbers. |

Not carried over:

- loose items lying in a house (only locked-down and secured things are surely the owner's);
- house co-owners and friends, and the house itself;
- quest progress, guilds, and your shard's own systems;
- anything your config leaves behind.

## SeriousFun

SeriousFun was the first shard moved this way, on 2026-11-11. Its config is
[`Tools/ServUOExport/seriousfun.import-config.json`](../Tools/ServUOExport/seriousfun.import-config.json), which sets
only `Source`.

**The export** was taken from a copy of the 12:46 save:

| Accounts | Characters | Items | Pets | Houses |
|---|---|---|---|---|
| 6 | 19 | 1,297 | 16 | 1 |

**Left behind:**

- a feed trough addon;
- 16 negative item tags (Antique, Prized and the like);
- 12 weapons' elemental damage splits, because our weapons don't split damage yet.

**Notes from the import:**

- The owner account's Kalor became "Kalor the Second".
- Kalor's 31×32 house became a Keep plot deed, plus 320 things in his bank.

**Imported on the live game server at 1:13 PM.** The accounts file was backed up first, to
`ServerData/accounts.before-seriousfun-import.json`. At the cutover, export again and import with `replace: true`.

## Tested

All tested on 2026-11-11:

- **SeriousFun:**
  - **Old passwords:** a ServUO-hashed password was refused when wrong and taken when right, and worked our way after.
  - **Repeat imports:** a second run skipped everyone.
  - **Playing an import:** Kalor came into the world at his spot in his plate armour with his pets in the stable.
- **A made-up custom shard** (`JoeBob`: its own map name, custom classes and attributes):
  - **Custom classes:**
    - The custom blade came over as a Longsword, exceptional valorite, with its damage bonus. Its custom life-drain
      attribute mapped to Hit Life Leech, and a cosmetic attribute was left behind as configured.
    - The custom loot sack was emptied (it derives from Bag).
    - The custom dragon came over as a bonded Dragon.
  - **Config mappings:**
    - A custom skill and a custom trophy were left behind, as the config said.
    - The custom map name went to facet 1.
  - **Repeat imports:** a second run skipped the character, and `replace` replaced it.

# Missing visuals inventory

Generated 2026-11-10 by a read-only audit: everything below comes from files (no editor, no play mode). It lists what the Unity client draws as a placeholder or not at all, so art can be sourced. How each list was derived is stated per section so it can be regenerated.

> **Change made alongside this audit:** `Assets/UOClient/Scripts/Game/ActorModel.cs` now draws a **magenta capsule** (named `Missing model (body N)`) for any non-human body the creature catalog has no model for. Before, those bodies silently fell back to the generic Synty human (the `CreatureCatalog` doc comment said capsule, the code said human), which hid every gap. Humans are unchanged. Not yet compiled or run in the editor.

## Contents
1. Creatures and NPCs
2. Map statics (bake census)
3. Items
4. Doors, boats, houses, effects and other placeholders
5. How to regenerate

## 1. Creatures and NPCs

**How determined.** `Content/Creatures.json` has 1324 kinds (ServUO types). `GameContent.Load` turns each kind's `Body` into body IDs (a number, a list from an expression like `Utility.RandomList(60, 61)`, or `Human`/missing = human 400/401). `ActorModel.Build` then looks every non-human body up in `Data/CreatureCatalog.asset` (76 entries covering 81 body IDs, decoded from the asset's packed hex lists; `Tools/Creatures/CreatureCatalog.eval.cs` is the source of truth). No entry = missing. "Spawned" = the kind appears in `Content/Spawns.json` (what `GameContent.Load`/`SpawnService` read, filtered by facet at load; 6788 spawn points over all facets); "spawn pts" = number of spawn-point entries naming it, "max" = sum of their `Max`. Whether something is created by other means (quests, scripts, summons, hand-placed vendors) cannot be told from files.

| Class | Kinds | Of which in Spawns.json |
|---|---|---|
| Has a catalog model | 214 | 72 |
| Human (the Sidekick human is correct) | 460 | 261 |
| **No model: capsule** (every body missing) | 518 | 178 |
| Some bodies modelled, some capsule (random-body kinds) | 15 | 8 |
| No `Body` in JSON, source says human (correct) | 51 | 18 |
| No `Body` in JSON and source did not resolve: drawn as human, true body unknown | 66 | 7 |

### 1.1 Bodies with no model (draw as a capsule now)

One row per UO body ID, by spawn weight. Kinds = ServUO types using that body (first four shown, busiest first). Bodies that appear only in random-body lists count too.

| Body | Kinds using it | Spawned kinds | Σ spawn pts | Σ max | Examples |
|---|---|---|---|---|---|
| 150 (0x96) | 5 | 2 | 462 | 6032 | SeaSerpent, CrystalSeaSerpent, DeepSeaSerpent, HPDeepSeaSerpent |
| 207 (0xCF) | 2 | 1 | 372 | 2618 | Sheep, SummonedSheep |
| 200 (0xC8) | 1 | 1 | 427 | 2611 | Horse |
| 204 (0xCC) | 1 | 1 | 427 | 2611 | Horse |
| 226 (0xE2) | 2 | 1 | 427 | 2611 | Horse, FactionWarHorse |
| 228 (0xE4) | 1 | 1 | 427 | 2611 | Horse |
| 214 (0xD6) | 2 | 1 | 378 | 2525 | Panther, SummonedPanther |
| 63 (0x3F) | 1 | 1 | 406 | 2478 | Cougar |
| 290 (0x122) | 1 | 1 | 362 | 2453 | Boar |
| 203 (0xCB) | 1 | 1 | 352 | 2421 | Pig |
| 209 (0xD1) | 1 | 1 | 346 | 2410 | Goat |
| 27 (0x1B) | 1 | 1 | 349 | 1725 | GreyWolf |
| 211 (0xD3) | 2 | 2 | 346 | 1722 | BlackBear, Grobu |
| 167 (0xA7) | 1 | 1 | 345 | 1721 | BrownBear |
| 212 (0xD4) | 2 | 1 | 344 | 1720 | GrizzlyBear, RagingGrizzlyBear |
| 6 (0x6) | 2 | 2 | 481 | 1396 | Bird, TropicalBird |
| 151 (0x97) | 1 | 1 | 231 | 1030 | Dolphin |
| 237 (0xED) | 2 | 2 | 428 | 950 | Hind, BravehornsMate |
| 216 (0xD8) | 2 | 1 | 373 | 805 | Cow, SummonedCow |
| 231 (0xE7) | 2 | 1 | 373 | 805 | Cow, SummonedCow |
| 232 (0xE8) | 1 | 1 | 353 | 726 | Bull |
| 233 (0xE9) | 1 | 1 | 353 | 726 | Bull |
| 5 (0x5) | 3 | 2 | 353 | 697 | Eagle, Swoop, Macaw |
| 234 (0xEA) | 3 | 1 | 345 | 690 | GreatHart, RabidReindeer, SummonedGreatHart |
| 205 (0xCD) | 11 | 3 | 141 | 675 | Rabbit, JackRabbit, FireRabbit, BaseEnraged |
| 238 (0xEE) | 4 | 3 | 120 | 620 | Rat, Sewerrat, PrisonRat, ClanRibbonPlagueRat |
| 201 (0xC9) | 3 | 1 | 52 | 421 | Cat, DiseasedCat, HellCat |
| 252 (0xFC) | 10 | 8 | 384 | 384 | BirlingBlades, Darkmane, Echidnite, Hellion |
| 271 (0x10F) | 10 | 8 | 368 | 384 | Dendrite, DespiseUnicorn, DivineGuardian, Fairy |
| 36 (0x24) | 5 | 3 | 85 | 321 | Lizardman, LizardmanSquatter, LizardmanDefender, LizardmanWitchdoctor |
| 56 (0x38) | 2 | 1 | 99 | 320 | Skeleton, Spellbinder |
| 8 (0x8) | 3 | 2 | 94 | 288 | Corpser, TanglingRoots, WhippingVine |
| 39 (0x27) | 5 | 2 | 92 | 237 | Mongbat, GreaterMongbat, DaemonMongbat, StrongMongbat |
| 48 (0x30) | 3 | 2 | 46 | 236 | Scorpion, SpeckledScorpion, Miasma |
| 52 (0x34) | 4 | 3 | 30 | 185 | Snake, CoralSnake, LavaSnake, IceSnake |
| 42 (0x2A) | 7 | 2 | 68 | 165 | Ratman, Lenley, ClanRC, ClanRS |
| 22 (0x16) | 4 | 2 | 83 | 134 | Gazer, ElderGazer, EternalGazer, StrangeGazer |
| 208 (0xD0) | 2 | 1 | 37 | 122 | Chicken, SummonedChicken |
| 30 (0x1E) | 3 | 2 | 62 | 106 | Harpy, Saliva, DazzledHarpy |
| 220 (0xDC) | 3 | 1 | 69 | 106 | Llama, EnergyVortex, RidableLlama |
| 202 (0xCA) | 2 | 1 | 14 | 104 | Alligator, WarAlligator |
| 60 (0x3C) | 6 | 2 | 58 | 98 | Drake, Grim, ColdDrake, FrostDrake |
| 61 (0x3D) | 6 | 2 | 58 | 98 | Drake, Grim, ColdDrake, FrostDrake |
| 62 (0x3E) | 3 | 1 | 48 | 91 | Wyvern, CorruptedWyvern, WyvernRenowned |
| 51 (0x33) | 7 | 4 | 19 | 73 | CorrosiveSlime, AcidSlug, Slime, Jwilson |
| 58 (0x3A) | 4 | 2 | 14 | 73 | Wisp, CrystalWisp, MysteriousWisp, Ortanord |
| 734 (0x2DE) | 3 | 1 | 7 | 69 | ToxicSlith, Slith, StoneSlith |
| 242 (0xF2) | 2 | 1 | 7 | 66 | DeathwatchBeetle, DeathwatchBeetleHatchling |
| 12 (0xC) | 4 | 2 | 53 | 58 | Dragon, GreaterDragon, FrostDragon, ShadowguardGreaterDragon |
| 59 (0x3B) | 4 | 2 | 53 | 58 | Dragon, GreaterDragon, FrostDragon, ShadowguardGreaterDragon |
| 210 (0xD2) | 1 | 1 | 6 | 57 | DesertOstard |
| 726 (0x2D6) | 2 | 1 | 6 | 57 | KepetchAmbusher, Kepetch |
| 790 (0x316) | 1 | 1 | 6 | 57 | SandVortex |
| 215 (0xD7) | 3 | 1 | 2 | 48 | GiantRat, PlagueRat, ShipRat |
| 221 (0xDD) | 1 | 1 | 12 | 48 | Walrus |
| 129 (0x81) | 2 | 2 | 32 | 32 | DiabolicalSeaweed, HarrowerTentacles |
| 729 (0x2D9) | 2 | 1 | 30 | 30 | Paralithode, Mimic |
| 259 (0x103) | 3 | 1 | 7 | 29 | InterredGrizzle, Ilhenir, MonstrousInterredGrizzle |
| 756 (0x2F4) | 3 | 2 | 6 | 29 | ExodusDrone, ExodusOverseer, Relanord |
| 131 (0x83) | 1 | 1 | 16 | 28 | Efreet |
| 128 (0x80) | 4 | 1 | 5 | 23 | Pixie, ArcaneFey, PixieRenowned, SAPixie |
| 219 (0xDB) | 1 | 1 | 6 | 23 | ForestOstard |
| 116 (0x74) | 3 | 1 | 21 | 21 | Nightmare, DreadWarhorse, UnholySteed |
| 123 (0x7B) | 2 | 2 | 5 | 20 | CrystalLatticeSeeker, EtherealWarrior |
| 77 (0x4D) | 3 | 1 | 14 | 18 | Kraken, HPKraken, Leviathan |
| 1293 (0x50D) | 2 | 2 | 5 | 17 | MyrmidexLarvae, DescicatedMyrmidexLarvae |
| 126 (0x7E) | 5 | 3 | 12 | 16 | EvilMageLord, Archmage, MasterTheophilus, SoulboundBattleMage |
| 770 (0x302) | 1 | 1 | 1 | 16 | MeerMage |
| 771 (0x303) | 1 | 1 | 1 | 16 | MeerWarrior |
| 772 (0x304) | 1 | 1 | 1 | 16 | MeerEternal |
| 773 (0x305) | 1 | 1 | 1 | 16 | MeerCaptain |
| 206 (0xCE) | 4 | 2 | 6 | 15 | LavaLizard, Thrasher, BexilPunchingBag, MagmaLizard |
| 715 (0x2CB) | 3 | 1 | 3 | 15 | RuddyBoura, HighPlainsBoura, LowlandBoura |
| 806 (0x326) | 2 | 1 | 3 | 15 | BlackSolenWarrior, BlackSolenInfiltratorWarrior |
| 807 (0x327) | 2 | 1 | 4 | 14 | BlackSolenQueen, BlackSolenInfiltratorQueen |
| 157 (0x9D) | 5 | 4 | 8 | 13 | GiantBlackWidow, LadyLissith, LadySabrix, SentinelSpider |
| 1403 (0x57B) | 2 | 1 | 4 | 12 | MyrmidexWarrior, SpectralMyrmidexWarrior |
| 757 (0x2F5) | 1 | 1 | 5 | 11 | ExodusMinion |
| 784 (0x310) | 2 | 2 | 8 | 11 | CrystalDaemon, ArcaneDaemon |
| 1402 (0x57A) | 1 | 1 | 4 | 11 | MyrmidexDrone |
| 165 (0xA5) | 5 | 1 | 2 | 10 | ShadowWisp, CorruptedWisp, DarkWisp, EnsorcledWisp |
| 265 (0x109) | 3 | 3 | 7 | 10 | Hydra, CrystalHydra, Abscess |
| 717 (0x2CD) | 2 | 1 | 4 | 10 | DesertScorpion, ClockworkScorpion |
| 766 (0x2FE) | 1 | 1 | 1 | 10 | JukaLord |
| 754 (0x2F2) | 1 | 1 | 5 | 9 | GargoyleEnforcer |
| 755 (0x2F3) | 2 | 1 | 5 | 9 | GargoyleDestroyer, GargoyleGuardian |
| 805 (0x325) | 1 | 1 | 5 | 9 | BlackSolenWorker |
| 28 (0x1C) | 1 | 1 | 2 | 8 | GiantSpider |
| 261 (0x105) | 2 | 1 | 1 | 8 | IgnisFatalis, ShimmeringEffusion |
| 305 (0x131) | 1 | 1 | 4 | 8 | GoreFiend |
| 763 (0x2FB) | 2 | 2 | 4 | 8 | ExodusSentinel, ExodusMinionLord |
| 791 (0x317) | 1 | 1 | 4 | 8 | Beetle |
| 11 (0xB) | 4 | 2 | 3 | 7 | Malefic, Virulent, Anlorzen, DreadSpider |
| 70 (0x46) | 1 | 1 | 4 | 7 | TerathanWarrior |
| 94 (0x5E) | 1 | 1 | 2 | 6 | FrostOoze |
| 307 (0x133) | 1 | 1 | 3 | 6 | Gibberling |
| 122 (0x7A) | 1 | 1 | 3 | 5 | Unicorn |
| 263 (0x107) | 2 | 1 | 1 | 5 | Minotaur, Meraktus |
| 264 (0x108) | 6 | 3 | 5 | 5 | Changeling, Guile, Irk, RenegadeChangeling |
| 764 (0x2FC) | 1 | 1 | 1 | 5 | JukaWarrior |
| 85 (0x55) | 2 | 2 | 4 | 4 | OphidianArchmage, OphidianMage |
| 86 (0x56) | 3 | 2 | 4 | 4 | OphidianKnight, OphidianWarrior, StoneMonster |
| 132 (0x84) | 1 | 1 | 2 | 4 | Kirin |
| 279 (0x117) | 2 | 1 | 2 | 4 | Ferret, ShimmeringFerret |
| 767 (0x2FF) | 1 | 1 | 4 | 4 | Betrayer |
| 768 (0x300) | 1 | 1 | 2 | 4 | Juggernaut |
| 777 (0x309) | 3 | 1 | 3 | 4 | Doppleganger, CovetousDoppleganger, SummonedDoppleganger |
| 72 (0x48) | 2 | 1 | 3 | 3 | TerathanMatriarch, Anlorlem |
| 90 (0x5A) | 2 | 1 | 1 | 3 | LavaSerpent, SummonedLavaSerpent |
| 142 (0x8E) | 5 | 1 | 1 | 3 | RatmanArcher, ClanCA, ClanCT, ClanSS |
| 266 (0x10A) | 2 | 1 | 1 | 3 | MLDryad, InsaneDryad |
| 267 (0x10B) | 2 | 1 | 1 | 3 | Troglodyte, Lurg |
| 714 (0x2CA) | 1 | 1 | 1 | 3 | IronBeetle |
| 722 (0x2D2) | 6 | 2 | 2 | 3 | UndeadGargoyle, Niporailem, EffeteUndeadGargoyle, PutridUndeadGargoyle |
| 71 (0x47) | 1 | 1 | 2 | 2 | TerathanDrone |
| 87 (0x57) | 1 | 1 | 2 | 2 | OphidianMatriarch |
| 92 (0x5C) | 3 | 1 | 2 | 2 | Coil, SeaSnake, SilverSerpent |
| 152 (0x98) | 2 | 1 | 2 | 2 | TerathanAvenger, Anlorvaglem |
| 276 (0x114) | 1 | 1 | 1 | 2 | Reptalon |
| 308 (0x134) | 1 | 1 | 1 | 2 | BoneDemon |
| 736 (0x2E0) | 1 | 1 | 1 | 2 | WolfSpider |
| 753 (0x2F1) | 1 | 1 | 1 | 2 | EnslavedGargoyle |
| 765 (0x2FD) | 1 | 1 | 1 | 2 | JukaMage |
| 778 (0x30A) | 1 | 1 | 1 | 2 | GazerLarva |
| 832 (0x340) | 3 | 1 | 1 | 2 | Phoenix, GreaterPhoenix, Pyre |
| 46 (0x2E) | 2 | 1 | 1 | 1 | AncientWyrm, ObsidianWyvern |
| 103 (0x67) | 2 | 1 | 1 | 1 | FierceDragon, SerpentineDragon |
| 169 (0xA9) | 1 | 1 | 1 | 1 | FireBeetle |
| 187 (0xBB) | 1 | 1 | 1 | 1 | Ridgeback |
| 241 (0xF1) | 3 | 1 | 1 | 1 | Oni, AbominableSnowman, LesserOni |
| 280 (0x118) | 2 | 1 | 1 | 1 | MinotaurCaptain, MinotaurGeneral |
| 281 (0x119) | 1 | 1 | 1 | 1 | MinotaurScout |
| 735 (0x2DF) | 1 | 1 | 1 | 1 | Navrey |
| 741 (0x2E5) | 1 | 1 | 1 | 1 | SlasherOfVeils |
| 0 (0x0) | 1 | 0 | 0 | 0 | PirateCrew |
| 2 (0x2) | 1 | 0 | 0 | 0 | PumpkinHead |
| 10 (0xA) | 1 | 0 | 0 | 0 | SummonedDaemon |
| 20 (0x14) | 2 | 0 | 0 | 0 | FrostSpider, SummonedFrostSpider |
| 21 (0x15) | 2 | 0 | 0 | 0 | DeathAdder, GiantSerpent |
| 33 (0x21) | 1 | 0 | 0 | 0 | PirateCrew |
| 37 (0x25) | 1 | 0 | 0 | 0 | WhiteWolf |
| 38 (0x26) | 1 | 0 | 0 | 0 | ServantOfSemidar |
| 49 (0x31) | 1 | 0 | 0 | 0 | WhiteWyrm |
| 64 (0x40) | 1 | 0 | 0 | 0 | SnowLeopard |
| 65 (0x41) | 1 | 0 | 0 | 0 | SnowLeopard |
| 66 (0x42) | 1 | 0 | 0 | 0 | SwampTentacle |
| 73 (0x49) | 1 | 0 | 0 | 0 | StoneHarpy |
| 80 (0x50) | 1 | 0 | 0 | 0 | GiantToad |
| 81 (0x51) | 2 | 0 | 0 | 0 | BullFrog, SummonedBullFrog |
| 88 (0x58) | 1 | 0 | 0 | 0 | MountainGoat |
| 89 (0x59) | 2 | 0 | 0 | 0 | GiantIceWorm, IceSerpent |
| 95 (0x5F) | 1 | 0 | 0 | 0 | Turkey |
| 99 (0x63) | 2 | 0 | 0 | 0 | DarkWolfFamiliar, UnholyFamiliar |
| 100 (0x64) | 1 | 0 | 0 | 0 | HolyFamiliar |
| 101 (0x65) | 2 | 0 | 0 | 0 | Centaur, Twaulo |
| 104 (0x68) | 3 | 0 | 0 | 0 | SkeletalDragon, SkeletalDragonRenowned, SkeletalDrake |
| 106 (0x6A) | 2 | 0 | 0 | 0 | CovetousWyrm, ShadowWyrm |
| 117 (0x75) | 2 | 0 | 0 | 0 | HolySteed, SilverSteed |
| 127 (0x7F) | 1 | 0 | 0 | 0 | PredatorHellCat |
| 143 (0x8F) | 3 | 0 | 0 | 0 | Chiikkaha, RatmanMage, VitaviRenowned |
| 144 (0x90) | 1 | 0 | 0 | 0 | SeaHorse |
| 146 (0x92) | 1 | 0 | 0 | 0 | Shadowlord |
| 164 (0xA4) | 1 | 0 | 0 | 0 | ChaosVortex |
| 172 (0xAC) | 1 | 0 | 0 | 0 | Rikktor |
| 173 (0xAD) | 1 | 0 | 0 | 0 | Mephitis |
| 174 (0xAE) | 1 | 0 | 0 | 0 | Semidar |
| 175 (0xAF) | 1 | 0 | 0 | 0 | LordOaks |
| 176 (0xB0) | 2 | 0 | 0 | 0 | NightmareFairy, Silvani |
| 180 (0xB4) | 2 | 0 | 0 | 0 | IceWyrm, WhiteWyrm |
| 185 (0xB9) | 1 | 0 | 0 | 0 | SavageRider |
| 188 (0xBC) | 1 | 0 | 0 | 0 | SavageRidgeback |
| 190 (0xBE) | 1 | 0 | 0 | 0 | FireSteed |
| 196 (0xC4) | 1 | 0 | 0 | 0 | KazeKemono |
| 197 (0xC5) | 1 | 0 | 0 | 0 | CrimsonDragon |
| 199 (0xC7) | 1 | 0 | 0 | 0 | RaiJu |
| 213 (0xD5) | 1 | 0 | 0 | 0 | PolarBear |
| 218 (0xDA) | 2 | 0 | 0 | 0 | FrenziedOstard, VvVMount |
| 240 (0xF0) | 1 | 0 | 0 | 0 | Kappa |
| 243 (0xF3) | 2 | 0 | 0 | 0 | Hiryu, LesserHiryu |
| 244 (0xF4) | 1 | 0 | 0 | 0 | RuneBeetle |
| 245 (0xF5) | 1 | 0 | 0 | 0 | YomotsuWarrior |
| 246 (0xF6) | 2 | 0 | 0 | 0 | BakeKitsune, SummonedBakeKitsune |
| 247 (0xF7) | 1 | 0 | 0 | 0 | FanDancer |
| 248 (0xF8) | 1 | 0 | 0 | 0 | Gaman |
| 249 (0xF9) | 2 | 0 | 0 | 0 | Serado, Yamandon |
| 251 (0xFB) | 1 | 0 | 0 | 0 | RevenantLion |
| 253 (0xFD) | 1 | 0 | 0 | 0 | YomotsuPriest |
| 254 (0xFE) | 1 | 0 | 0 | 0 | Crane |
| 255 (0xFF) | 1 | 0 | 0 | 0 | YomotsuElder |
| 256 (0x100) | 1 | 0 | 0 | 0 | ChiefParoxysmus |
| 257 (0x101) | 1 | 0 | 0 | 0 | DreadHorn |
| 258 (0x102) | 1 | 0 | 0 | 0 | LadyMelisande |
| 260 (0x104) | 1 | 0 | 0 | 0 | CorporealBrume |
| 262 (0x106) | 1 | 0 | 0 | 0 | TormentedMinotaur |
| 270 (0x10E) | 1 | 0 | 0 | 0 | Grubber |
| 273 (0x111) | 2 | 0 | 0 | 0 | FetidEssence, MantraEffervescence |
| 277 (0x115) | 1 | 0 | 0 | 0 | CuSidhe |
| 278 (0x116) | 2 | 0 | 0 | 0 | AlbinoSquirrel, Squirrel |
| 282 (0x11A) | 1 | 0 | 0 | 0 | PetParrot |
| 287 (0x11F) | 1 | 0 | 0 | 0 | BloodWorm |
| 291 (0x123) | 1 | 0 | 0 | 0 | PackHorse |
| 292 (0x124) | 1 | 0 | 0 | 0 | PackLlama |
| 293 (0x125) | 2 | 0 | 0 | 0 | Vollem, VollemHeld |
| 301 (0x12D) | 4 | 0 | 0 | 0 | FeralTreefellow, Treefellow, TreefellowGuardian, VileTreefellow |
| 302 (0x12E) | 1 | 0 | 0 | 0 | SkitteringHopper |
| 306 (0x132) | 2 | 0 | 0 | 0 | Impaler, TheButcher |
| 310 (0x136) | 2 | 0 | 0 | 0 | SummonedWailingBanshee, WailingBanshee |
| 311 (0x137) | 1 | 0 | 0 | 0 | ShadowKnight |
| 312 (0x138) | 2 | 0 | 0 | 0 | AbysmalHorror, AbyssalAbomination |
| 313 (0x139) | 1 | 0 | 0 | 0 | DarknightCreeper |
| 314 (0x13A) | 1 | 0 | 0 | 0 | Ravager |
| 315 (0x13B) | 1 | 0 | 0 | 0 | FleshRenderer |
| 316 (0x13C) | 1 | 0 | 0 | 0 | WandererOfTheVoid |
| 317 (0x13D) | 3 | 0 | 0 | 0 | ShipBat, VampireBat, VampireBatFamiliar |
| 318 (0x13E) | 2 | 0 | 0 | 0 | DemonKnight, UsagralemBallem |
| 319 (0x13F) | 2 | 0 | 0 | 0 | GooeyMaggots, MoundOfMaggots |
| 334 (0x14E) | 5 | 0 | 0 | 0 | EnslavedGoblinKeeper, EnslavedGoblinMage, EnslavedGoblinScout, EnslavedGrayGoblin |
| 574 (0x23E) | 1 | 0 | 0 | 0 | BladeSpirits |
| 692 (0x2B4) | 1 | 0 | 0 | 0 | AnimatedWeapon |
| 713 (0x2C9) | 1 | 0 | 0 | 0 | AbyssalInfernal |
| 716 (0x2CC) | 2 | 0 | 0 | 0 | BattleChickenLizard, ChickenLizard |
| 718 (0x2CE) | 1 | 0 | 0 | 0 | FairyDragon |
| 719 (0x2CF) | 1 | 0 | 0 | 0 | DragonWolf |
| 720 (0x2D0) | 2 | 0 | 0 | 0 | LavaElemental, Triton |
| 721 (0x2D1) | 1 | 0 | 0 | 0 | MaddeningHorror |
| 724 (0x2D4) | 1 | 0 | 0 | 0 | Gremlin |
| 728 (0x2D8) | 2 | 0 | 0 | 0 | Medusa, MedusaClone |
| 730 (0x2DA) | 2 | 0 | 0 | 0 | GargoylePet, Raptor |
| 732 (0x2DC) | 1 | 0 | 0 | 0 | Rotworm |
| 733 (0x2DD) | 1 | 0 | 0 | 0 | Skree |
| 737 (0x2E1) | 1 | 0 | 0 | 0 | TrapdoorSpider |
| 738 (0x2E2) | 1 | 0 | 0 | 0 | FireAnt |
| 739 (0x2E3) | 2 | 0 | 0 | 0 | LeatherWolf, LeatherWolfFellow |
| 740 (0x2E4) | 3 | 0 | 0 | 0 | DreamWraith, ShadowDweller, VoidManifestation |
| 776 (0x308) | 4 | 0 | 0 | 0 | Betballem, CapturedHordeMinion, HordeMinion, HordeMinionFamiliar |
| 781 (0x30D) | 1 | 0 | 0 | 0 | RedSolenWorker |
| 782 (0x30E) | 2 | 0 | 0 | 0 | RedSolenInfiltratorWarrior, RedSolenWarrior |
| 783 (0x30F) | 2 | 0 | 0 | 0 | RedSolenInfiltratorQueen, RedSolenQueen |
| 787 (0x313) | 4 | 0 | 0 | 0 | AntLion, BaseTalismanSummon, RockMite, SummonedAntLion |
| 788 (0x314) | 1 | 0 | 0 | 0 | Sphynx |
| 789 (0x315) | 1 | 0 | 0 | 0 | Quagmire |
| 793 (0x319) | 3 | 0 | 0 | 0 | GrizzledMare, HellSteed, SkeletalMount |
| 794 (0x31A) | 3 | 0 | 0 | 0 | BaneDragon, ParoxysmusSwampDragon, SwampDragon |
| 799 (0x31F) | 1 | 0 | 0 | 0 | ScaledSwampDragon |
| 808 (0x328) | 2 | 0 | 0 | 0 | BlackSolenMatriarch, RedSolenMatriarch |
| 826 (0x33A) | 1 | 0 | 0 | 0 | StygianDragon |
| 829 (0x33D) | 2 | 0 | 0 | 0 | EnragedColossus, RisingColossus |
| 830 (0x33E) | 1 | 0 | 0 | 0 | PrimevalLich |
| 831 (0x33F) | 1 | 0 | 0 | 0 | Parrot |
| 1026 (0x402) | 1 | 0 | 0 | 0 | GiantTurkey |
| 1068 (0x42C) | 2 | 0 | 0 | 0 | Osiredon, ParasiticEel |
| 1071 (0x42F) | 1 | 0 | 0 | 0 | Virtuebane |
| 1244 (0x4DC) | 2 | 0 | 0 | 0 | Charydbis, GiantTentacle |
| 1246 (0x4DE) | 1 | 0 | 0 | 0 | PumpkinHead |
| 1248 (0x4E0) | 1 | 0 | 0 | 0 | ClockworkExodus |
| 1255 (0x4E7) | 3 | 0 | 0 | 0 | WildBlackTiger, WildTiger, WildWhiteTiger |
| 1285 (0x505) | 1 | 0 | 0 | 0 | Dimetrosaur |
| 1286 (0x506) | 1 | 0 | 0 | 0 | Gallusaurus |
| 1287 (0x507) | 1 | 0 | 0 | 0 | Archaeosaurus |
| 1288 (0x508) | 1 | 0 | 0 | 0 | DragonTurtle |
| 1289 (0x509) | 2 | 0 | 0 | 0 | Najasaurus, StockingSerpent |
| 1290 (0x50A) | 1 | 0 | 0 | 0 | Allosaurus |
| 1291 (0x50B) | 1 | 0 | 0 | 0 | Saurosaurus |
| 1292 (0x50C) | 1 | 0 | 0 | 0 | Anchisaur |
| 1294 (0x50E) | 1 | 0 | 0 | 0 | DragonTurtleHatchling |
| 1309 (0x51D) | 1 | 0 | 0 | 0 | TigerCub |
| 1400 (0x578) | 4 | 0 | 0 | 0 | GreatApe, Poacher, TRex, Trapper |
| 1404 (0x57C) | 1 | 0 | 0 | 0 | MyrmidexQueen |
| 1405 (0x57D) | 1 | 0 | 0 | 0 | Zipactriotl |
| 1406 (0x57E) | 2 | 0 | 0 | 0 | BlackrockAutomaton, KotlAutomaton |
| 1407 (0x57F) | 1 | 0 | 0 | 0 | Lasher |
| 1408 (0x580) | 1 | 0 | 0 | 0 | Palomino |
| 1410 (0x582) | 1 | 0 | 0 | 0 | Windrunner |
| 1415 (0x587) | 1 | 0 | 0 | 0 | Triceratops |
| 1416 (0x588) | 1 | 0 | 0 | 0 | SabertoothedTiger |
| 1417 (0x589) | 1 | 0 | 0 | 0 | PlatinumDrake |
| 1419 (0x58B) | 1 | 0 | 0 | 0 | CrimsonDrake |
| 1422 (0x58E) | 1 | 0 | 0 | 0 | StygianDrake |
| 1423 (0x58F) | 1 | 0 | 0 | 0 | BloodFox |
| 1424 (0x590) | 1 | 0 | 0 | 0 | FrostMite |
| 1425 (0x591) | 1 | 0 | 0 | 0 | OsseinRam |
| 1426 (0x592) | 1 | 0 | 0 | 0 | Lion |
| 1428 (0x594) | 1 | 0 | 0 | 0 | JackInTheBox |
| 1440 (0x5A0) | 1 | 0 | 0 | 0 | Eowmu |
| 1441 (0x5A1) | 1 | 0 | 0 | 0 | SkeletalCat |
| 1479 (0x5C7) | 1 | 0 | 0 | 0 | KhalAnkur |
| 1484 (0x5CC) | 1 | 0 | 0 | 0 | Krampus |
| 1485 (0x5CD) | 1 | 0 | 0 | 0 | KrampusMinion |
| 1510 (0x5E6) | 1 | 0 | 0 | 0 | CoconutCrab |
| 1511 (0x5E7) | 1 | 0 | 0 | 0 | HungryCoconutCrab |
| 16047 (0x3EAF) | 1 | 0 | 0 | 0 | MinaxWarHorse |
| 16048 (0x3EB0) | 1 | 0 | 0 | 0 | SLWarHorse |
| 16049 (0x3EB1) | 1 | 0 | 0 | 0 | CoMWarHorse |
| 16050 (0x3EB2) | 1 | 0 | 0 | 0 | TBWarHorse |
| 16072 (0x3EC8) | 3 | 0 | 0 | 0 | WildBlackTiger, WildTiger, WildWhiteTiger |

Total: **295 bodies** without a model; 134 of them are spawned.

#### Spawned kinds drawn as capsule

| Kind | Base | Body | Spawn pts | Σ max | Mount | Tamable |
|---|---|---|---|---|---|---|
| SeaSerpent | BaseCreature | 150 | 460 | 6028 |  |  |
| Sheep | BaseCreature | 207 | 372 | 2618 |  | yes |
| Horse | BaseMount | 200,226,228,204 | 427 | 2611 | yes | yes |
| Panther | BaseCreature | 214 | 378 | 2525 |  | yes |
| Cougar | BaseCreature | 63 | 406 | 2478 |  | yes |
| Boar | BaseCreature | 290 | 362 | 2453 |  | yes |
| Pig | BaseCreature | 203 | 352 | 2421 |  | yes |
| Goat | BaseCreature | 209 | 346 | 2410 |  | yes |
| GreyWolf | BaseCreature | 25,27 | 349 | 1725 |  | yes |
| BlackBear | BaseCreature | 211 | 345 | 1721 |  | yes |
| BrownBear | BaseCreature | 167 | 345 | 1721 |  | yes |
| GrizzlyBear | BaseCreature | 212 | 344 | 1720 |  | yes |
| Bird | BaseCreature | 6 | 475 | 1362 |  | yes |
| Dolphin | BaseCreature | 151 | 231 | 1030 |  |  |
| Hind | BaseCreature | 237 | 426 | 940 |  | yes |
| Cow | BaseCreature | 216,231 | 373 | 805 |  | yes |
| Bull | BaseCreature | 232,233 | 353 | 726 |  | yes |
| Eagle | BaseCreature | 5 | 352 | 696 |  | yes |
| GreatHart | BaseCreature | 234 | 345 | 690 |  | yes |
| Rat | BaseCreature | 238 | 111 | 530 |  | yes |
| Cat | BaseCreature | 201 | 52 | 421 |  | yes |
| Rabbit | BaseCreature | 205 | 72 | 344 |  | yes |
| JackRabbit | BaseCreature | 205 | 68 | 330 |  | yes |
| Skeleton | BaseCreature | 50,56 | 99 | 320 |  |  |
| Lizardman | BaseCreature | 35,36 | 71 | 289 |  |  |
| Corpser | BaseCreature | 8 | 92 | 283 |  |  |
| Scorpion | BaseCreature | 48 | 41 | 229 |  | yes |
| Mongbat | BaseCreature | 39 | 87 | 221 |  | yes |
| Ratman | BaseCreature | 42 | 66 | 163 |  |  |
| Gazer | BaseCreature | 22 | 82 | 133 |  |  |
| Snake | BaseCreature | 52 | 23 | 126 |  | yes |
| Chicken | BaseCreature | 208 | 37 | 122 |  | yes |
| Llama | BaseCreature | 220 | 69 | 106 |  | yes |
| Alligator | BaseCreature | 202 | 14 | 104 |  | yes |
| Harpy | BaseCreature | 30 | 60 | 104 |  |  |
| Drake | BaseCreature | 60,61 | 57 | 97 |  | yes |
| Wyvern | BaseCreature | 62 | 48 | 91 |  |  |
| Sewerrat | BaseCreature | 238 | 7 | 80 |  | yes |
| ToxicSlith | BaseCreature | 734 | 7 | 69 |  |  |
| DeathwatchBeetle | BaseCreature | 242 | 7 | 66 |  | yes |
| CoralSnake | BaseCreature | 52 | 6 | 57 |  |  |
| DesertOstard | BaseMount | 210 | 6 | 57 | yes | yes |
| KepetchAmbusher | BaseCreature | 726 | 6 | 57 |  |  |
| SandVortex | BaseCreature | 790 | 6 | 57 |  |  |
| Dragon | BaseCreature | 12,59 | 50 | 55 |  | yes |
| Wisp | BaseCreature | 58 | 8 | 49 |  |  |
| BirlingBlades | DespiseCreature | 252 | 48 | 48 |  |  |
| Darkmane | DespiseCreature | 252 | 48 | 48 |  |  |
| Dendrite | DespiseCreature | 271 | 46 | 48 |  |  |
| DespiseUnicorn | DespiseCreature | 271 | 46 | 48 |  |  |
| DivineGuardian | DespiseCreature | 271 | 46 | 48 |  |  |
| Echidnite | DespiseCreature | 252 | 48 | 48 |  |  |
| Fairy | DespiseCreature | 271 | 46 | 48 |  |  |
| ForestNymph | DespiseCreature | 271 | 46 | 48 |  |  |
| GiantRat | BaseCreature | 215 | 2 | 48 |  | yes |
| Hellion | DespiseCreature | 252 | 48 | 48 |  |  |
| Naba | DespiseCreature | 252 | 48 | 48 |  |  |
| Phantom | DespiseCreature | 252 | 48 | 48 |  |  |
| Prometheoid | DespiseCreature | 252 | 48 | 48 |  |  |
| Sagittarri | DespiseCreature | 271 | 46 | 48 |  |  |
| Silenii | DespiseCreature | 271 | 46 | 48 |  |  |
| Skeletrex | DespiseCreature | 252 | 48 | 48 |  |  |
| Ursadane | DespiseCreature | 271 | 46 | 48 |  |  |
| Walrus | BaseCreature | 221 | 12 | 48 |  | yes |
| TropicalBird | BaseCreature | 6 | 6 | 34 |  | yes |
| CorrosiveSlime | BaseCreature | 51 | 4 | 30 |  | yes |
| DiabolicalSeaweed | BaseCreature | 129 | 30 | 30 |  |  |
| Paralithode | BaseCreature | 729 | 30 | 30 |  | yes |
| InterredGrizzle | BaseCreature | 259 | 7 | 29 |  |  |
| Efreet | BaseCreature | 131 | 16 | 28 |  |  |
| CrystalWisp | Wisp | 58 | 6 | 24 |  |  |
| LizardmanSquatter | BaseCreature | 35,36 | 10 | 24 |  |  |
| ForestOstard | BaseMount | 219 | 6 | 23 | yes | yes |
| Pixie | BaseCreature | 128 | 5 | 23 |  |  |
| Nightmare | BaseMount | 116 | 21 | 21 | yes | yes |
| AcidSlug | BaseCreature | 51 | 1 | 20 |  |  |
| ExodusDrone | BaseCreature | 756 | 2 | 20 |  |  |
| CrystalLatticeSeeker | BaseCreature | 123 | 4 | 18 |  |  |
| Kraken | BaseCreature | 77 | 14 | 18 |  |  |
| Slime | BaseCreature | 51 | 9 | 18 |  | yes |
| GreaterMongbat | BaseCreature | 39 | 5 | 16 |  | yes |
| MeerCaptain | BaseCreature | 773 | 1 | 16 |  |  |
| MeerEternal | BaseCreature | 772 | 1 | 16 |  |  |
| MeerMage | BaseCreature | 770 | 1 | 16 |  |  |
| MeerWarrior | BaseCreature | 771 | 1 | 16 |  |  |
| BlackSolenWarrior | BaseCreature | 806 | 3 | 15 |  |  |
| RuddyBoura | BaseCreature | 715 | 3 | 15 |  | yes |
| BlackSolenQueen | BaseCreature | 807 | 4 | 14 |  |  |
| MyrmidexWarrior | BaseCreature | 1403 | 4 | 12 |  |  |
| EvilMageLord | BaseCreature | 125,126 | 7 | 11 |  |  |
| ExodusMinion | BaseCreature | 757 | 5 | 11 |  |  |
| LavaLizard | BaseCreature | 206 | 2 | 11 |  | yes |
| MyrmidexDrone | BaseCreature | 1402 | 4 | 11 |  |  |
| MyrmidexLarvae | BaseCreature | 1293 | 4 | 11 |  |  |
| BravehornsMate | Hind | 237 | 2 | 10 |  |  |
| DesertScorpion | BaseCreature | 717 | 4 | 10 |  |  |
| GiantBlackWidow | BaseCreature | 157 | 5 | 10 |  |  |
| JukaLord | BaseCreature | 766 | 1 | 10 |  |  |
| PrisonRat | BaseCreature | 238 | 2 | 10 |  |  |
| ShadowWisp | BaseCreature | 165 | 2 | 10 |  |  |
| BlackSolenWorker | BaseCreature | 805 | 5 | 9 |  |  |
| ExodusOverseer | BaseCreature | 756 | 4 | 9 |  |  |
| GargoyleDestroyer | BaseCreature | 755 | 5 | 9 |  |  |
| GargoyleEnforcer | BaseCreature | 754 | 5 | 9 |  |  |
| Beetle | BaseMount | 791 | 4 | 8 | yes | yes |
| GiantSpider | BaseCreature | 28 | 2 | 8 |  | yes |
| GoreFiend | BaseCreature | 305 | 4 | 8 |  |  |
| IgnisFatalis | BaseCreature | 261 | 1 | 8 |  |  |
| LizardmanDefender | BaseCreature | 35,36 | 4 | 8 |  |  |
| SpeckledScorpion | Scorpion | 48 | 5 | 7 |  |  |
| TerathanWarrior | BaseCreature | 70 | 4 | 7 |  |  |
| CrystalDaemon | BaseCreature | 784 | 6 | 6 |  |  |
| DescicatedMyrmidexLarvae | BaseCreature | 1293 | 1 | 6 |  |  |
| ExodusSentinel | BaseCreature | 763 | 2 | 6 |  |  |
| FrostOoze | BaseCreature | 94 | 2 | 6 |  |  |
| Gibberling | BaseCreature | 307 | 3 | 6 |  |  |
| ArcaneDaemon | BaseCreature | 784 | 2 | 5 |  |  |
| Hydra | BaseCreature | 265 | 4 | 5 |  |  |
| JukaWarrior | BaseCreature | 764 | 1 | 5 |  |  |
| Jwilson | BaseCreature | 51 | 5 | 5 |  |  |
| Malefic | DreadSpider | 11 | 2 | 5 |  |  |
| Minotaur | BaseCreature | 263 | 1 | 5 |  |  |
| TanglingRoots | BaseCreature | 8 | 2 | 5 |  |  |
| Unicorn | BaseMount | 122 | 3 | 5 | yes | yes |
| Archmage | BaseCreature | 125,126 | 4 | 4 |  |  |
| Betrayer | BaseCreature | 767 | 4 | 4 |  |  |
| CrystalHydra | BaseCreature | 265 | 2 | 4 |  |  |
| CrystalSeaSerpent | SeaSerpent | 150 | 2 | 4 |  |  |
| Doppleganger | BaseCreature | 777 | 3 | 4 |  |  |
| Ferret | BaseCreature | 279 | 2 | 4 |  | yes |
| Juggernaut | BaseCreature | 768 | 2 | 4 |  |  |
| Kirin | BaseMount | 132 | 2 | 4 | yes | yes |
| Thrasher | BaseCreature | 206 | 4 | 4 |  |  |
| Changeling | BaseCreature | 264 | 3 | 3 |  |  |
| GreaterDragon | BaseCreature | 12,59 | 3 | 3 |  | yes |
| IronBeetle | BaseCreature | 714 | 1 | 3 |  | yes |
| LavaSerpent | BaseCreature | 90 | 1 | 3 |  |  |
| MLDryad | BaseCreature | 266 | 1 | 3 |  |  |
| RatmanArcher | BaseCreature | 142 | 1 | 3 |  |  |
| TerathanMatriarch | BaseCreature | 72 | 3 | 3 |  |  |
| Troglodyte | BaseCreature | 267 | 1 | 3 |  |  |
| BoneDemon | BaseCreature | 308 | 1 | 2 |  |  |
| Coil | SilverSerpent | 92 | 2 | 2 |  |  |
| EnslavedGargoyle | BaseCreature | 753 | 1 | 2 |  |  |
| EtherealWarrior | BaseCreature | 123 | 1 | 2 |  |  |
| ExodusMinionLord | BaseCreature | 763 | 2 | 2 |  |  |
| GazerLarva | BaseCreature | 778 | 1 | 2 |  |  |
| HarrowerTentacles | BaseCreature | 129 | 2 | 2 |  |  |
| JukaMage | BaseCreature | 765 | 1 | 2 |  |  |
| LavaSnake | BaseCreature | 52 | 1 | 2 |  |  |
| Lenley | BaseEscort | 42 | 2 | 2 |  |  |
| OphidianArchmage | BaseCreature | 85 | 2 | 2 |  |  |
| OphidianKnight | BaseCreature | 86 | 2 | 2 |  |  |
| OphidianMage | BaseCreature | 85 | 2 | 2 |  |  |
| OphidianMatriarch | BaseCreature | 87 | 2 | 2 |  |  |
| OphidianWarrior | BaseCreature | 86 | 2 | 2 |  |  |
| Phoenix | BaseCreature | 832 | 1 | 2 |  | yes |
| Reptalon | BaseMount | 276 | 1 | 2 | yes | yes |
| Saliva | Harpy | 30 | 2 | 2 |  |  |
| TerathanAvenger | BaseCreature | 152 | 2 | 2 |  |  |
| TerathanDrone | BaseCreature | 71 | 2 | 2 |  |  |
| UndeadGargoyle | BaseCreature | 722 | 1 | 2 |  |  |
| Virulent | BaseCreature | 11 | 1 | 2 |  |  |
| WolfSpider | BaseCreature | 736 | 1 | 2 |  | yes |
| Abscess | BaseCreature | 265 | 1 | 1 |  |  |
| AncientWyrm | BaseCreature | 46 | 1 | 1 |  |  |
| ElderGazer | BaseCreature | 22 | 1 | 1 |  |  |
| FierceDragon | BaseCreature | 103 | 1 | 1 |  |  |
| FireBeetle | BaseMount | 169 | 1 | 1 | yes | yes |
| FireRabbit | VorpalBunny | 205 | 1 | 1 |  |  |
| Grim | Drake | 60,61 | 1 | 1 |  |  |
| Grobu | BlackBear | 211 | 1 | 1 |  |  |
| Guile | Changeling | 264 | 1 | 1 |  |  |
| Irk | Changeling | 264 | 1 | 1 |  |  |
| LadyLissith | GiantBlackWidow | 157 | 1 | 1 |  |  |
| LadySabrix | GiantBlackWidow | 157 | 1 | 1 |  |  |
| MasterTheophilus | EvilMageLord | 125,126 | 1 | 1 |  |  |
| MinotaurCaptain | BaseCreature | 280 | 1 | 1 |  |  |
| MinotaurScout | BaseCreature | 281 | 1 | 1 |  |  |
| Navrey | BaseCreature | 735 | 1 | 1 |  |  |
| Niporailem | BaseSABoss | 722 | 1 | 1 |  |  |
| Oni | BaseCreature | 241 | 1 | 1 |  |  |
| Ridgeback | BaseMount | 187 | 1 | 1 | yes | yes |
| SentinelSpider | BaseCreature | 157 | 1 | 1 |  |  |
| SlasherOfVeils | BaseSABoss | 741 | 1 | 1 |  |  |
| Swoop | Eagle | 5 | 1 | 1 |  |  |

#### Never-spawned kinds drawn as capsule (in the data, but no spawn point names them)

347 kinds: PirateCrew (0,33,140,181,182); Macaw (5); WhippingVine (8); SummonedDaemon (10,9); Anlorzen (11); DreadSpider (11); FrostDragon (12,59); ShadowguardGreaterDragon (12,59); FrostSpider (20); SummonedFrostSpider (20); DeathAdder (21); GiantSerpent (21); EternalGazer (22); StrangeGazer (22); Spellbinder (26,50,56); DazzledHarpy (30); WhiteWolf (34,37); LizardmanWitchdoctor (35,36); SpectralKotlWarrior (36); ServantOfSemidar (38); DaemonMongbat (39); StrongMongbat (39); VampireMongbat (39); ClanRC (42); ClanRS (42); ClanSH (42); RatmanAssassin (42); TikitaviRenowned (42); ObsidianWyvern (46); Miasma (48); KhaldunBlood (51); Korpre (51); NatureFury (51); IceSnake (52); MysteriousWisp (58); Ortanord (58); ColdDrake (60,61); FrostDrake (60,61); ViciousDrake (60,61); VileDrake (60,61); CorruptedWyvern (62); WyvernRenowned (62); SnowLeopard (64,65); SwampTentacle (66); Anlorlem (72); StoneHarpy (73); HPKraken (77); Leviathan (77); GiantToad (80); BullFrog (81); SummonedBullFrog (81); StoneMonster (86); MountainGoat (88); GiantIceWorm (89); IceSerpent (89); SummonedLavaSerpent (90); SeaSnake (92); SilverSerpent (92); Turkey (95); DarkWolfFamiliar (99); UnholyFamiliar (99); HolyFamiliar (100); Centaur (101); Twaulo (101); SerpentineDragon (103); SkeletalDragon (104); SkeletalDragonRenowned (104); SkeletalDrake (104); CovetousWyrm (106); ShadowWyrm (106); DreadWarhorse (116); UnholySteed (116); HolySteed (117); SilverSteed (117); SoulboundBattleMage (125,126); SoulboundSpellSlinger (125,126); PredatorHellCat (127); ArcaneFey (128); PixieRenowned (128); SAPixie (128); ClanCA (142); ClanCT (142); ClanSS (142); RakktaviRenowned (142); Chiikkaha (143); RatmanMage (143); VitaviRenowned (143); SeaHorse (144); Shadowlord (146); DeepSeaSerpent (150); HPDeepSeaSerpent (150); HPSeaSerpent (150); Anlorvaglem (152); Silk (157); ChaosVortex (164); CorruptedWisp (165); DarkWisp (165); EnsorcledWisp (165); ShadowWispFamiliar (165); Rikktor (172); Mephitis (173); Semidar (174); LordOaks (175); NightmareFairy (176); Silvani (176); IceWyrm (180); WhiteWyrm (180,49); SavageRider (185); SavageRidgeback (188); FireSteed (190); KazeKemono (196); CrimsonDragon (197); RaiJu (199); DiseasedCat (201); HellCat (201); WarAlligator (202); BaseEnraged (205); EnragedBlackBear (205); EnragedEagle (205); EnragedHart (205); EnragedHind (205); EnragedRabbit (205); SummonedVorpalBunny (205); VorpalBunny (205); BexilPunchingBag (206); MagmaLizard (206); SummonedSheep (207); SummonedChicken (208); RagingGrizzlyBear (212); PolarBear (213); SummonedPanther (214); PlagueRat (215); ShipRat (215); SummonedCow (216,231); FrenziedOstard (218); VvVMount (218); EnergyVortex (220); RidableLlama (220); FactionWarHorse (226); RabidReindeer (234); SummonedGreatHart (234); ClanRibbonPlagueRat (238); Kappa (240); AbominableSnowman (241); LesserOni (241); DeathwatchBeetleHatchling (242); Hiryu (243); LesserHiryu (243); RuneBeetle (244); YomotsuWarrior (245); BakeKitsune (246); SummonedBakeKitsune (246); FanDancer (247); Gaman (248); Serado (249); Yamandon (249); RevenantLion (251); LadyOfTheSnow (252); Wight (252); YomotsuPriest (253); Crane (254); YomotsuElder (255); ChiefParoxysmus (256); DreadHorn (257); LadyMelisande (258); Ilhenir (259); MonstrousInterredGrizzle (259); CorporealBrume (260); ShimmeringEffusion (261); TormentedMinotaur (262); Meraktus (263); RenegadeChangeling (264); Spite (264); Travesty (264); InsaneDryad (266); Lurg (267); Grubber (270); EnslavedSatyr (271); Satyr (271); FetidEssence (273); MantraEffervescence (273); CuSidhe (277); AlbinoSquirrel (278); Squirrel (278); ShimmeringFerret (279); MinotaurGeneral (280); PetParrot (282); BloodWorm (287); PackHorse (291); PackLlama (292); Vollem (293); VollemHeld (293); FeralTreefellow (301); Treefellow (301); TreefellowGuardian (301); VileTreefellow (301); SkitteringHopper (302); Impaler (306); TheButcher (306); SummonedWailingBanshee (310); WailingBanshee (310); ShadowKnight (311); AbysmalHorror (312); AbyssalAbomination (312); DarknightCreeper (313); Ravager (314); FleshRenderer (315); WandererOfTheVoid (316); ShipBat (317); VampireBat (317); VampireBatFamiliar (317); DemonKnight (318); UsagralemBallem (318); GooeyMaggots (319); MoundOfMaggots (319); EnslavedGoblinKeeper (334); EnslavedGoblinMage (334); EnslavedGoblinScout (334); EnslavedGrayGoblin (334); EnslavedGreenGoblin (334); BladeSpirits (574); AnimatedWeapon (692); AbyssalInfernal (713); HighPlainsBoura (715); LowlandBoura (715); BattleChickenLizard (716); ChickenLizard (716); ClockworkScorpion (717); FairyDragon (718); DragonWolf (719); LavaElemental (720); Triton (720); MaddeningHorror (721); EffeteUndeadGargoyle (722); PutridUndeadGargoyle (722); PutridUndeadGuardian (722); UndeadGuardian (722); Gremlin (724); Kepetch (726); Medusa (728); MedusaClone (728); Mimic (729); GargoylePet (730); Raptor (730); Rotworm (732); Skree (733); Slith (734); StoneSlith (734); TrapdoorSpider (737); FireAnt (738); LeatherWolf (739); LeatherWolfFellow (739); DreamWraith (740); ShadowDweller (740); VoidManifestation (740); GargoyleGuardian (755); Relanord (756); Betballem (776); CapturedHordeMinion (776); HordeMinion (776); HordeMinionFamiliar (776); CovetousDoppleganger (777); SummonedDoppleganger (777); RedSolenWorker (781); RedSolenInfiltratorWarrior (782); RedSolenWarrior (782); RedSolenInfiltratorQueen (783); RedSolenQueen (783); AntLion (787); BaseTalismanSummon (787); RockMite (787); SummonedAntLion (787); Sphynx (788); Quagmire (789); GrizzledMare (793); HellSteed (793); SkeletalMount (793); BaneDragon (794); ParoxysmusSwampDragon (794); SwampDragon (794); ScaledSwampDragon (799); BlackSolenInfiltratorWarrior (806); BlackSolenInfiltratorQueen (807); BlackSolenMatriarch (808); RedSolenMatriarch (808); StygianDragon (826); EnragedColossus (829); RisingColossus (829); PrimevalLich (830); Parrot (831); GreaterPhoenix (832); Pyre (832); GiantTurkey (1026); Osiredon (1068); ParasiticEel (1068); Virtuebane (1071); Charydbis (1244); GiantTentacle (1244); PumpkinHead (1246,2); ClockworkExodus (1248); WildBlackTiger (1255,16072); WildTiger (1255,16072); WildWhiteTiger (1255,16072); Dimetrosaur (1285); Gallusaurus (1286); Archaeosaurus (1287); DragonTurtle (1288); Najasaurus (1289); StockingSerpent (1289); Allosaurus (1290); Saurosaurus (1291); Anchisaur (1292); DragonTurtleHatchling (1294); TigerCub (1309); GreatApe (1400); Poacher (1400); TRex (1400); Trapper (1400); SpectralMyrmidexWarrior (1403); MyrmidexQueen (1404); Zipactriotl (1405); BlackrockAutomaton (1406); KotlAutomaton (1406); Lasher (1407); Palomino (1408); Windrunner (1410); Triceratops (1415); SabertoothedTiger (1416); PlatinumDrake (1417); CrimsonDrake (1419); StygianDrake (1422); BloodFox (1423); FrostMite (1424); OsseinRam (1425); Lion (1426); JackInTheBox (1428); Eowmu (1440); SkeletalCat (1441); KhalAnkur (1479); Krampus (1484); KrampusMinion (1485); CoconutCrab (1510); HungryCoconutCrab (1511); MinaxWarHorse (16047); SLWarHorse (16048); CoMWarHorse (16049); TBWarHorse (16050)

### 1.2 Mounts

Kinds whose base is `BaseMount`/`BaseWarHorse` or that carry a `MountItem`. `Docs/CreatureMapping.md`: no horse model exists in the owned POLYGON packs (the SIMPLE Farm/Fantasy horses are not imported). Separately, `SidekickDresser.Signature` skips layer 0x19 (mount), so no rider-on-mount visual exists even for a modelled mount.

| Kind | Body | Status | Spawn pts |
|---|---|---|---|
| Horse | 200,226,228,204 | capsule | 427 |
| Nightmare | 116 | capsule | 21 |
| DesertOstard | 210 | capsule | 6 |
| ForestOstard | 219 | capsule | 6 |
| Beetle | 791 | capsule | 4 |
| Unicorn | 122 | capsule | 3 |
| Kirin | 132 | capsule | 2 |
| FireBeetle | 169 | capsule | 1 |
| Reptalon | 276 | capsule | 1 |
| Ridgeback | 187 | capsule | 1 |
| BaneDragon | 794 | capsule | 0 |
| CoMWarHorse | 16049 | capsule | 0 |
| CoconutCrab | 1510 | capsule | 0 |
| CuSidhe | 277 | capsule | 0 |
| DreadWarhorse | 116 | capsule | 0 |
| Eowmu | 1440 | capsule | 0 |
| FactionWarHorse | 226 | capsule | 0 |
| FireSteed | 190 | capsule | 0 |
| FrenziedOstard | 218 | capsule | 0 |
| GrizzledMare | 793 | capsule | 0 |
| HellSteed | 793 | capsule | 0 |
| Hiryu | 243 | capsule | 0 |
| HolySteed | 117 | capsule | 0 |
| Lasher | 1407 | capsule | 0 |
| LesserHiryu | 243 | capsule | 0 |
| MinaxWarHorse | 16047 | capsule | 0 |
| Palomino | 1408 | capsule | 0 |
| ParoxysmusSwampDragon | 794 | capsule | 0 |
| RidableLlama | 220 | capsule | 0 |
| SLWarHorse | 16048 | capsule | 0 |
| SavageRidgeback | 188 | capsule | 0 |
| ScaledSwampDragon | 799 | capsule | 0 |
| SeaHorse | 144 | capsule | 0 |
| SilverSteed | 117 | capsule | 0 |
| SkeletalCat | 1441 | capsule | 0 |
| SkeletalMount | 793 | capsule | 0 |
| SwampDragon | 794 | capsule | 0 |
| TBWarHorse | 16050 | capsule | 0 |
| UnholySteed | 116 | capsule | 0 |
| VvVMount | 218 | capsule | 0 |
| WildTiger | 1255,16072 | capsule | 0 |
| Windrunner | 1410 | capsule | 0 |

### 1.3 Kinds whose body was read as human (fixed)

`Creatures.json` had no `Body` for 190 kinds (ServUO sets it in the constructor or inherits it), and `GameContent.Load` treats a missing body as human, so e.g. Bird (475 spawn entries) looked like a person. On 2026-11-10 I added the real `Body` to the 73 creature kinds below, read from each kind's ServUO source (`Body = ...;` or the `BaseMount` constructor's body argument; a text match, not a compile). They now follow sections 1.1 and 1.4 like any other body. Re-export of Creatures.json would drop these, so the exporter needs the same rule.

Bird (6); BirlingBlades (252); Darkmane (252); Echidnite (252); Hellion (252); Naba (252); Phantom (252); Prometheoid (252); Skeletrex (252); Dendrite (271); DespiseUnicorn (271); DivineGuardian (271); Fairy (271); ForestNymph (271); Sagittarri (271); Silenii (271); Ursadane (271); Golem (752); Savage (184); Aminia (23); FireBeetle (169); Navrey (735); Reptalon (276); AnimatedWeapon (692); BaneDragon (794); BaseEnraged (205); BaseTalismanSummon (787); BurningMage (779); ClayGolem (779); CoMWarHorse (16049); CorruptedMage (779); CrazedMage (779); CrimsonDrake (1419); EnergyVortex (220); EnragedBlackBear (205); EnragedEagle (205); EnragedHart (205); EnragedHind (205); EnragedRabbit (205); GadgetryTableGolem (752); GiantTentacle (1244); GreatApe (1400); Hiryu (243); HolySteed (117); KhaldunRevenant (970); LesserHiryu (243); MedusaClone (728); MinaxWarHorse (16047); ParasiticEel (1068); PlagueSpawn (14); PlatinumDrake (1417); Poacher (1400); PumpkinHead (1246,2); RisingColossus (829); SLWarHorse (16048); SeaHorse (144); ShameGuardian (14); SilverSteed (117); SkelementalKnight (147); SkelementalMage (50); SpectralKotlWarrior (36); SpectralMyrmidexWarrior (1403); StoneMonster (86); SummonedReaper (47); TBWarHorse (16050); TheMasterInstructor (78); Trapper (1400); UnholySteed (116); VileMage (779); VvVMount (218); WildBlackTiger (1255,16072); WildTiger (1255,16072); WildWhiteTiger (1255,16072)

**Still unresolved (66 kinds):** no body in the JSON and none in the kind's own file (abstract bases, or bodies inherited from a parent). Most are probably humanoid NPCs, but that cannot be confirmed from files: AttendantFemaleGuide; AttendantFemaleHerald; AttendantFemaleLuckyDealer; AttendantFortuneTeller; AttendantGuide; AttendantHerald; AttendantLuckyDealer; AttendantMaleGuide; AttendantMaleHerald; AttendantMaleLuckyDealer; BaseHire; BasePeerless; BaseSeaChampion; BaseSummoned; BaseVoidCreature; BrideGroom; BrigandCannibal [2 spawn pts]; BrigandCannibalMage [2 spawn pts]; ChaosGuard; Clone; CommodityBroker; CovetousCreature; DespiseCreature; DragonsFlameGrandMage [5 spawn pts]; DragonsFlameMage; DummyAssassin; DummyFence; DummyHealer; DummyMace; DummyNox; DummyStun; DummySuper; DummySword; DummyThief; EscortableMage; EvilMage [7 spawn pts]; FactionBerserker; FactionDeathKnight; FactionDragoon; FactionHenchman; FactionKnight; FactionMercenary; FactionNecromancer; FactionPaladin; FactionSorceress; FactionWizard; Gregorio [1 spawn pts]; Harrower; Merchant; MerchantCaptain; Messenger; NaughtyTwin; Noble [27 spawn pts]; OrderGuard; Peasant; PersonalAttendant; PetBroker; RedDeath [1 spawn pts]; Rend; SeekerOfAdventure; SerpentsFangAssassin; SerpentsFangHighExecutioner; SoulboundApprenticeMage; TalkingBaseCreature; TigersClawMaster; TigersClawThief

> **2026-11-10 update:** gargoyle monsters (bodies 4, 67, 130) are no longer catalog stand-ins: they are the player gargoyle (Sidekick body, rigged wings, kilt and weapon) at player size. Rows for them below are out of date.

### 1.4 Stand-ins (a model reused for a different creature)

From `Tools/Creatures/CreatureCatalog.eval.cs` and the asset. These have a model, but it is not quite that creature; the last column is my judgement from the model names (subjective). Tint only recolours.

| Catalog entry | UO body | Model used | Judgement |
|---|---|---|---|
| orcs | 17 | Fantasy Rivals Big Ork | fine |
| orc captains / lords / brutes | 7, 138, 189 | Big Ork (scaled, tinted) | fine |
| orcish mages | 140 | Dungeon Goblin Shaman | stand-in (goblin for orc) |
| orc scouts and bombers | 181, 182 | Dungeon Goblin Warrior | stand-in (goblin for orc) |
| goblins | 723 | Dungeon Goblin | fine |
| ogres, ogre lords, arctic ogre lords | 1, 83, 135 | Big Ork (scaled/tinted) | stand-in (orc for ogre) |
| ettins | 18 | Barbarian Giant | rough |
| cyclopes | 75 | Dark Fortress Behemoth | stand-in |
| titans | 76 | Dark Fortress Colossus | stand-in |
| trolls / frost trolls | 53, 54, 55 | Fantasy Rivals Troll / Goblin Camp Troll | fine |
| daemons | 9 | Red Demon | fine |
| balrons | 40 | Dungeon Realms Big Demon | fine |
| chaos daemons | 792 | Dark Fantasy Demon | fine |
| imps | 74 | Dark Fantasy Demon Male scaled to 1.1 m | stand-in |
| succubi | 149 | Spirit Demon | stand-in |
| ice fiends | 43 | Kaiju 01 | stand-in |
| molochs | 785 | Kaiju 02 | stand-in |
| earth elementals | 14 | Dungeon Rock Golem | fine |
| golems | 752 | Mechanical Golem | fine |
| water/air/fire/poison/blood/ice/crystal elementals | 16,13,15,158,162,159,161,163,300 | Elemental Golem, tinted per element | stand-in (one model, 8 elements) |
| ore elementals (dull copper to valorite) | 107-113, 166 | Fort Golem, tinted per ore | stand-in (one model, 8 ores) |
| gargoyles / stone / fire | 4, 67, 130 | Dark Fantasy Gargoyle (tinted) | fine |
| skeleton family | 50,147,57,148,309 | Dark Fantasy/Dungeon skeletons | fine |
| zombies, ghouls, rotting corpses | 3,153,155 | Dark Fortress Undead/Corpse | fine |
| spectres/wraiths | 26 | Dark Fortress Wraith | fine |
| headless ones | 31 | Dungeon Tormented Soul | stand-in |
| restless souls | 970 | Dungeon Ghost | fine |
| spectral armour | 637 | Dungeon Realms Undead Knight | fine |
| liches / lich lords / ancient liches | 24,79,78 | Necromancer / Plague Lord x2 | lich lords + ancient = stand-in |
| evil mages | 124,125 | Dark Fortress Necromancer | stand-in (mage as undead) |
| mummies | 154 | Ancient Egypt Mummy | fine |
| flesh golems | 304 | Boss Zombie Brute | stand-in |
| plague beasts | 775 | Boss Zombie Blobber | stand-in |
| boglings / bog things | 779,780 | Boss Zombie Wretch / Slobber | stand-in |
| devourers | 303 | Dark Fortress Plague | stand-in |
| reapers | 47 | Forest Guardian | stand-in |
| lizardmen | 35 | Kaiju 03 | stand-in (rough) |
| gorillas | 29 | Kaiju Gorilla, tail hidden | fine |
| savages / savage shamans | 184,186 | Goblin War Camp Warrior / Shaman | stand-in (goblin camp for tribesmen) |
| timber/grey/dire/tsuki wolves | 225,25,23,250 | Dog Pack Wolf, tinted | fine |
| white wolves | 34 | Dog Pack Husky | fine |
| dogs | 217 | Dog Pack Labrador | fine |
| hellhounds | 98 | Dog Pack Hellhound | fine |

Broadly, every human NPC, vendor and quest-giver also shares the one Sidekick human with a seed-based face and a catalog outfit; there is no per-NPC art. `ActorModel.IsHumanBody` only knows 400/401/605/606, so other human-race bodies (402/403, 607/608, 666/667, 744/745, 987) that are not in the catalog now draw as capsules.

### 1.5 Spawned vs not

Of the 1324 kinds, 544 are named in Spawns.json and 780 are not. Among kinds with no model (capsule class), 186 are spawned and 347 never are. Never-spawned kinds are mostly quest, champion, peerless, seasonal and summon creatures; they matter only if something else creates them.

## 2. Map statics (bake census)

**How determined.** `Scripts/World/Bake/StaticCensus.cs` is run by the world bake and writes `WorldData/<facet>/census-<facet>.md` (problem graphics first, with example coordinates) and `census-<facet>.csv` (every graphic and outcome). "Missing" = outcome `PLACEHOLDER BOX` (a stand-in box) or `NOT DRAWN (…)` (no shape, invisible). The census files are dated 2026-11-10 00:17-00:49; re-bake after catalog changes. Names are UO tiledata names; counts are statics over the whole facet. The `.md` files give example coordinates for each graphic.

| Facet | Statics | Placeholder box | Not drawn: no shape | Not drawn: stair support | Not drawn: wall under roof | Problem graphics |
|---|---|---|---|---|---|---|
| 0 Felucca | 2,912,439 | 213,321 | 14,219 | 3,707 | 10 | 2915 |
| 1 Trammel | 2,913,521 | 222,413 | 13,604 | 3,755 | 13 | 3392 |
| 2 Ilshenar | 349,145 | 66,282 | 5,129 | 2,941 | 11 | 1878 |
| 3 Malas | 331,658 | 146,743 | 3,510 | 1,021 | 2 | 1524 |
| 4 Tokuno | 167,671 | 15,868 | 2,805 | 116 | 0 | 906 |
| 5 Ter Mur | 493,186 | 217,781 | 33,078 | 949 | 0 | 2835 |

Stair-support and wall-under-roof statics are hidden on purpose (the generated stair flight or roof replaces them), so they are not really missing art; "no shape" and boxes are. The tables count all three under "Not drawn".

### 2.1 Trammel (facet 1): full list, 3392 graphics, biggest first

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x051D | stone pavers | 0 | 8,193 | 0 | 8,193 |
| 0x0CA7 | rushes | 0 | 8,020 | 0 | 8,020 |
| 0x0519 | stone pavers Dark | 0 | 4,440 | 0 | 4,440 |
| 0x053D | cave floor | 0 | 4,322 | 0 | 4,322 |
| 0x053B | cave floor | 0 | 4,320 | 0 | 4,320 |
| 0x0D16 | mushroom | 1 | 4,290 | 0 | 4,290 |
| 0x0518 | cobblestones | 0 | 4,264 | 0 | 4,264 |
| 0x0515 | cobblestones | 0 | 3,996 | 0 | 3,996 |
| 0x053F | cave floor | 0 | 3,939 | 0 | 3,939 |
| 0x0D18 | mushroom | 1 | 3,669 | 0 | 3,669 |
| 0x053C | cave floor | 0 | 3,313 | 0 | 3,313 |
| 0x076C | sandstone | 10 | 3,183 | 0 | 3,183 |
| 0x21A3 | nodraw | 100 | 0 | 3,118 | 3,118 |
| 0x053E | cave floor | 0 | 3,063 | 0 | 3,063 |
| 0x08E1 | stalagmites | 10 | 2,799 | 0 | 2,799 |
| 0x0C93 | blade plant | 2 | 2,657 | 0 | 2,657 |
| 0x0C98 | fan plant | 1 | 2,469 | 0 | 2,469 |
| 0x0709 | marble | 10 | 2,387 | 0 | 2,387 |
| 0x3609 |  | 10 | 0 | 2,124 | 2,124 |
| 0x08E4 | stalagmites | 10 | 2,076 | 0 | 2,076 |
| 0x051E | stone pavers | 0 | 2,043 | 0 | 2,043 |
| 0x0D80 | vines | 0 | 2,037 | 0 | 2,037 |
| 0x04C1 | wooden logs | 0 | 1,977 | 0 | 1,977 |
| 0x08E0 | stalagmites | 10 | 1,974 | 0 | 1,974 |
| 0x0D82 | vines | 10 | 1,921 | 0 | 1,921 |
| 0x0D81 | vines | 10 | 1,912 | 0 | 1,912 |
| 0x0D91 | vines | 0 | 1,910 | 0 | 1,910 |
| 0x0517 | cobblestones | 0 | 1,906 | 0 | 1,906 |
| 0x0D92 | vines | 0 | 1,905 | 0 | 1,905 |
| 0x0D93 | vines | 0 | 1,905 | 0 | 1,905 |
| 0x0D6A | vines | 0 | 1,896 | 0 | 1,896 |
| 0x0D6D | vines | 0 | 1,888 | 0 | 1,888 |
| 0x0D6B | vines | 20 | 1,884 | 0 | 1,884 |
| 0x0D6C | vines | 20 | 1,879 | 0 | 1,879 |
| 0x0516 | cobblestones | 0 | 1,877 | 0 | 1,877 |
| 0x0D55 | vines | 20 | 1,833 | 0 | 1,833 |
| 0x0D56 | vines | 35 | 1,829 | 0 | 1,829 |
| 0x0524 | stone pavers Dark | 0 | 1,826 | 0 | 1,826 |
| 0x0CA9 | snake plant | 3 | 1,762 | 0 | 1,762 |
| 0x0D34 | morning glories | 0 | 1,745 | 0 | 1,745 |
| 0x0520 | stone pavers | 0 | 1,711 | 0 | 1,711 |
| 0x08E5 | stalagmites | 10 | 1,607 | 0 | 1,607 |
| 0x07CA | wooden bridge | 1 | 1,564 | 0 | 1,564 |
| 0x0521 | stone pavers Dark | 0 | 1,502 | 0 | 1,502 |
| 0x08E9 | stalagmites | 10 | 1,496 | 0 | 1,496 |
| 0x051F | stone pavers | 0 | 1,381 | 0 | 1,381 |
| 0x07CB | wooden bridge | 1 | 1,381 | 0 | 1,381 |
| 0x0523 | stone pavers Dark | 0 | 1,368 | 0 | 1,368 |
| 0x08E3 | flowstone | 10 | 1,366 | 0 | 1,366 |
| 0x08EA | stalagmites | 10 | 1,359 | 0 | 1,359 |
| 0x08E6 | flowstone | 10 | 1,317 | 0 | 1,317 |
| 0x050D | marble floor | 0 | 1,140 | 0 | 1,140 |
| 0x07A3 | stone | 10 | 0 | 1,067 | 1,067 |
| 0x0ABE | carpet | 0 | 1,051 | 0 | 1,051 |
| 0x08E2 | flowstone | 10 | 972 | 0 | 972 |
| 0x0750 | stone | 10 | 0 | 963 | 963 |
| 0x049C | grey slate tile | 0 | 927 | 0 | 927 |
| 0x32B1 | lava | 0 | 908 | 0 | 908 |
| 0x2199 | nodraw | 3 | 0 | 891 | 891 |
| 0x08E8 | flowstone | 16 | 881 | 0 | 881 |
| 0x32AB | lava | 0 | 866 | 0 | 866 |
| 0x04A0 | sandstone paver | 0 | 848 | 0 | 848 |
| 0x32AF | lava | 0 | 842 | 0 | 842 |
| 0x32AD | lava | 0 | 812 | 0 | 812 |
| 0x324F | sunken log | 0 | 810 | 0 | 810 |
| 0x08E7 | stalagmites | 10 | 790 | 0 | 790 |
| 0x07BE | platform | 2 | 778 | 0 | 778 |
| 0xAE46 | Tracks Straight Sout | 1 | 710 | 0 | 710 |
| 0x0522 | stone pavers Dark | 0 | 678 | 0 | 678 |
| 0x055E | stone roof | 0 | 660 | 0 | 660 |
| 0x050E | white marble | 0 | 655 | 0 | 655 |
| 0x56D0 | carpet | 1 | 652 | 0 | 652 |
| 0x0AC8 | carpet | 0 | 626 | 0 | 626 |
| 0x055F | stone roof | 0 | 590 | 0 | 590 |
| 0x0540 | cave floor | 0 | 582 | 0 | 582 |
| 0x0573 | stone roof | 0 | 577 | 0 | 577 |
| 0x055C | stone roof | 1 | 574 | 0 | 574 |
| 0x0560 | stone roof | 0 | 559 | 0 | 559 |
| 0x0569 | stone roof | 0 | 554 | 0 | 554 |
| 0x0561 | stone roof | 0 | 553 | 0 | 553 |
| 0x0577 | stone roof | 0 | 547 | 0 | 547 |
| 0x04F2 | blue tiles | 0 | 539 | 0 | 539 |
| 0x057A | stone roof | 0 | 531 | 0 | 531 |
| 0x10EE | garbage | 0 | 531 | 0 | 531 |
| 0x17CD | snow | 0 | 520 | 0 | 520 |
| 0x057B | stone roof | 0 | 510 | 0 | 510 |
| 0x0564 | stone roof | 0 | 508 | 0 | 508 |
| 0x0738 | wood | 10 | 0 | 506 | 506 |
| 0x0DC1 | water lilly | 0 | 506 | 0 | 506 |
| 0x0DC2 | water lilly | 0 | 490 | 0 | 490 |
| 0x0DBD | lilly pad | 0 | 489 | 0 | 489 |
| 0x324E | sunken log | 0 | 481 | 0 | 481 |
| 0x0576 | stone roof | 0 | 481 | 0 | 481 |
| 0x071E | stone | 10 | 0 | 479 | 479 |
| 0x0AD1 | carpet | 0 | 477 | 0 | 477 |
| 0x0DBC | lilly pad | 0 | 469 | 0 | 469 |
| 0x0DBE | lilly pads | 0 | 466 | 0 | 466 |
| 0x3250 | sunken log | 0 | 465 | 0 | 465 |
| 0x0DC3 | water lilly | 0 | 463 | 0 | 463 |
| 0x0575 | stone roof | 0 | 459 | 0 | 459 |
| 0x0EE3 | spiderweb | 0 | 455 | 0 | 455 |
| 0x0578 | stone roof | 0 | 441 | 0 | 441 |
| 0x055D | stone roof | 0 | 440 | 0 | 440 |
| 0x0547 | cave floor | 0 | 440 | 0 | 440 |
| 0x0510 | marble floor | 0 | 433 | 0 | 433 |
| 0x0548 | cave floor | 0 | 426 | 0 | 426 |
| 0xAE47 | Tracks Straight East | 1 | 410 | 0 | 410 |
| 0x0574 | stone roof | 0 | 396 | 0 | 396 |
| 0x0EE6 | spiderweb | 0 | 388 | 0 | 388 |
| 0x0549 | cave floor | 0 | 387 | 0 | 387 |
| 0x057E | stone roof | 0 | 384 | 0 | 384 |
| 0x049F | sandstone paver | 0 | 380 | 0 | 380 |
| 0x0567 | stone roof | 0 | 377 | 0 | 377 |
| 0x049E | sandstone paver | 0 | 372 | 0 | 372 |
| 0xAE31 | Wall Mine Beams 01 E | 1 | 360 | 0 | 360 |
| 0x0EE4 | spiderweb | 0 | 350 | 0 | 350 |
| 0x0639 | stone roof | 1 | 347 | 0 | 347 |
| 0x0563 | stone roof | 0 | 346 | 0 | 346 |
| 0xAE34 | Wall Mine Beams 04 E | 1 | 342 | 0 | 342 |
| 0x2198 | nodraw | 0 | 0 | 341 | 341 |
| 0x0EE5 | spiderweb | 0 | 334 | 0 | 334 |
| 0x2212 | crystal | 10 | 331 | 0 | 331 |
| 0x0570 | stone roof | 0 | 325 | 0 | 325 |
| 0x056B | stone roof | 0 | 325 | 0 | 325 |
| 0x04C5 | wooden logs | 0 | 321 | 0 | 321 |
| 0x0637 | stone roof | 1 | 320 | 0 | 320 |
| 0x0C2D | debris | 1 | 319 | 0 | 319 |
| 0x21A4 | no draw | 4 | 0 | 319 | 319 |
| 0x063A | stone roof | 1 | 318 | 0 | 318 |
| 0x056D | stone roof | 0 | 317 | 0 | 317 |
| 0xAE32 | Wall Mine Beams 02 E | 1 | 316 | 0 | 316 |
| 0x057D | stone roof | 0 | 309 | 0 | 309 |
| 0x361A |  | 0 | 0 | 307 | 307 |
| 0x221A | crystal | 10 | 303 | 0 | 303 |
| 0xAE33 | Wall Mine Beams 03 E | 1 | 303 | 0 | 303 |
| 0x0571 | stone roof | 0 | 302 | 0 | 302 |
| 0xAE2D | Wall Mine Beams 03 S | 1 | 298 | 0 | 298 |
| 0xAE2B | Wall Mine Beams 01 S | 1 | 294 | 0 | 294 |
| 0x0544 | cave floor | 0 | 293 | 0 | 293 |
| 0xAE2C | Wall Mine Beams 02 S | 1 | 290 | 0 | 290 |
| 0x0508 | wooden logs | 0 | 288 | 0 | 288 |
| 0x07BD | platform | 2 | 287 | 0 | 287 |
| 0xAE2E | Wall Mine Beams 04 S | 1 | 287 | 0 | 287 |
| 0x2985 | wooden floor | 0 | 281 | 0 | 281 |
| 0x051C | stone pavers | 0 | 275 | 0 | 275 |
| 0x063B | marble roof | 0 | 273 | 0 | 273 |
| 0x1038 | wood curls | 1 | 272 | 0 | 272 |
| 0x0ECE | bones | 1 | 269 | 0 | 269 |
| 0x051B | stone pavers | 0 | 268 | 0 | 268 |
| 0x19BA | iron ore | 1 | 268 | 0 | 268 |
| 0x0541 | cave floor | 0 | 266 | 0 | 266 |
| 0x10EF | garbage | 0 | 265 | 0 | 265 |
| 0x063D | marble roof | 0 | 263 | 0 | 263 |
| 0x1B0F | bone pile | 1 | 263 | 0 | 263 |
| 0x1B19 | bone shards | 1 | 262 | 0 | 262 |
| 0x0543 | cave floor | 0 | 260 | 0 | 260 |
| 0x3213 | swamp | 0 | 258 | 0 | 258 |
| 0x0638 | stone roof | 1 | 256 | 0 | 256 |
| 0x1B17 | rib cage | 1 | 256 | 0 | 256 |
| 0x328F | lava | 1 | 255 | 0 | 255 |
| 0x1B0B | bone pile | 1 | 254 | 0 | 254 |
| 0x0CEE | vines | 0 | 252 | 0 | 252 |
| 0x122F | blood smear | 0 | 249 | 0 | 249 |
| 0x56C5 | carpet | 1 | 248 | 0 | 248 |
| 0x050A | wooden logs | 0 | 247 | 0 | 247 |
| 0x0504 | wooden logs | 0 | 244 | 0 | 244 |
| 0x0525 | sandstone floor | 0 | 241 | 0 | 241 |
| 0x32A2 | lava | 0 | 237 | 0 | 237 |
| 0x057F | slate roof | 0 | 237 | 0 | 237 |
| 0x122D | blood | 0 | 234 | 0 | 234 |
| 0x04C4 | wooden logs | 0 | 233 | 0 | 233 |
| 0x2E28 | acid | 0 | 230 | 0 | 230 |
| 0x322C | swamp | 0 | 230 | 0 | 230 |
| 0x3241 | swamp | 0 | 228 | 0 | 228 |
| 0x19B9 | iron ore | 1 | 226 | 0 | 226 |
| 0x11E9 | woven mat | 1 | 222 | 0 | 222 |
| 0x0C2E | debris | 1 | 221 | 0 | 221 |
| 0x1B0C | bone pile | 1 | 220 | 0 | 220 |
| 0x0ECF | bones | 1 | 217 | 0 | 217 |
| 0x1B0A | bone pile | 1 | 217 | 0 | 217 |
| 0x2F53 |  | 0 | 0 | 216 | 216 |
| 0x1B0E | bone pile | 1 | 216 | 0 | 216 |
| 0x122C | blood | 0 | 215 | 0 | 215 |
| 0x070A | marble stairs | 5 | 215 | 0 | 215 |
| 0x1B10 | bone pile | 1 | 213 | 0 | 213 |
| 0x0542 | cave floor | 0 | 212 | 0 | 212 |
| 0x0ED0 | bones | 1 | 210 | 0 | 210 |
| 0x32A4 | lava | 0 | 209 | 0 | 209 |
| 0x11E7 | woven mat | 1 | 208 | 0 | 208 |
| 0x0579 | stone roof | 0 | 202 | 0 | 202 |
| 0x2E0C | acid | 0 | 201 | 0 | 201 |
| 0x0C2F | debris | 1 | 200 | 0 | 200 |
| 0x122B | blood | 0 | 198 | 0 | 198 |
| 0x122A | blood | 0 | 197 | 0 | 197 |
| 0x0545 | cave floor | 0 | 192 | 0 | 192 |
| 0x1B0D | bone pile | 1 | 192 | 0 | 192 |
| 0x2F55 |  | 0 | 0 | 188 | 188 |
| 0x1B1B | spine | 1 | 187 | 0 | 187 |
| 0x1B12 | bone | 1 | 182 | 0 | 182 |
| 0x32A5 | lava | 0 | 182 | 0 | 182 |
| 0x063E | marble roof | 0 | 181 | 0 | 181 |
| 0x0546 | cave floor | 0 | 180 | 0 | 180 |
| 0x0C5E | vines | 1 | 180 | 0 | 180 |
| 0x0CEF | vines | 0 | 179 | 0 | 179 |
| 0x10F0 | garbage | 0 | 179 | 0 | 179 |
| 0x04F3 | red tiles | 0 | 176 | 0 | 176 |
| 0x054A | cave floor | 0 | 175 | 0 | 175 |
| 0x10F1 | garbage | 0 | 175 | 0 | 175 |
| 0x063C | marble roof | 0 | 175 | 0 | 175 |
| 0x0ED2 | bones | 1 | 174 | 0 | 174 |
| 0x0C60 | vines | 1 | 172 | 0 | 172 |
| 0x32A3 | lava | 0 | 171 | 0 | 171 |
| 0x2E1B | acid | 0 | 171 | 0 | 171 |
| 0x1AE4 | skull | 1 | 171 | 0 | 171 |
| 0x2E11 | acid | 0 | 168 | 0 | 168 |
| 0x0ACF | carpet | 0 | 167 | 0 | 167 |
| 0x4572 | Icicle Large South | 0 | 166 | 0 | 166 |
| 0x0ACD | carpet | 0 | 166 | 0 | 166 |
| 0x050F | marble floor | 0 | 165 | 0 | 165 |
| 0x0AD0 | carpet | 0 | 165 | 0 | 165 |
| 0x1ADB | pile of skulls | 1 | 164 | 0 | 164 |
| 0x1779 | rock | 1 | 164 | 0 | 164 |
| 0x070B | marble stairs | 5 | 162 | 0 | 162 |
| 0x0526 | sandstone floor | 0 | 162 | 0 | 162 |
| 0x08F9 | stone rail | 5 | 161 | 0 | 161 |
| 0x1B18 | rib cage | 1 | 159 | 0 | 159 |
| 0x1B1A | bone shards | 1 | 159 | 0 | 159 |
| 0x1B11 | bone | 1 | 159 | 0 | 159 |
| 0x1AE1 | skull | 1 | 158 | 0 | 158 |
| 0x0ECB | bones | 1 | 156 | 0 | 156 |
| 0x1AD9 | pile of skulls | 1 | 156 | 0 | 156 |
| 0x054C | cave floor | 0 | 156 | 0 | 156 |
| 0x326F | swamp | 0 | 155 | 0 | 155 |
| 0x0ACE | carpet | 0 | 155 | 0 | 155 |
| 0x0C30 | debris | 1 | 154 | 0 | 154 |
| 0x1B13 | jaw bone | 1 | 154 | 0 | 154 |
| 0x1ADC | pile of skulls | 1 | 152 | 0 | 152 |
| 0x0509 | wooden logs | 0 | 152 | 0 | 152 |
| 0x054D | cave floor | 0 | 152 | 0 | 152 |
| 0x361E |  | 0 | 0 | 151 | 151 |
| 0x0AA9 | Rug East | 0 | 150 | 0 | 150 |
| 0x1AD8 | pile of skulls | 1 | 149 | 0 | 149 |
| 0x0ED1 | bones | 1 | 149 | 0 | 149 |
| 0x1A9B | flax | 1 | 148 | 0 | 148 |
| 0x0C5F | vines | 1 | 148 | 0 | 148 |
| 0x054B | cave floor | 0 | 146 | 0 | 146 |
| 0x1ADF | pile of skulls | 1 | 145 | 0 | 145 |
| 0x2E17 | acid | 0 | 145 | 0 | 145 |
| 0xAE38 | Wall Mine Arch South | 1 | 144 | 0 | 144 |
| 0x0530 | sandstone floor | 0 | 142 | 0 | 142 |
| 0x0531 | sandstone floor | 0 | 142 | 0 | 142 |
| 0x0527 | sandstone floor | 0 | 142 | 0 | 142 |
| 0x122E | blood | 0 | 141 | 0 | 141 |
| 0x1ADA | pile of skulls | 1 | 140 | 0 | 140 |
| 0x1B16 | pelvis bone | 1 | 140 | 0 | 140 |
| 0x0CEB | vines | 0 | 140 | 0 | 140 |
| 0x09FD | wall sconce | 0 | 139 | 0 | 139 |
| 0x0ABF | carpet | 0 | 139 | 0 | 139 |
| 0x0CF2 | vines | 0 | 138 | 0 | 138 |
| 0x0532 | sandstone floor | 0 | 137 | 0 | 137 |
| 0x0ECD | bones | 1 | 137 | 0 | 137 |
| 0x0528 | sandstone floor | 0 | 137 | 0 | 137 |
| 0x31B2 |  | 0 | 0 | 136 | 136 |
| 0x1B09 | bone pile | 1 | 136 | 0 | 136 |
| 0x0ADA |  | 0 | 0 | 136 | 136 |
| 0x19AB | fire | 0 | 133 | 0 | 133 |
| 0x3251 | sunken log | 0 | 133 | 0 | 133 |
| 0x326D | swamp | 0 | 132 | 0 | 132 |
| 0x1ADD | pile of skulls | 1 | 132 | 0 | 132 |
| 0x054E | cave floor | 0 | 131 | 0 | 131 |
| 0x3B41 |  | 0 | 0 | 131 | 131 |
| 0x0512 | marble floor | 0 | 130 | 0 | 130 |
| 0x1AE2 | skull | 1 | 130 | 0 | 130 |
| 0x052F | sandstone floor | 0 | 130 | 0 | 130 |
| 0x0580 | slate roof | 0 | 129 | 0 | 129 |
| 0x3295 | lava | 1 | 129 | 0 | 129 |
| 0x0581 | slate roof | 0 | 126 | 0 | 126 |
| 0x0ECA | bones | 1 | 125 | 0 | 125 |
| 0x17CE | snow | 0 | 124 | 0 | 124 |
| 0x0514 | marble floor | 0 | 123 | 0 | 123 |
| 0x054F | cave floor | 0 | 123 | 0 | 123 |
| 0x0AF6 |  | 0 | 0 | 122 | 122 |
| 0x2F54 |  | 0 | 0 | 120 | 120 |
| 0x0511 | marble floor | 0 | 120 | 0 | 120 |
| 0x0A02 | wall sconce | 0 | 120 | 0 | 120 |
| 0x079E | cave steps | 11 | 119 | 0 | 119 |
| 0x0583 | slate roof | 0 | 119 | 0 | 119 |
| 0x222A | crystal | 10 | 119 | 0 | 119 |
| 0x187E | spilled flour | 1 | 119 | 0 | 119 |
| 0x2E19 | acid | 0 | 119 | 0 | 119 |
| 0x2E0A | acid | 0 | 118 | 0 | 118 |
| 0x0AFA |  | 0 | 0 | 118 | 118 |
| 0x0AF8 |  | 0 | 0 | 117 | 117 |
| 0x1AE3 | skull | 1 | 117 | 0 | 117 |
| 0x34FF | waterfall | 3 | 116 | 0 | 116 |
| 0x2E02 | acid | 0 | 116 | 0 | 116 |
| 0x0513 | marble floor | 0 | 115 | 0 | 115 |
| 0x326C | swamp | 0 | 114 | 0 | 114 |
| 0x0788 | stone | 10 | 0 | 113 | 113 |
| 0x3291 | lava | 1 | 113 | 0 | 113 |
| 0x10D9 | egg case | 2 | 113 | 0 | 113 |
| 0x56EC | carpet | 1 | 112 | 0 | 112 |
| 0x1ADE | pile of skulls | 1 | 112 | 0 | 112 |
| 0x3289 | lava | 1 | 112 | 0 | 112 |
| 0x1AE0 | skull | 1 | 112 | 0 | 112 |
| 0x32AE | lava | 0 | 111 | 0 | 111 |
| 0x0C92 | untrimmed hedge | 6 | 110 | 0 | 110 |
| 0x0582 | slate roof | 0 | 110 | 0 | 110 |
| 0x1645 | blood | 0 | 109 | 0 | 109 |
| 0x31B4 |  | 0 | 0 | 109 | 109 |
| 0x19BB | brazier | 1 | 108 | 0 | 108 |
| 0x2E1C | acid | 0 | 107 | 0 | 107 |
| 0x056A | cave floor | 0 | 107 | 0 | 107 |
| 0x0551 | cave floor | 0 | 107 | 0 | 107 |
| 0x0ECC | bones | 1 | 107 | 0 | 107 |
| 0x0553 | cave floor | 0 | 106 | 0 | 106 |
| 0x0EDF | grave | 0 | 105 | 0 | 105 |
| 0x0EE1 | grave | 0 | 105 | 0 | 105 |
| 0x31C0 |  | 0 | 0 | 104 | 104 |
| 0x1B1C | spine | 1 | 104 | 0 | 104 |
| 0x0585 | slate roof | 0 | 104 | 0 | 104 |
| 0x0CEC | vines | 0 | 103 | 0 | 103 |
| 0x0AC7 | carpet | 0 | 103 | 0 | 103 |
| 0x1B15 | pelvis bone | 1 | 102 | 0 | 102 |
| 0x0AED |  | 0 | 0 | 102 | 102 |
| 0x0AEC |  | 0 | 0 | 102 | 102 |
| 0x0CF1 | vines | 0 | 101 | 0 | 101 |
| 0x0A22 | lantern | 1 | 101 | 0 | 101 |
| 0x31B7 |  | 0 | 0 | 100 | 100 |
| 0x0584 | slate roof | 0 | 99 | 0 | 99 |
| 0x087A | wooden ramp | 5 | 98 | 0 | 98 |
| 0x31B3 |  | 0 | 0 | 98 | 98 |
| 0x0AB3 | rug | 0 | 98 | 0 | 98 |
| 0x0CF0 | vines | 0 | 97 | 0 | 97 |
| 0x0552 | cave floor | 0 | 96 | 0 | 96 |
| 0x360B |  | 5 | 0 | 96 | 96 |
| 0x2960 | wooden floor | 0 | 95 | 0 | 95 |
| 0x34F9 | waterfall | 10 | 95 | 0 | 95 |
| 0x3286 | lava | 1 | 95 | 0 | 95 |
| 0x4575 | Icicle Large East | 0 | 94 | 0 | 94 |
| 0x0768 | stone | 5 | 0 | 93 | 93 |
| 0x31B6 |  | 0 | 0 | 91 | 91 |
| 0x56D7 | carpet | 1 | 91 | 0 | 91 |
| 0x56D5 | carpet | 1 | 91 | 0 | 91 |
| 0x0767 | stone | 5 | 0 | 90 | 90 |
| 0x0766 | stone | 5 | 0 | 90 | 90 |
| 0x0AEB |  | 0 | 0 | 89 | 89 |
| 0x0C91 | untrimmed hedge | 6 | 89 | 0 | 89 |
| 0x2210 | crystal | 10 | 87 | 0 | 87 |
| 0x56D6 | carpet | 1 | 87 | 0 | 87 |
| 0x0AF9 | carpet fringe | 0 | 86 | 0 | 86 |
| 0x0AC6 | carpet | 0 | 86 | 0 | 86 |
| 0x2E29 | acid | 0 | 85 | 0 | 85 |
| 0x3EEA |  | 1 | 0 | 85 | 85 |
| 0x2E26 | acid | 0 | 85 | 0 | 85 |
| 0x057C | stone roof | 0 | 85 | 0 | 85 |
| 0x2E21 | acid | 0 | 85 | 0 | 85 |
| 0x2E27 | acid | 0 | 85 | 0 | 85 |
| 0x0AF7 |  | 0 | 0 | 84 | 84 |
| 0x0765 | stone | 5 | 0 | 83 | 83 |
| 0x2E20 | acid | 0 | 83 | 0 | 83 |
| 0x19B7 | iron ore | 1 | 82 | 0 | 82 |
| 0x1B08 | floor cracks | 0 | 82 | 0 | 82 |
| 0x10D7 | small web | 0 | 81 | 0 | 81 |
| 0x56D9 | carpet | 1 | 81 | 0 | 81 |
| 0x56D8 | carpet | 1 | 81 | 0 | 81 |
| 0x0AD6 |  | 0 | 0 | 81 | 81 |
| 0x328D | lava | 1 | 80 | 0 | 80 |
| 0x0565 | stone roof | 0 | 80 | 0 | 80 |
| 0x1318 | lava | 0 | 80 | 0 | 80 |
| 0x0CED | vines | 0 | 80 | 0 | 80 |
| 0x1CD9 | blood | 0 | 80 | 0 | 80 |
| 0x070D | marble stairs | 5 | 79 | 0 | 79 |
| 0x3505 | waterfall | 3 | 79 | 0 | 79 |
| 0x04C3 | wooden logs | 0 | 78 | 0 | 78 |
| 0x10D3 | small web | 0 | 78 | 0 | 78 |
| 0x04E2 | bricks | 0 | 77 | 0 | 77 |
| 0x46CC | ruin Floor01 | 0 | 77 | 0 | 77 |
| 0x0145 | log post | 20 | 77 | 0 | 77 |
| 0x0562 | stone roof | 0 | 77 | 0 | 77 |
| 0x222C | crystal | 10 | 76 | 0 | 76 |
| 0x0911 | dirt patch | 0 | 76 | 0 | 76 |
| 0x326B | swamp | 0 | 76 | 0 | 76 |
| 0x1E02 | mud | 0 | 75 | 0 | 75 |
| 0x31B0 |  | 0 | 0 | 75 | 75 |
| 0x0568 | stone roof | 0 | 75 | 0 | 75 |
| 0x34F3 | waterfall | 10 | 74 | 0 | 74 |
| 0x0EE2 | grave | 0 | 74 | 0 | 74 |
| 0x0EE0 | grave | 0 | 74 | 0 | 74 |
| 0x2E13 | acid | 0 | 74 | 0 | 74 |
| 0x328B | lava | 1 | 73 | 0 | 73 |
| 0x223B | broken crystals | 1 | 73 | 0 | 73 |
| 0x418A | Floor Gold East | 0 | 73 | 0 | 73 |
| 0x0566 | stone roof | 0 | 73 | 0 | 73 |
| 0x31BC |  | 0 | 0 | 72 | 72 |
| 0x2E23 | acid | 0 | 72 | 0 | 72 |
| 0x2E24 | acid | 0 | 72 | 0 | 72 |
| 0x051A | stone pavers | 0 | 72 | 0 | 72 |
| 0x3E8B | deck | 3 | 72 | 0 | 72 |
| 0x1223 | pedestal | 9 | 70 | 0 | 70 |
| 0x3292 | lava | 1 | 70 | 0 | 70 |
| 0x2E0B | acid | 0 | 69 | 0 | 69 |
| 0x31B8 |  | 0 | 0 | 69 | 69 |
| 0x3275 | swamp | 0 | 69 | 0 | 69 |
| 0x0AD8 |  | 0 | 0 | 68 | 68 |
| 0x1250 | rack | 3 | 68 | 0 | 68 |
| 0x0AD9 | carpet | 0 | 67 | 0 | 67 |
| 0x02DA | crystal | 5 | 66 | 0 | 66 |
| 0x31B9 |  | 0 | 0 | 66 | 66 |
| 0x1B02 | floor cracks | 0 | 66 | 0 | 66 |
| 0x0AC0 | carpet | 0 | 66 | 0 | 66 |
| 0x04FE | flagstones | 0 | 66 | 0 | 66 |
| 0x32AA | lava | 0 | 66 | 0 | 66 |
| 0x1258 | rack | 3 | 65 | 0 | 65 |
| 0x0529 | sandstone floor | 0 | 65 | 0 | 65 |
| 0x31BA |  | 0 | 0 | 64 | 64 |
| 0x3299 | lava | 1 | 64 | 0 | 64 |
| 0x46CD | ruin Floor02 | 0 | 64 | 0 | 64 |
| 0x0497 | marble floor | 0 | 63 | 0 | 63 |
| 0x1259 | rack | 3 | 63 | 0 | 63 |
| 0x1251 | rack | 3 | 63 | 0 | 63 |
| 0x31B5 |  | 0 | 0 | 62 | 62 |
| 0x3290 | lava | 1 | 62 | 0 | 62 |
| 0x0AD7 |  | 0 | 0 | 62 | 62 |
| 0x1B9F | refuse | 0 | 61 | 0 | 61 |
| 0x10D6 | small web | 0 | 61 | 0 | 61 |
| 0x31BB |  | 0 | 0 | 61 | 61 |
| 0x049B | floor tile | 0 | 61 | 0 | 61 |
| 0x31BD |  | 0 | 0 | 61 | 61 |
| 0x3274 | swamp | 0 | 58 | 0 | 58 |
| 0x10D4 | small web | 0 | 58 | 0 | 58 |
| 0x1D94 | blood | 0 | 58 | 0 | 58 |
| 0x1A07 | chains | 0 | 58 | 0 | 58 |
| 0x070C | marble stairs | 5 | 57 | 0 | 57 |
| 0x1D96 | blood | 0 | 57 | 0 | 57 |
| 0x0FAC | fire pit | 0 | 57 | 0 | 57 |
| 0x2F4F |  | 0 | 0 | 57 | 57 |
| 0x0AAD | Rug East | 0 | 56 | 0 | 56 |
| 0x329B | lava | 1 | 56 | 0 | 56 |
| 0x0AB6 | rug | 0 | 56 | 0 | 56 |
| 0x0E31 | brazier | 0 | 56 | 0 | 56 |
| 0x31BF |  | 0 | 0 | 55 | 55 |
| 0x0AB4 | rug | 0 | 54 | 0 | 54 |
| 0x2CEE | simple floor tile | 0 | 54 | 0 | 54 |
| 0x326E | swamp | 0 | 53 | 0 | 53 |
| 0x0AB5 | rug | 0 | 53 | 0 | 53 |
| 0x1D93 | blood | 0 | 53 | 0 | 53 |
| 0x1B14 | jaw bone | 1 | 53 | 0 | 53 |
| 0x329A | lava | 1 | 53 | 0 | 53 |
| 0x087B | wooden ramp | 5 | 52 | 0 | 52 |
| 0x0AB7 | rug | 0 | 52 | 0 | 52 |
| 0x360D |  | 5 | 0 | 51 | 51 |
| 0x328A | lava | 1 | 51 | 0 | 51 |
| 0x0720 | wood | 5 | 0 | 51 | 51 |
| 0x360C |  | 5 | 0 | 51 | 51 |
| 0x2E12 | acid | 0 | 50 | 0 | 50 |
| 0x04EB | blue tiles | 0 | 50 | 0 | 50 |
| 0x229D |  | 0 | 0 | 50 | 50 |
| 0x076B | sandstone | 5 | 50 | 0 | 50 |
| 0x17BF | snow | 0 | 49 | 0 | 49 |
| 0x04ED | blue tiles | 0 | 49 | 0 | 49 |
| 0x2E05 | acid | 0 | 49 | 0 | 49 |
| 0x2E08 | acid | 0 | 49 | 0 | 49 |
| 0x076A | sandstone | 5 | 49 | 0 | 49 |
| 0x1B01 | floor cracks | 0 | 48 | 0 | 48 |
| 0x0572 | stone roof | 0 | 48 | 0 | 48 |
| 0x3293 | lava | 1 | 48 | 0 | 48 |
| 0x1E01 | mud | 0 | 48 | 0 | 48 |
| 0x17CB | snow | 0 | 48 | 0 | 48 |
| 0x0AAB | Rug East | 0 | 47 | 0 | 47 |
| 0x0A15 | lantern | 2 | 47 | 0 | 47 |
| 0x360A |  | 5 | 0 | 47 | 47 |
| 0x1B03 | floor cracks | 0 | 47 | 0 | 47 |
| 0x1D95 | blood | 0 | 47 | 0 | 47 |
| 0x2F4B |  | 0 | 0 | 46 | 46 |
| 0x3459 | lava | 0 | 46 | 0 | 46 |
| 0x056F | stone roof | 0 | 46 | 0 | 46 |
| 0x056E | stone roof | 0 | 46 | 0 | 46 |
| 0x3297 | lava | 1 | 46 | 0 | 46 |
| 0x1D92 | blood | 0 | 46 | 0 | 46 |
| 0x3548 | lava | 1 | 46 | 0 | 46 |
| 0x363A |  | 20 | 0 | 45 | 45 |
| 0x19AA | brazier | 17 | 45 | 0 | 45 |
| 0x0E23 | bloody water | 1 | 45 | 0 | 45 |
| 0x0AF3 |  | 0 | 0 | 45 | 45 |
| 0x1B07 | floor cracks | 0 | 45 | 0 | 45 |
| 0x46CE | ruin Floor03 | 0 | 45 | 0 | 45 |
| 0x0AF5 |  | 0 | 0 | 44 | 44 |
| 0x56B8 | carpet | 1 | 44 | 0 | 44 |
| 0x087C | wooden ramp | 5 | 44 | 0 | 44 |
| 0x31B1 |  | 0 | 0 | 44 | 44 |
| 0x056C | stone roof | 0 | 44 | 0 | 44 |
| 0x04EC | blue tiles | 0 | 44 | 0 | 44 |
| 0x0E33 | brazier | 0 | 44 | 0 | 44 |
| 0x04EA | blue tiles | 0 | 43 | 0 | 43 |
| 0x0ABD | carpet | 0 | 43 | 0 | 43 |
| 0x0721 | wood | 10 | 0 | 43 | 43 |
| 0x10CB | web | 0 | 43 | 0 | 43 |
| 0x229A |  | 0 | 0 | 43 | 43 |
| 0x10CC | web | 0 | 43 | 0 | 43 |
| 0x10CA | web | 0 | 42 | 0 | 42 |
| 0x46CF | ruin Floor04 | 0 | 42 | 0 | 42 |
| 0x087D | wooden ramp | 5 | 42 | 0 | 42 |
| 0x2D39 | wooden floor | 0 | 42 | 0 | 42 |
| 0x0A92 | folded sheet  | 1 | 42 | 0 | 42 |
| 0x2E22 | acid | 0 | 42 | 0 | 42 |
| 0x3294 | lava | 1 | 41 | 0 | 41 |
| 0x327A | swamp | 0 | 41 | 0 | 41 |
| 0x0AC1 | carpet | 0 | 41 | 0 | 41 |
| 0x10F2 | pile of garbage | 0 | 41 | 0 | 41 |
| 0x3B63 |  | 0 | 0 | 41 | 41 |
| 0x32B0 | lava | 0 | 41 | 0 | 41 |
| 0x10CD | web | 0 | 41 | 0 | 41 |
| 0x17BD | snow | 0 | 40 | 0 | 40 |
| 0x3636 |  | 3 | 0 | 40 | 40 |
| 0x0ED3 | grave | 0 | 40 | 0 | 40 |
| 0x08FA | stone rail | 5 | 40 | 0 | 40 |
| 0x2226 | crystal | 10 | 40 | 0 | 40 |
| 0x049D | sandstone paver | 0 | 39 | 0 | 39 |
| 0x3549 | lava | 1 | 39 | 0 | 39 |
| 0x1312 | lava | 0 | 39 | 0 | 39 |
| 0x1449 | loose grain | 1 | 39 | 0 | 39 |
| 0x328E | lava | 1 | 39 | 0 | 39 |
| 0x326A | swamp | 0 | 39 | 0 | 39 |
| 0x1881 | spilled flour | 1 | 39 | 0 | 39 |
| 0x0A93 | folded sheet  | 1 | 38 | 0 | 38 |
| 0x3547 | lava | 1 | 38 | 0 | 38 |
| 0x2E14 | acid | 0 | 38 | 0 | 38 |
| 0x3B66 |  | 0 | 0 | 38 | 38 |
| 0x0505 | wooden logs | 0 | 38 | 0 | 38 |
| 0x10D2 | small web | 0 | 38 | 0 | 38 |
| 0x31BE |  | 0 | 0 | 37 | 37 |
| 0x1DFD | dirt | 0 | 37 | 0 | 37 |
| 0x10DA | cocoon | 6 | 37 | 0 | 37 |
| 0x1BB4 | refuse | 0 | 37 | 0 | 37 |
| 0x10B0 | hanging pole | 5 | 37 | 0 | 37 |
| 0x12EE | lava | 0 | 36 | 0 | 36 |
| 0x0503 | flagstones | 0 | 36 | 0 | 36 |
| 0x10B8 | web | 0 | 35 | 0 | 35 |
| 0x0881 | wooden rail | 5 | 35 | 0 | 35 |
| 0x4187 | Floor Gold East | 0 | 35 | 0 | 35 |
| 0x0880 | wooden rail | 5 | 35 | 0 | 35 |
| 0x10B9 | web | 0 | 35 | 0 | 35 |
| 0x418B | Floor Gold East | 0 | 35 | 0 | 35 |
| 0x10D5 | small web | 0 | 35 | 0 | 35 |
| 0x4189 | Floor Gold East | 0 | 35 | 0 | 35 |
| 0x11EB | straw pillow | 1 | 35 | 0 | 35 |
| 0x1880 | spilled flour | 1 | 35 | 0 | 35 |
| 0x418D | Floor Gold East | 0 | 35 | 0 | 35 |
| 0x11E5 | grass mat | 1 | 35 | 0 | 35 |
| 0xAE2A | Wall Mine Beam | 1 | 35 | 0 | 35 |
| 0x0AAA | Rug East | 0 | 34 | 0 | 34 |
| 0x0506 | wooden logs | 0 | 34 | 0 | 34 |
| 0x3AFE |  | 1 | 0 | 34 | 34 |
| 0x354B | lava | 1 | 34 | 0 | 34 |
| 0x10BB | web | 0 | 34 | 0 | 34 |
| 0x11FD | cot | 2 | 34 | 0 | 34 |
| 0x56CC | carpet | 1 | 34 | 0 | 34 |
| 0x10DD | web | 6 | 34 | 0 | 34 |
| 0x11FE | cot | 2 | 34 | 0 | 34 |
| 0x10CF | web | 0 | 33 | 0 | 33 |
| 0x0AC9 | carpet | 0 | 33 | 0 | 33 |
| 0x10D0 | web | 0 | 33 | 0 | 33 |
| 0x3559 | lava | 1 | 33 | 0 | 33 |
| 0x3278 | swamp | 0 | 33 | 0 | 33 |
| 0x04FF | flagstones | 0 | 33 | 0 | 33 |
| 0x1BA0 | refuse | 2 | 33 | 0 | 33 |
| 0x11E8 | woven mat | 1 | 33 | 0 | 33 |
| 0x223C | broken crystals | 1 | 33 | 0 | 33 |
| 0x3271 | swamp | 0 | 33 | 0 | 33 |
| 0x0AE8 |  | 0 | 0 | 33 | 33 |
| 0x10BA | web | 0 | 33 | 0 | 33 |
| 0x0AEA |  | 0 | 0 | 33 | 33 |
| 0x3298 | lava | 1 | 33 | 0 | 33 |
| 0x17CC | snow | 0 | 33 | 0 | 33 |
| 0x0ACB | carpet | 0 | 33 | 0 | 33 |
| 0x344E | lava | 0 | 32 | 0 | 32 |
| 0x10D1 | web | 0 | 32 | 0 | 32 |
| 0x0D1C | grapevines | 0 | 32 | 0 | 32 |
| 0x56BC | carpet | 1 | 32 | 0 | 32 |
| 0x187F | spilled flour | 1 | 32 | 0 | 32 |
| 0x2E16 | acid | 0 | 32 | 0 | 32 |
| 0x10CE | web | 0 | 32 | 0 | 32 |
| 0x10BC | web | 0 | 32 | 0 | 32 |
| 0x1200 | cot | 2 | 32 | 0 | 32 |
| 0x04D7 | wooden planks | 0 | 0 | 31 | 31 |
| 0x0AF4 |  | 0 | 0 | 31 | 31 |
| 0x0912 | dirt patch | 0 | 31 | 0 | 31 |
| 0x10BD | web | 0 | 31 | 0 | 31 |
| 0x0C25 | broken furniture | 15 | 31 | 0 | 31 |
| 0x354A | lava | 1 | 30 | 0 | 30 |
| 0x32AC | lava | 0 | 30 | 0 | 30 |
| 0x1A03 | skeleton | 0 | 30 | 0 | 30 |
| 0x3B60 |  | 0 | 0 | 30 | 30 |
| 0x12F4 | lava | 0 | 30 | 0 | 30 |
| 0x10BF | web | 0 | 30 | 0 | 30 |
| 0x56DF | carpet | 1 | 30 | 0 | 30 |
| 0x0ACA | carpet | 0 | 30 | 0 | 30 |
| 0x56E1 | carpet | 1 | 30 | 0 | 30 |
| 0x11FF | cot | 2 | 30 | 0 | 30 |
| 0x2844 | sword display | 4 | 30 | 0 | 30 |
| 0x56CE | carpet | 1 | 30 | 0 | 30 |
| 0x0AAC | Rug East | 0 | 30 | 0 | 30 |
| 0x2F4A |  | 0 | 0 | 29 | 29 |
| 0x2F50 |  | 0 | 0 | 29 | 29 |
| 0x0E74 | cannon balls | 1 | 29 | 0 | 29 |
| 0x355C | lava | 1 | 29 | 0 | 29 |
| 0x4C2B | Sea Coral SOUTH | 1 | 29 | 0 | 29 |
| 0x10F3 | pile of garbage | 0 | 29 | 0 | 29 |
| 0x3B68 |  | 0 | 0 | 29 | 29 |
| 0x10DB | cocoon | 6 | 29 | 0 | 29 |
| 0x0AE7 |  | 0 | 0 | 29 | 29 |
| 0x10C0 | web | 0 | 29 | 0 | 29 |
| 0x10BE | web | 0 | 29 | 0 | 29 |
| 0x0AF2 |  | 0 | 0 | 29 | 29 |
| 0x10C1 | web | 0 | 29 | 0 | 29 |
| 0x0C3A | peg board | 0 | 29 | 0 | 29 |
| 0x1300 | lava | 0 | 29 | 0 | 29 |
| 0x17BE | snow | 0 | 28 | 0 | 28 |
| 0x3B61 |  | 0 | 0 | 28 | 28 |
| 0x4081 | decorative paver | 0 | 28 | 0 | 28 |
| 0x07C9 | wooden plank | 1 | 0 | 28 | 28 |
| 0x0E68 | glowing rune | 0 | 28 | 0 | 28 |
| 0x3B69 |  | 0 | 0 | 28 | 28 |
| 0x1B9D | twigs | 1 | 28 | 0 | 28 |
| 0x1EBD | wheat shea%ves/f% | 7 | 28 | 0 | 28 |
| 0x4083 | decorative paver | 0 | 28 | 0 | 28 |
| 0x3B67 |  | 0 | 0 | 28 | 28 |
| 0x0ACC | carpet | 0 | 28 | 0 | 28 |
| 0x10AD | hanging pole | 5 | 28 | 0 | 28 |
| 0x0C39 | peg board | 0 | 28 | 0 | 28 |
| 0x3B3B |  | 0 | 0 | 28 | 28 |
| 0x327D | swamp | 0 | 28 | 0 | 28 |
| 0x0429 | gruesome standard | 15 | 27 | 0 | 27 |
| 0x17C6 | snow | 0 | 27 | 0 | 27 |
| 0x130C | lava | 0 | 27 | 0 | 27 |
| 0x3B62 |  | 0 | 0 | 27 | 27 |
| 0x0002 | ankh | 12 | 27 | 0 | 27 |
| 0x354C | lava | 1 | 27 | 0 | 27 |
| 0x1BE2 | logs | 3 | 27 | 0 | 27 |
| 0x221C | crystal | 10 | 27 | 0 | 27 |
| 0x0815 | suspension bridge | 21 | 27 | 0 | 27 |
| 0x3236 | swamp | 0 | 27 | 0 | 27 |
| 0x1AA0 | hops | 1 | 27 | 0 | 27 |
| 0x3270 | swamp | 0 | 27 | 0 | 27 |
| 0x1B06 | floor cracks | 0 | 27 | 0 | 27 |
| 0x0C24 | broken furniture | 15 | 27 | 0 | 27 |
| 0x56CF | carpet | 1 | 27 | 0 | 27 |
| 0x56CD | carpet | 1 | 27 | 0 | 27 |
| 0x10DC | cocoon | 6 | 27 | 0 | 27 |
| 0x3B71 |  | 0 | 0 | 26 | 26 |
| 0x08CC | edging | 3 | 26 | 0 | 26 |
| 0x1A9E | hops | 1 | 26 | 0 | 26 |
| 0x0F3B | horse dung | 1 | 26 | 0 | 26 |
| 0x32F2 | pedestal | 5 | 26 | 0 | 26 |
| 0x10AF | hanging pole | 5 | 26 | 0 | 26 |
| 0x1BEE | gold ingots | 1 | 26 | 0 | 26 |
| 0x3B79 |  | 0 | 0 | 26 | 26 |
| 0x2E15 | acid | 0 | 26 | 0 | 26 |
| 0x3B00 |  | 1 | 0 | 26 | 26 |
| 0x3637 |  | 3 | 0 | 26 | 26 |
| 0x3B7A |  | 0 | 0 | 26 | 26 |
| 0x2E03 | acid | 0 | 26 | 0 | 26 |
| 0x17C0 | snow | 0 | 26 | 0 | 26 |
| 0x3B72 |  | 0 | 0 | 26 | 26 |
| 0x2D04 | large glowing beetl | 2 | 25 | 0 | 25 |
| 0x1CEF | entrails | 1 | 25 | 0 | 25 |
| 0x132D | lava | 0 | 25 | 0 | 25 |
| 0x11EA | straw pillow | 1 | 25 | 0 | 25 |
| 0x10C7 | web | 0 | 25 | 0 | 25 |
| 0x3B6A |  | 0 | 0 | 25 | 25 |
| 0x3B73 |  | 0 | 0 | 25 | 25 |
| 0x3511 | waterfall | 10 | 25 | 0 | 25 |
| 0x10C3 | web | 0 | 25 | 0 | 25 |
| 0x10C4 | web | 0 | 25 | 0 | 25 |
| 0x10B1 | hanging pole | 5 | 25 | 0 | 25 |
| 0x3B74 |  | 0 | 0 | 25 | 25 |
| 0x2E0D | acid | 0 | 25 | 0 | 25 |
| 0x1348 | lava | 0 | 25 | 0 | 25 |
| 0x99C8 | Marble Bar Exterior | 9 | 25 | 0 | 25 |
| 0x1F2B | brazier | 2 | 25 | 0 | 25 |
| 0x3B65 |  | 0 | 0 | 25 | 25 |
| 0x3B6B |  | 0 | 0 | 25 | 25 |
| 0x3B6C |  | 0 | 0 | 25 | 25 |
| 0x04FD | flagstones | 0 | 25 | 0 | 25 |
| 0x0E62 | glowing rune | 0 | 24 | 0 | 24 |
| 0x0D1E | grapevines | 11 | 24 | 0 | 24 |
| 0x1345 | lava | 0 | 24 | 0 | 24 |
| 0x0AE9 |  | 0 | 0 | 24 | 24 |
| 0x0ADF |  | 0 | 0 | 24 | 24 |
| 0x1E05 | footprints | 0 | 24 | 0 | 24 |
| 0x3B78 |  | 0 | 0 | 24 | 24 |
| 0x1BBC | refuse | 0 | 24 | 0 | 24 |
| 0x0A1A | hanging lantern | 0 | 24 | 0 | 24 |
| 0x10C5 | web | 0 | 24 | 0 | 24 |
| 0x1306 | lava | 0 | 24 | 0 | 24 |
| 0x3B70 |  | 0 | 0 | 24 | 24 |
| 0x09FE | wall sconce | 0 | 24 | 0 | 24 |
| 0x3B6F |  | 0 | 0 | 23 | 23 |
| 0x2D02 | large glowing beetl | 2 | 23 | 0 | 23 |
| 0x1BB0 | refuse | 2 | 23 | 0 | 23 |
| 0x04FC | flagstones | 0 | 23 | 0 | 23 |
| 0x1D04 | blood | 0 | 23 | 0 | 23 |
| 0x3B75 |  | 0 | 0 | 23 | 23 |
| 0x10C2 | web | 0 | 23 | 0 | 23 |
| 0x2206 | crystal | 10 | 23 | 0 | 23 |
| 0x1BBB | refuse | 2 | 23 | 0 | 23 |
| 0x325D | swamp | 0 | 23 | 0 | 23 |
| 0x3967 | field of paralysis | 12 | 23 | 0 | 23 |
| 0x1CF6 | blood | 0 | 22 | 0 | 22 |
| 0x0420 | gruesome standard | 15 | 22 | 0 | 22 |
| 0x24BB | paper lantern | 2 | 22 | 0 | 22 |
| 0x10C8 | web | 0 | 22 | 0 | 22 |
| 0x1915 | pillow | 1 | 22 | 0 | 22 |
| 0x1BD9 | boards | 2 | 22 | 0 | 22 |
| 0x0A03 | wall sconce | 0 | 22 | 0 | 22 |
| 0x0AE1 |  | 0 | 0 | 22 | 22 |
| 0x2F48 |  | 0 | 0 | 22 | 22 |
| 0x0816 | suspension bridge | 21 | 22 | 0 | 22 |
| 0x1CF7 | blood | 0 | 22 | 0 | 22 |
| 0x17C5 | snow | 0 | 22 | 0 | 22 |
| 0x1DFE | dirt | 0 | 22 | 0 | 22 |
| 0x32A7 | lava | 0 | 22 | 0 | 22 |
| 0x1D00 | blood | 0 | 22 | 0 | 22 |
| 0x10AC | hanging pole | 5 | 22 | 0 | 22 |
| 0x0886 | wooden rail | 5 | 22 | 0 | 22 |
| 0x1078 | pil%es/e% of hides | 1 | 22 | 0 | 22 |
| 0x132A | lava | 0 | 22 | 0 | 22 |
| 0x223A | broken crystals | 1 | 21 | 0 | 21 |
| 0x1BF7 | silver ingots | 1 | 21 | 0 | 21 |
| 0x2E06 | acid | 0 | 21 | 0 | 21 |
| 0x10C9 | web | 0 | 21 | 0 | 21 |
| 0x1324 | lava | 0 | 21 | 0 | 21 |
| 0x041F | gruesome standard | 15 | 21 | 0 | 21 |
| 0x2F49 |  | 0 | 0 | 21 | 21 |
| 0x0C3C | dried flowers | 1 | 21 | 0 | 21 |
| 0x3284 | swamp | 0 | 21 | 0 | 21 |
| 0x079F | cave steps | 22 | 21 | 0 | 21 |
| 0x11FB | fur%s% | 1 | 21 | 0 | 21 |
| 0x0ED8 | gravestone | 2 | 21 | 0 | 21 |
| 0x363B |  | 20 | 0 | 21 | 21 |
| 0x0001 | nodraw | 0 | 0 | 21 | 21 |
| 0x0AE2 |  | 0 | 0 | 21 | 21 |
| 0x10AE | hanging pole | 5 | 21 | 0 | 21 |
| 0x2E04 | acid | 0 | 21 | 0 | 21 |
| 0x2E07 | acid | 0 | 21 | 0 | 21 |
| 0x364E |  | 10 | 0 | 21 | 21 |
| 0x1872 | beef carcass | 10 | 20 | 0 | 20 |
| 0x14EC | map | 1 | 20 | 0 | 20 |
| 0x0AC2 | carpet fringe | 0 | 20 | 0 | 20 |
| 0x2E1A | acid | 0 | 20 | 0 | 20 |
| 0x1BBA | refuse | 0 | 20 | 0 | 20 |
| 0x0AD4 |  | 0 | 0 | 20 | 20 |
| 0x0887 | wooden rail | 5 | 20 | 0 | 20 |
| 0x3B01 |  | 1 | 0 | 20 | 20 |
| 0x3AFF |  | 1 | 0 | 20 | 20 |
| 0x11E6 | woven mat | 1 | 20 | 0 | 20 |
| 0x3B05 |  | 1 | 0 | 20 | 20 |
| 0x34ED | waterfall | 5 | 20 | 0 | 20 |
| 0x3B7C |  | 0 | 0 | 19 | 19 |
| 0x0AC3 | carpet fringe | 0 | 19 | 0 | 19 |
| 0x1BA2 | refuse | 0 | 19 | 0 | 19 |
| 0x0AC5 | carpet fringe | 0 | 19 | 0 | 19 |
| 0x0FEB | pentagram | 0 | 19 | 0 | 19 |
| 0x3273 | swamp | 0 | 19 | 0 | 19 |
| 0x0FEE | pentagram | 0 | 19 | 0 | 19 |
| 0x325B | swamp | 0 | 19 | 0 | 19 |
| 0x2E09 | acid | 0 | 19 | 0 | 19 |
| 0x1D07 | blood | 0 | 19 | 0 | 19 |
| 0x2F4C |  | 0 | 0 | 19 | 19 |
| 0x56C4 | carpet | 1 | 19 | 0 | 19 |
| 0x091A | beehive | 5 | 19 | 0 | 19 |
| 0x0FED | pentagram | 0 | 19 | 0 | 19 |
| 0x3B82 |  | 0 | 0 | 19 | 19 |
| 0x3261 | swamp | 0 | 19 | 0 | 19 |
| 0x3B83 |  | 0 | 0 | 19 | 19 |
| 0x56C2 | carpet | 1 | 19 | 0 | 19 |
| 0x1CF0 | brain | 1 | 19 | 0 | 19 |
| 0x0884 | wooden rail | 5 | 19 | 0 | 19 |
| 0x3B7D |  | 0 | 0 | 19 | 19 |
| 0x0FE8 | pentagram | 0 | 19 | 0 | 19 |
| 0x3B81 |  | 0 | 0 | 19 | 19 |
| 0x0FE7 | pentagram | 0 | 19 | 0 | 19 |
| 0x0AD3 |  | 0 | 0 | 19 | 19 |
| 0x1DFF | dirt | 0 | 19 | 0 | 19 |
| 0x0FE9 | pentagram | 0 | 19 | 0 | 19 |
| 0x0FEC | pentagram | 0 | 19 | 0 | 19 |
| 0x1184 | gravestone | 6 | 19 | 0 | 19 |
| 0x1D08 | blood | 0 | 19 | 0 | 19 |
| 0x0AB8 | rug | 0 | 19 | 0 | 19 |
| 0x3288 | lava | 1 | 19 | 0 | 19 |
| 0x0FE6 | pentagram | 0 | 19 | 0 | 19 |
| 0x2E25 | acid | 0 | 19 | 0 | 19 |
| 0x3B97 |  | 0 | 0 | 19 | 19 |
| 0x32A8 | lava | 0 | 18 | 0 | 18 |
| 0x14EE | rolled map | 1 | 18 | 0 | 18 |
| 0x3EE9 |  | 2 | 0 | 18 | 18 |
| 0x07FC | suspension bridge | 20 | 18 | 0 | 18 |
| 0x1327 | lava | 0 | 18 | 0 | 18 |
| 0x1CFE | blood | 0 | 18 | 0 | 18 |
| 0x10D8 | egg case web | 0 | 18 | 0 | 18 |
| 0x17C7 | snow | 0 | 18 | 0 | 18 |
| 0x0AD5 |  | 0 | 0 | 18 | 18 |
| 0x2E18 | acid | 0 | 18 | 0 | 18 |
| 0x10C6 | web | 0 | 18 | 0 | 18 |
| 0x1BB6 | refuse | 2 | 18 | 0 | 18 |
| 0x0D1F | grapevines | 11 | 18 | 0 | 18 |
| 0x3550 | lava | 1 | 18 | 0 | 18 |
| 0x0ABB | rug | 0 | 18 | 0 | 18 |
| 0x1CE8 | torso | 1 | 18 | 0 | 18 |
| 0x0DC0 | fishing pole | 1 | 18 | 0 | 18 |
| 0x1A05 | skeleton | 0 | 18 | 0 | 18 |
| 0x07FB | suspension bridge | 20 | 18 | 0 | 18 |
| 0x3B7E |  | 0 | 0 | 18 | 18 |
| 0x07B1 | cave steps | 11 | 18 | 0 | 18 |
| 0x0AE0 |  | 0 | 0 | 18 | 18 |
| 0x091B | bees | 0 | 18 | 0 | 18 |
| 0x327B | swamp | 0 | 18 | 0 | 18 |
| 0x0AD2 |  | 0 | 0 | 18 | 18 |
| 0x234D | rose of trinsic | 1 | 17 | 0 | 17 |
| 0x3996 | Field of fire | 12 | 17 | 0 | 17 |
| 0x2984 | wooden floor | 0 | 17 | 0 | 17 |
| 0x1CDE | body | 1 | 17 | 0 | 17 |
| 0x14ED | rolled map | 1 | 17 | 0 | 17 |
| 0x104B | clock | 1 | 17 | 0 | 17 |
| 0x418C | Floor Gold East02 | 0 | 17 | 0 | 17 |
| 0x3B02 |  | 1 | 0 | 17 | 17 |
| 0x418E | Floor Gold East | 0 | 17 | 0 | 17 |
| 0x1CE0 | torso | 1 | 17 | 0 | 17 |
| 0x19B8 | iron ore | 1 | 17 | 0 | 17 |
| 0x4188 | Floor Gold East01 | 0 | 17 | 0 | 17 |
| 0x17CA | snow | 0 | 17 | 0 | 17 |
| 0x56F2 | carpet | 1 | 17 | 0 | 17 |
| 0x4186 | Floor Gold East03 | 0 | 17 | 0 | 17 |
| 0x3277 | swamp | 0 | 17 | 0 | 17 |
| 0x327C | swamp | 0 | 17 | 0 | 17 |
| 0x0B45 | large vase | 9 | 17 | 0 | 17 |
| 0x0DCA | fishing net | 0 | 17 | 0 | 17 |
| 0x1CF4 | blood | 0 | 17 | 0 | 17 |
| 0x3BAD |  | 0 | 0 | 17 | 17 |
| 0x3B84 |  | 0 | 0 | 17 | 17 |
| 0x0ABA | rug | 0 | 17 | 0 | 17 |
| 0x1085 | necklace | 1 | 17 | 0 | 17 |
| 0x0A20 | lantern post | 20 | 17 | 0 | 17 |
| 0x2F51 |  | 0 | 0 | 17 | 17 |
| 0x2F4E |  | 0 | 0 | 17 | 17 |
| 0x24C1 | paper lantern | 10 | 17 | 0 | 17 |
| 0x2F52 |  | 0 | 0 | 17 | 17 |
| 0x17C8 | snow | 0 | 16 | 0 | 16 |
| 0x1214 | altar | 0 | 16 | 0 | 16 |
| 0x0EEF | gold coins | 0 | 16 | 0 | 16 |
| 0x1212 | altar | 0 | 16 | 0 | 16 |
| 0x0E5C | glowing rune | 0 | 16 | 0 | 16 |
| 0x1215 | altar | 0 | 16 | 0 | 16 |
| 0x1CED | heart | 1 | 16 | 0 | 16 |
| 0x1D03 | blood | 0 | 16 | 0 | 16 |
| 0x17C9 | snow | 0 | 16 | 0 | 16 |
| 0x4C2F | Sea Sponge SOUTH | 1 | 16 | 0 | 16 |
| 0x3B06 |  | 1 | 0 | 16 | 16 |
| 0x120E | altar | 0 | 16 | 0 | 16 |
| 0x3B87 |  | 0 | 0 | 16 | 16 |
| 0x1A01 | skeleton | 0 | 16 | 0 | 16 |
| 0x07F5 | suspension bridge | 20 | 16 | 0 | 16 |
| 0xAE39 | Wall Mine Arch East | 1 | 16 | 0 | 16 |
| 0x1CE2 | leg | 1 | 16 | 0 | 16 |
| 0x1210 | altar | 0 | 16 | 0 | 16 |
| 0x0AB9 | rug | 0 | 16 | 0 | 16 |
| 0x0E22 | bloody bandage%s% | 1 | 16 | 0 | 16 |
| 0x3552 | lava | 1 | 16 | 0 | 16 |
| 0x0F3C | horse dung | 1 | 16 | 0 | 16 |
| 0x3BA4 |  | 0 | 0 | 16 | 16 |
| 0x0C3E | dried flowers | 1 | 16 | 0 | 16 |
| 0x3B8A |  | 0 | 0 | 16 | 16 |
| 0x120F | altar | 0 | 16 | 0 | 16 |
| 0x1D10 | blood | 0 | 16 | 0 | 16 |
| 0x0781 | sandstone | 5 | 16 | 0 | 16 |
| 0x1CE9 | head | 1 | 16 | 0 | 16 |
| 0x1213 | altar | 0 | 16 | 0 | 16 |
| 0x1AA2 | hops cones | 1 | 16 | 0 | 16 |
| 0x17D3 | teleporter | 0 | 16 | 0 | 16 |
| 0x1BB7 | refuse | 2 | 16 | 0 | 16 |
| 0x3B86 |  | 0 | 0 | 16 | 16 |
| 0x325A | swamp | 0 | 16 | 0 | 16 |
| 0x1211 | altar | 0 | 16 | 0 | 16 |
| 0x087E | wooden rail | 5 | 16 | 0 | 16 |
| 0x3B85 |  | 0 | 0 | 16 | 16 |
| 0x0C41 | dried herbs | 1 | 16 | 0 | 16 |
| 0x56F4 | carpet | 1 | 16 | 0 | 16 |
| 0x2F47 |  | 0 | 0 | 16 | 16 |
| 0x3B8B |  | 0 | 0 | 16 | 16 |
| 0x09ED | kettle | 5 | 16 | 0 | 16 |
| 0x116C | gravestone | 2 | 16 | 0 | 16 |
| 0x3E79 | ship | 3 | 15 | 0 | 15 |
| 0x1CF8 | blood | 0 | 15 | 0 | 15 |
| 0x1BB9 | refuse | 2 | 15 | 0 | 15 |
| 0x180A | teleporter | 0 | 15 | 0 | 15 |
| 0x2F44 |  | 0 | 0 | 15 | 15 |
| 0x3BA8 |  | 0 | 0 | 15 | 15 |
| 0x398C | Field of fire | 12 | 15 | 0 | 15 |
| 0x3BA0 |  | 0 | 0 | 15 | 15 |
| 0x3553 | lava | 1 | 15 | 0 | 15 |
| 0x3551 | lava | 1 | 15 | 0 | 15 |
| 0x34D1 | ripples | 0 | 15 | 0 | 15 |
| 0x07BA | carpeted rostrum | 10 | 15 | 0 | 15 |
| 0x1CFF | blood | 0 | 15 | 0 | 15 |
| 0x1806 | teleporter | 0 | 15 | 0 | 15 |
| 0x17EB | teleporter | 0 | 15 | 0 | 15 |
| 0x3BAB |  | 0 | 0 | 15 | 15 |
| 0x1CF1 | blood | 0 | 15 | 0 | 15 |
| 0x3B8C |  | 0 | 0 | 15 | 15 |
| 0x07FD | suspension bridge | 21 | 15 | 0 | 15 |
| 0x3B91 |  | 0 | 0 | 15 | 15 |
| 0x3BAC |  | 0 | 0 | 15 | 15 |
| 0x1B80 | skeleton | 4 | 15 | 0 | 15 |
| 0x180D | teleporter | 0 | 15 | 0 | 15 |
| 0x0FEA | pentagram | 0 | 15 | 0 | 15 |
| 0x364F |  | 15 | 0 | 15 | 15 |
| 0x3B89 |  | 0 | 0 | 15 | 15 |
| 0x0498 | marble floor | 0 | 15 | 0 | 15 |
| 0x180C | teleporter | 0 | 15 | 0 | 15 |
| 0x3B8F |  | 0 | 0 | 15 | 15 |
| 0x3BA7 |  | 0 | 0 | 15 | 15 |
| 0x087F | wooden rail | 5 | 15 | 0 | 15 |
| 0x354D | lava | 1 | 15 | 0 | 15 |
| 0x3B90 |  | 0 | 0 | 15 | 15 |
| 0x1EA6 | net | 15 | 15 | 0 | 15 |
| 0x3BA6 |  | 0 | 0 | 15 | 15 |
| 0x17F1 | teleporter | 0 | 15 | 0 | 15 |
| 0x4C2C | Sea Coral SOUTH | 1 | 15 | 0 | 15 |
| 0x0ED4 | gravestone | 5 | 15 | 0 | 15 |
| 0x0D3B | fallen branch | 0 | 15 | 0 | 15 |
| 0x1805 | teleporter | 0 | 15 | 0 | 15 |
| 0x1BA1 | refuse | 2 | 15 | 0 | 15 |
| 0x0C7A | canteloupe%s% | 1 | 15 | 0 | 15 |
| 0x131E | lava | 0 | 15 | 0 | 15 |
| 0x1BB5 | refuse | 2 | 15 | 0 | 15 |
| 0x1A47 | lava | 10 | 15 | 0 | 15 |
| 0x1808 | teleporter | 0 | 15 | 0 | 15 |
| 0x0A04 | wall sconce | 0 | 14 | 0 | 14 |
| 0x1B9E | twigs | 1 | 14 | 0 | 14 |
| 0x17EA | teleporter | 0 | 14 | 0 | 14 |
| 0x134B | lava | 0 | 14 | 0 | 14 |
| 0x1BB1 | refuse | 0 | 14 | 0 | 14 |
| 0x17EF | teleporter | 0 | 14 | 0 | 14 |
| 0x0ED7 | gravestone | 2 | 14 | 0 | 14 |
| 0x2F45 |  | 0 | 0 | 14 | 14 |
| 0x1C95 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x1C94 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x1216 | altar | 0 | 14 | 0 | 14 |
| 0x1A02 | skeleton | 0 | 14 | 0 | 14 |
| 0x17D0 | teleporter | 0 | 14 | 0 | 14 |
| 0x3818 | energy | 0 | 14 | 0 | 14 |
| 0x1C93 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x3BAA |  | 0 | 0 | 14 | 14 |
| 0x4C30 | Sea Sponge SOUTH | 1 | 14 | 0 | 14 |
| 0x3BA5 |  | 0 | 0 | 14 | 14 |
| 0x3B93 |  | 0 | 0 | 14 | 14 |
| 0x3B0A |  | 1 | 0 | 14 | 14 |
| 0x3BA2 |  | 0 | 0 | 14 | 14 |
| 0x1BBD | refuse | 2 | 14 | 0 | 14 |
| 0x3B8E |  | 0 | 0 | 14 | 14 |
| 0x328C | lava | 1 | 14 | 0 | 14 |
| 0x1BDF | logs | 3 | 14 | 0 | 14 |
| 0x1C92 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x1C90 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x1A9A | flax | 1 | 14 | 0 | 14 |
| 0x1D11 | blood | 0 | 14 | 0 | 14 |
| 0x11FA | fur%s% | 1 | 14 | 0 | 14 |
| 0x0E32 | brazier | 0 | 14 | 0 | 14 |
| 0x0C3B | dried flowers | 1 | 14 | 0 | 14 |
| 0x0B46 | vase | 5 | 14 | 0 | 14 |
| 0x3612 |  | 15 | 0 | 14 | 14 |
| 0x1D0E | blood | 0 | 14 | 0 | 14 |
| 0x17D6 | teleporter | 0 | 14 | 0 | 14 |
| 0x2F46 |  | 0 | 0 | 14 | 14 |
| 0x1D09 | blood | 0 | 14 | 0 | 14 |
| 0x17F2 | teleporter | 0 | 14 | 0 | 14 |
| 0x1A08 | chains | 0 | 14 | 0 | 14 |
| 0x1CDD | arm | 1 | 14 | 0 | 14 |
| 0x1E00 | dirt | 0 | 14 | 0 | 14 |
| 0x1EA5 | net | 15 | 14 | 0 | 14 |
| 0x1C91 | sarcophagus | 2 | 14 | 0 | 14 |
| 0x234C | rose of trinsic | 1 | 14 | 0 | 14 |
| 0x3263 | swamp | 0 | 14 | 0 | 14 |
| 0x17ED | teleporter | 0 | 14 | 0 | 14 |
| 0x0A25 | lantern | 1 | 14 | 0 | 14 |
| 0x07F2 | suspension bridge | 20 | 14 | 0 | 14 |
| 0x361B |  | 0 | 0 | 14 | 14 |
| 0x0F88 | Nightshade | 1 | 14 | 0 | 14 |
| 0x1D0B | blood | 0 | 13 | 0 | 13 |
| 0x32A9 | lava | 0 | 13 | 0 | 13 |
| 0x0AAE | Rug East | 0 | 13 | 0 | 13 |
| 0x3287 | lava | 1 | 13 | 0 | 13 |
| 0x3554 | lava | 1 | 13 | 0 | 13 |
| 0x1089 | necklace | 1 | 13 | 0 | 13 |
| 0x17F0 | teleporter | 0 | 13 | 0 | 13 |
| 0x0EE9 | clean bandage%s% | 1 | 13 | 0 | 13 |
| 0x3BA1 |  | 0 | 0 | 13 | 13 |
| 0x3B3D |  | 0 | 0 | 13 | 13 |
| 0x180B | teleporter | 0 | 13 | 0 | 13 |
| 0x355B | lava | 1 | 13 | 0 | 13 |
| 0x0C3D | dried flowers | 1 | 13 | 0 | 13 |
| 0x1079 | pil%es/e% of hides | 1 | 13 | 0 | 13 |
| 0x1B7C | skeleton | 4 | 13 | 0 | 13 |
| 0x105A | sextant parts | 1 | 13 | 0 | 13 |
| 0x325C | swamp | 0 | 13 | 0 | 13 |
| 0x56E0 | carpet | 1 | 13 | 0 | 13 |
| 0x1BAE | refuse | 0 | 13 | 0 | 13 |
| 0x1BAF | refuse | 2 | 13 | 0 | 13 |
| 0x1086 | bracelet | 1 | 13 | 0 | 13 |
| 0x17EC | teleporter | 0 | 13 | 0 | 13 |
| 0x56C1 | carpet | 1 | 13 | 0 | 13 |
| 0x0ED6 | gravestone | 5 | 13 | 0 | 13 |
| 0x0AB1 | Rug East | 0 | 13 | 0 | 13 |
| 0x0A16 | lantern | 1 | 13 | 0 | 13 |
| 0x09D1 | grape bunch%es% | 1 | 13 | 0 | 13 |
| 0x1CE4 | legs | 1 | 13 | 0 | 13 |
| 0x56C6 | carpet | 1 | 13 | 0 | 13 |
| 0x56DE | carpet | 1 | 13 | 0 | 13 |
| 0x1CFC | blood | 0 | 13 | 0 | 13 |
| 0x12CB | bust | 0 | 13 | 0 | 13 |
| 0x1CE1 | head | 1 | 13 | 0 | 13 |
| 0x1D0F | blood | 0 | 13 | 0 | 13 |
| 0x3B94 |  | 0 | 0 | 13 | 13 |
| 0x0A1F | lantern post | 20 | 13 | 0 | 13 |
| 0x1E06 | footprints | 0 | 13 | 0 | 13 |
| 0x3B95 |  | 0 | 0 | 13 | 13 |
| 0x1807 | teleporter | 0 | 13 | 0 | 13 |
| 0x1E04 | footprints | 0 | 13 | 0 | 13 |
| 0x1CFA | blood | 0 | 13 | 0 | 13 |
| 0x56C3 | carpet | 1 | 13 | 0 | 13 |
| 0x1C77 | sarcophagus | 2 | 12 | 0 | 12 |
| 0x17D5 | teleporter | 0 | 12 | 0 | 12 |
| 0x3283 | swamp | 0 | 12 | 0 | 12 |
| 0x1165 | gravestone | 2 | 12 | 0 | 12 |
| 0x1508 | decorative armor | 15 | 12 | 0 | 12 |
| 0x1C7A | sarcophagus | 2 | 12 | 0 | 12 |
| 0x104C | clock | 1 | 12 | 0 | 12 |
| 0x09FF | wall sconce | 0 | 12 | 0 | 12 |
| 0x1330 | lava | 0 | 12 | 0 | 12 |
| 0x4082 | decorative paver | 0 | 12 | 0 | 12 |
| 0x3265 | swamp | 0 | 12 | 0 | 12 |
| 0x1C7B | sarcophagus  | 2 | 12 | 0 | 12 |
| 0x246D | bamboo | 20 | 12 | 0 | 12 |
| 0x17CF | teleporter | 0 | 12 | 0 | 12 |
| 0x17D2 | teleporter | 0 | 12 | 0 | 12 |
| 0x355A | lava | 1 | 12 | 0 | 12 |
| 0x14EB | map | 1 | 12 | 0 | 12 |
| 0x0DE3 | campfire | 1 | 12 | 0 | 12 |
| 0x1C7C | sarcophagus | 2 | 12 | 0 | 12 |
| 0x2D70 | small elven tapestr | 0 | 12 | 0 | 12 |
| 0x17C4 | snow | 0 | 12 | 0 | 12 |
| 0x17D4 | teleporter | 0 | 12 | 0 | 12 |
| 0x1BAA | refuse | 2 | 12 | 0 | 12 |
| 0x3272 | swamp | 0 | 12 | 0 | 12 |
| 0x3462 | lava | 0 | 12 | 0 | 12 |
| 0x1512 | decorative armor | 15 | 12 | 0 | 12 |
| 0x56C7 | carpet | 1 | 12 | 0 | 12 |
| 0x1CEA | body part | 1 | 12 | 0 | 12 |
| 0x0E65 | glowing rune | 0 | 12 | 0 | 12 |
| 0x1D9F | torso | 1 | 12 | 0 | 12 |
| 0x1D0D | blood | 0 | 12 | 0 | 12 |
| 0x246E | bamboo | 20 | 12 | 0 | 12 |
| 0x351D | waterfall | 3 | 12 | 0 | 12 |
| 0x297E | wooden floor | 0 | 12 | 0 | 12 |
| 0x17D1 | teleporter | 0 | 12 | 0 | 12 |
| 0x1C79 | sarcophagus | 2 | 12 | 0 | 12 |
| 0x07F9 | suspension bridge | 20 | 12 | 0 | 12 |
| 0x1034 | saw | 1 | 12 | 0 | 12 |
| 0x17D7 | teleporter | 0 | 12 | 0 | 12 |
| 0x1183 | gravestone | 6 | 12 | 0 | 12 |
| 0x1C78 | sarcophagus | 2 | 12 | 0 | 12 |
| 0x1415 | platemail | 1 | 12 | 0 | 12 |
| 0x0817 | suspension bridge | 21 | 12 | 0 | 12 |
| 0x3B04 |  | 1 | 0 | 12 | 12 |
| 0x1167 | gravestone | 2 | 12 | 0 | 12 |
| 0x354F | lava | 1 | 12 | 0 | 12 |
| 0x4301 | Large Stairs South | 5 | 12 | 0 | 12 |
| 0x0DC8 | fishing net | 1 | 12 | 0 | 12 |
| 0x3517 | waterfall | 10 | 12 | 0 | 12 |
| 0x0DC9 | fishing net | 0 | 12 | 0 | 12 |
| 0x3264 | swamp | 0 | 11 | 0 | 11 |
| 0x49E0 | Pulley Crane NORTH | 10 | 11 | 0 | 11 |
| 0x1871 | beef carcass | 10 | 11 | 0 | 11 |
| 0x3B6E |  | 0 | 0 | 11 | 11 |
| 0x1A9F | hops | 1 | 11 | 0 | 11 |
| 0x32A6 | lava | 0 | 11 | 0 | 11 |
| 0x3B77 |  | 0 | 0 | 11 | 11 |
| 0x0E76 | bag | 2 | 11 | 0 | 11 |
| 0x117C | gravestone | 2 | 11 | 0 | 11 |
| 0x09BE | silverware | 1 | 11 | 0 | 11 |
| 0x49DF | Pulley Crane NORTH | 10 | 11 | 0 | 11 |
| 0x1D0A | blood | 0 | 11 | 0 | 11 |
| 0x3B99 |  | 0 | 0 | 11 | 11 |
| 0x1CE3 | body part | 1 | 11 | 0 | 11 |
| 0x56D1 | carpet | 1 | 11 | 0 | 11 |
| 0x1CA7 | sarcophagus | 2 | 11 | 0 | 11 |
| 0x1178 | gravestone | 2 | 11 | 0 | 11 |
| 0x49E5 | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x1CA9 | sarcophagus | 2 | 11 | 0 | 11 |
| 0x56D4 | carpet | 1 | 11 | 0 | 11 |
| 0x13A7 | pillow | 1 | 11 | 0 | 11 |
| 0x49E4 | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x49DE | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x0818 | suspension bridge | 21 | 11 | 0 | 11 |
| 0x0E5F | glowing rune | 0 | 11 | 0 | 11 |
| 0x09D4 | silverware | 1 | 11 | 0 | 11 |
| 0x1CE6 | body | 1 | 11 | 0 | 11 |
| 0x1CAB | sarcophagus | 2 | 11 | 0 | 11 |
| 0x56D3 | carpet | 1 | 11 | 0 | 11 |
| 0x17C1 | snow | 0 | 11 | 0 | 11 |
| 0x1CA8 | sarcophagus | 2 | 11 | 0 | 11 |
| 0x2207 | crystal | 10 | 11 | 0 | 11 |
| 0x1CAA | sarcophagus | 2 | 11 | 0 | 11 |
| 0x11F9 | fur%s% | 1 | 11 | 0 | 11 |
| 0x1559 | water | 8 | 11 | 0 | 11 |
| 0x11F4 | fur%s% | 1 | 11 | 0 | 11 |
| 0x56D2 | carpet | 1 | 11 | 0 | 11 |
| 0x49E3 | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x1CDA | blood | 0 | 11 | 0 | 11 |
| 0x3452 | lava | 0 | 11 | 0 | 11 |
| 0x1CAC | sarcophagus | 2 | 11 | 0 | 11 |
| 0x13AB | pillow | 1 | 11 | 0 | 11 |
| 0x1CF2 | blood | 0 | 11 | 0 | 11 |
| 0x17C3 | snow | 0 | 11 | 0 | 11 |
| 0x3B03 |  | 1 | 0 | 11 | 11 |
| 0x1D05 | blood | 0 | 11 | 0 | 11 |
| 0x49E1 | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x49E2 | Pulley Crane NORTH | 1 | 11 | 0 | 11 |
| 0x3B98 |  | 0 | 0 | 11 | 11 |
| 0x0CC3 | muck | 0 | 11 | 0 | 11 |
| 0x116E | gravestone | 2 | 11 | 0 | 11 |
| 0x49E7 | Pulley Crane NORTH | 1 | 10 | 0 | 10 |
| 0x1D0C | blood | 0 | 10 | 0 | 10 |
| 0x1CF3 | blood | 0 | 10 | 0 | 10 |
| 0x07E8 | suspension bridge | 20 | 10 | 0 | 10 |
| 0x0F8E | Nox Crystal | 1 | 10 | 0 | 10 |
| 0x49E6 | Pulley Crane NORTH | 1 | 10 | 0 | 10 |
| 0x108A | ring | 1 | 10 | 0 | 10 |
| 0x0812 | suspension bridge | 21 | 10 | 0 | 10 |
| 0x1CF5 | blood | 0 | 10 | 0 | 10 |
| 0x1CEC | leg | 1 | 10 | 0 | 10 |
| 0x07B4 | wood | 5 | 0 | 10 | 10 |
| 0x1BAD | refuse | 0 | 10 | 0 | 10 |
| 0x07B5 | wood | 5 | 0 | 10 | 10 |
| 0x0428 | gruesome standard | 15 | 10 | 0 | 10 |
| 0x11F7 | fur%s% | 1 | 10 | 0 | 10 |
| 0x1D02 | blood | 0 | 10 | 0 | 10 |
| 0x1342 | lava | 0 | 10 | 0 | 10 |
| 0x3B88 |  | 0 | 0 | 10 | 10 |
| 0x0F41 | arrows | 1 | 10 | 0 | 10 |
| 0x1DAE | head | 1 | 10 | 0 | 10 |
| 0x0F7B | Blood Moss | 1 | 10 | 0 | 10 |
| 0x3260 | swamp | 0 | 10 | 0 | 10 |
| 0x327E | swamp | 0 | 10 | 0 | 10 |
| 0x07E9 | suspension bridge | 20 | 10 | 0 | 10 |
| 0x0F61 | longsword | 1 | 10 | 0 | 10 |
| 0x1873 | sheep carcass | 8 | 10 | 0 | 10 |
| 0x11C7 | flower pot | 3 | 10 | 0 | 10 |
| 0x0F5C | mace | 1 | 10 | 0 | 10 |
| 0x0A6C | folded blanket | 1 | 10 | 0 | 10 |
| 0x3B07 |  | 1 | 0 | 10 | 10 |
| 0x0D1D | grapevines | 11 | 10 | 0 | 10 |
| 0x1D91 | skeleton | 2 | 10 | 0 | 10 |
| 0x3259 | swamp | 0 | 10 | 0 | 10 |
| 0x0B47 | large vase | 7 | 10 | 0 | 10 |
| 0x0F8C | Sulfurous Ash | 1 | 10 | 0 | 10 |
| 0x1CE5 | arm | 1 | 10 | 0 | 10 |
| 0x0883 | wooden rail | 5 | 10 | 0 | 10 |
| 0x1CD4 | blood | 0 | 10 | 0 | 10 |
| 0x04E4 | bricks | 0 | 10 | 0 | 10 |
| 0x3B80 |  | 0 | 0 | 10 | 10 |
| 0x13B2 | bow | 1 | 10 | 0 | 10 |
| 0x22A4 |  | 0 | 0 | 10 | 10 |
| 0x07EC | suspension bridge | 20 | 10 | 0 | 10 |
| 0x0DE1 | kindling | 1 | 10 | 0 | 10 |
| 0x3B6D |  | 0 | 0 | 10 | 10 |
| 0x0ED5 | gravestone | 5 | 10 | 0 | 10 |
| 0x0D1B | grapevines | 0 | 10 | 0 | 10 |
| 0x3276 | swamp | 0 | 10 | 0 | 10 |
| 0x4C2E | Sea Sponge SOUTH | 1 | 10 | 0 | 10 |
| 0x1D01 | blood | 0 | 10 | 0 | 10 |
| 0x07EA | suspension bridge | 20 | 10 | 0 | 10 |
| 0x1E26 | oar | 0 | 10 | 0 | 10 |
| 0x246F | bamboo | 20 | 10 | 0 | 10 |
| 0x1088 | necklace | 1 | 9 | 0 | 9 |
| 0x117E | gravestone | 2 | 9 | 0 | 9 |
| 0x3E7C | ship | 3 | 9 | 0 | 9 |
| 0x1BB2 | refuse | 2 | 9 | 0 | 9 |
| 0x0F98 | bolt%s% of cloth | 1 | 9 | 0 | 9 |
| 0x1339 | lava | 0 | 9 | 0 | 9 |
| 0x117D | gravestone | 2 | 9 | 0 | 9 |
| 0x1A0A | skeleton | 0 | 9 | 0 | 9 |
| 0x12C9 | scaffold | 10 | 9 | 0 | 9 |
| 0x07EB | suspension bridge | 20 | 9 | 0 | 9 |
| 0x1174 | gravestone | 2 | 9 | 0 | 9 |
| 0x0F42 | arrow | 1 | 9 | 0 | 9 |
| 0x109A | carpet | 0 | 9 | 0 | 9 |
| 0x3447 | lava | 0 | 9 | 0 | 9 |
| 0x0DF2 |  wand | 1 | 9 | 0 | 9 |
| 0x2F4D |  | 0 | 0 | 9 | 9 |
| 0x0DD3 | fishing net | 0 | 9 | 0 | 9 |
| 0x1411 | platemail legs | 1 | 9 | 0 | 9 |
| 0x1ECB | pylon | 4 | 9 | 0 | 9 |
| 0x3E8C | ship | 3 | 9 | 0 | 9 |
| 0x1057 | sextant | 1 | 9 | 0 | 9 |
| 0x325E | swamp | 0 | 9 | 0 | 9 |
| 0x1E08 | stain | 0 | 9 | 0 | 9 |
| 0x1171 | gravestone | 2 | 9 | 0 | 9 |
| 0x1336 | lava | 0 | 9 | 0 | 9 |
| 0x3E95 | prow | 3 | 9 | 0 | 9 |
| 0x3E6A | sail | 3 | 9 | 0 | 9 |
| 0x2244 | broken crystals | 1 | 9 | 0 | 9 |
| 0x1CF9 | blood | 0 | 9 | 0 | 9 |
| 0x3E7E | ship | 3 | 9 | 0 | 9 |
| 0x0E21 | clean bandage%s% | 1 | 9 | 0 | 9 |
| 0x14F5 | spyglass | 1 | 9 | 0 | 9 |
| 0x105D | springs | 1 | 9 | 0 | 9 |
| 0x108B | beads | 1 | 9 | 0 | 9 |
| 0x0885 | wooden rail | 5 | 9 | 0 | 9 |
| 0x1BDC | boards | 3 | 9 | 0 | 9 |
| 0x1A99 | flax | 1 | 9 | 0 | 9 |
| 0x4C3B | Wall_Shelves_wood | 10 | 9 | 0 | 9 |
| 0x2F56 |  | 0 | 0 | 9 | 9 |
| 0x3E6B | sail | 3 | 9 | 0 | 9 |
| 0x12C8 | scaffold | 10 | 9 | 0 | 9 |
| 0x3BA9 |  | 0 | 0 | 9 | 9 |
| 0x0AF0 |  | 0 | 0 | 9 | 9 |
| 0x3E6C | mast | 3 | 9 | 0 | 9 |
| 0x3E91 | ship | 3 | 9 | 0 | 9 |
| 0x109C | carpet | 0 | 9 | 0 | 9 |
| 0x1B71 | hole | 0 | 9 | 0 | 9 |
| 0x0655 | stone roof | 0 | 9 | 0 | 9 |
| 0x3252 | swamp | 0 | 9 | 0 | 9 |
| 0x0538 | bricks | 0 | 9 | 0 | 9 |
| 0x0DD1 | fishing net | 0 | 9 | 0 | 9 |
| 0x11F6 | fur%s% | 1 | 9 | 0 | 9 |
| 0x3E6E | spar | 3 | 9 | 0 | 9 |
| 0x1008 | wash basin | 1 | 9 | 0 | 9 |
| 0x107B | stretched hide | 12 | 9 | 0 | 9 |
| 0x2979 | wooden floor | 0 | 9 | 0 | 9 |
| 0x1B7F | skeleton | 4 | 9 | 0 | 9 |
| 0x0AC4 | flagstones | 0 | 9 | 0 | 9 |
| 0x1053 | gears | 1 | 9 | 0 | 9 |
| 0x0F3F | arrow | 1 | 9 | 0 | 9 |
| 0x07E7 | suspension bridge | 20 | 9 | 0 | 9 |
| 0x0EDA | gravestone | 2 | 9 | 0 | 9 |
| 0x1D12 | blood | 0 | 9 | 0 | 9 |
| 0x133F | lava | 0 | 9 | 0 | 9 |
| 0x3253 | swamp | 0 | 9 | 0 | 9 |
| 0x3E8E | ship | 3 | 9 | 0 | 9 |
| 0x1172 | gravestone | 2 | 9 | 0 | 9 |
| 0x3557 | lava | 1 | 9 | 0 | 9 |
| 0x3E6D | sail | 3 | 9 | 0 | 9 |
| 0x0AEF |  | 0 | 0 | 9 | 9 |
| 0x354E | lava | 1 | 9 | 0 | 9 |
| 0x0AEE |  | 0 | 0 | 8 | 8 |
| 0x32F0 |  | 15 | 0 | 8 | 8 |
| 0x223D | broken crystals | 1 | 8 | 0 | 8 |
| 0x3523 | waterfall | 3 | 8 | 0 | 8 |
| 0x07ED | suspension bridge | 20 | 8 | 0 | 8 |
| 0x0DD5 | fishing net | 0 | 8 | 0 | 8 |
| 0x1170 | gravestone | 2 | 8 | 0 | 8 |
| 0x0B48 | vase | 4 | 8 | 0 | 8 |
| 0x0C69 | sprouts | 1 | 8 | 0 | 8 |
| 0x0F9B | bolt%s% of cloth | 1 | 8 | 0 | 8 |
| 0x3254 | swamp | 0 | 8 | 0 | 8 |
| 0x1087 | earrings | 1 | 8 | 0 | 8 |
| 0x0C1F | broken clock | 2 | 8 | 0 | 8 |
| 0x29CC | tatami mat | 0 | 8 | 0 | 8 |
| 0x0DCB | fishing net | 0 | 8 | 0 | 8 |
| 0x13A8 | pillow | 1 | 8 | 0 | 8 |
| 0x04F1 | blue tiles | 0 | 8 | 0 | 8 |
| 0x07EE | suspension bridge | 20 | 8 | 0 | 8 |
| 0x0F4B | double axe | 1 | 8 | 0 | 8 |
| 0x1175 | gravestone | 2 | 8 | 0 | 8 |
| 0x04C9 | wooden planks | 0 | 0 | 8 | 8 |
| 0x361D |  | 0 | 0 | 8 | 8 |
| 0x0D3C | fallen branch | 0 | 8 | 0 | 8 |
| 0x0DD4 | fishing net | 0 | 8 | 0 | 8 |
| 0x42FF | Large Stairs Carpet  | 5 | 8 | 0 | 8 |
| 0x1CD7 | blood | 0 | 8 | 0 | 8 |
| 0x13A6 | pillow | 1 | 8 | 0 | 8 |
| 0x1EBC | tinker's tools | 1 | 8 | 0 | 8 |
| 0x0A29 | candlabra | 20 | 8 | 0 | 8 |
| 0x19AC | fire | 0 | 8 | 0 | 8 |
| 0x07E6 | suspension bridge | 20 | 8 | 0 | 8 |
| 0x0F50 | crossbow | 1 | 8 | 0 | 8 |
| 0x3B8D |  | 0 | 0 | 8 | 8 |
| 0x42FE | Large Stairs Carpet  | 5 | 8 | 0 | 8 |
| 0x1047 | globe | 3 | 8 | 0 | 8 |
| 0x34FB | waterfall | 10 | 8 | 0 | 8 |
| 0x07E1 | suspension bridge | 21 | 8 | 0 | 8 |
| 0x0810 | suspension bridge | 21 | 8 | 0 | 8 |
| 0x42F5 | Large Stairs Carpet  | 5 | 8 | 0 | 8 |
| 0x42ED | Large Stairs Carpet  | 5 | 8 | 0 | 8 |
| 0x3AFB |  | 1 | 0 | 8 | 8 |
| 0x1A51 | lava | 10 | 8 | 0 | 8 |
| 0x9FB4 | Town_Cryer | 1 | 8 | 0 | 8 |
| 0x151A | decorative armor | 15 | 8 | 0 | 8 |
| 0x3B0B |  | 1 | 0 | 8 | 8 |
| 0x1A09 | skeleton | 0 | 8 | 0 | 8 |
| 0x2242 | broken crystals | 1 | 8 | 0 | 8 |
| 0x0DD2 | fishing net | 0 | 8 | 0 | 8 |
| 0x0ED9 | gravestone | 2 | 8 | 0 | 8 |
| 0x0F5E | broadsword | 1 | 8 | 0 | 8 |
| 0x0F8D | Spider's Silk | 1 | 8 | 0 | 8 |
| 0x77FB | BoardBatten Stairs S | 5 | 8 | 0 | 8 |
| 0x1E27 | oar | 0 | 8 | 0 | 8 |
| 0x1EAC | winch | 3 | 8 | 0 | 8 |
| 0x0913 | dirt patch | 0 | 8 | 0 | 8 |
| 0x0769 | sandstone | 5 | 8 | 0 | 8 |
| 0x0AF1 |  | 0 | 0 | 8 | 8 |
| 0x1A0C | skeleton | 0 | 8 | 0 | 8 |
| 0x3B76 |  | 0 | 0 | 8 | 8 |
| 0x24BF | lantern | 4 | 8 | 0 | 8 |
| 0x11F8 | fur%s% | 1 | 8 | 0 | 8 |
| 0x1166 | gravestone | 2 | 8 | 0 | 8 |
| 0x079D | cave steps | 16 | 8 | 0 | 8 |
| 0x34FC | waterfall | 10 | 8 | 0 | 8 |
| 0x1DA4 | right leg | 1 | 8 | 0 | 8 |
| 0x1874 | sheep carcass | 8 | 8 | 0 | 8 |
| 0x3B92 |  | 0 | 0 | 8 | 8 |
| 0x3296 | lava | 1 | 8 | 0 | 8 |
| 0x22A3 |  | 0 | 0 | 8 | 8 |
| 0x1321 | lava | 0 | 8 | 0 | 8 |
| 0x0AAF | Rug East | 0 | 8 | 0 | 8 |
| 0x1052 | axle%s% with gears | 1 | 8 | 0 | 8 |
| 0x133C | lava | 0 | 8 | 0 | 8 |
| 0x3B08 |  | 1 | 0 | 8 | 8 |
| 0x0814 | suspension bridge | 21 | 8 | 0 | 8 |
| 0x1BB8 | refuse | 2 | 8 | 0 | 8 |
| 0x0AB0 | Rug East | 0 | 8 | 0 | 8 |
| 0x224E | demon skull | 1 | 8 | 0 | 8 |
| 0x3E80 | ship | 3 | 8 | 0 | 8 |
| 0x363C |  | 20 | 0 | 8 | 8 |
| 0x126E | block | 2 | 8 | 0 | 8 |
| 0x07F4 | suspension bridge | 22 | 7 | 0 | 7 |
| 0x0805 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x3B96 |  | 0 | 0 | 7 | 7 |
| 0x0801 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x11F5 | fur%s% | 1 | 7 | 0 | 7 |
| 0x3267 | swamp | 0 | 7 | 0 | 7 |
| 0x3555 | lava | 1 | 7 | 0 | 7 |
| 0x1640 | shackles | 15 | 7 | 0 | 7 |
| 0x1D90 | skeleton | 2 | 7 | 0 | 7 |
| 0x0808 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x24BD | paper lantern | 2 | 7 | 0 | 7 |
| 0x0806 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x3E81 | ship | 3 | 7 | 0 | 7 |
| 0x1BFD | crossbow bolts | 1 | 7 | 0 | 7 |
| 0x1BA7 | refuse | 2 | 7 | 0 | 7 |
| 0x0F0B | Red Potion | 1 | 7 | 0 | 7 |
| 0x1AA1 | hops | 1 | 7 | 0 | 7 |
| 0x0F0C | Yellow Potion | 1 | 7 | 0 | 7 |
| 0x1CD8 | blood | 0 | 7 | 0 | 7 |
| 0x3B9F |  | 0 | 0 | 7 | 7 |
| 0x153A | long pants | 1 | 7 | 0 | 7 |
| 0x3BA3 |  | 0 | 0 | 7 | 7 |
| 0x07E2 | suspension bridge | 20 | 7 | 0 | 7 |
| 0x1CFB | blood | 0 | 7 | 0 | 7 |
| 0x3450 | lava | 0 | 7 | 0 | 7 |
| 0x0882 | wooden rail | 5 | 7 | 0 | 7 |
| 0x2D74 | elven wall map | 0 | 7 | 0 | 7 |
| 0x1029 | dovetail saw | 1 | 7 | 0 | 7 |
| 0x0F7D | Daemon Blood | 1 | 7 | 0 | 7 |
| 0x224F | demon skull | 1 | 7 | 0 | 7 |
| 0x0ABC | rug | 0 | 7 | 0 | 7 |
| 0x117B | gravestone | 2 | 7 | 0 | 7 |
| 0x0F9A | bolt%s% of cloth | 1 | 7 | 0 | 7 |
| 0x1DAF | right arm | 1 | 7 | 0 | 7 |
| 0x1CD5 | blood | 0 | 7 | 0 | 7 |
| 0x17C2 | snow | 0 | 7 | 0 | 7 |
| 0x1E68 | trophy | 0 | 7 | 0 | 7 |
| 0x1BD7 | board%s | 1 | 7 | 0 | 7 |
| 0x350B | waterfall | 5 | 7 | 0 | 7 |
| 0x0C53 | cotton | 1 | 7 | 0 | 7 |
| 0x0DF4 | wand | 1 | 7 | 0 | 7 |
| 0x1333 | lava | 0 | 7 | 0 | 7 |
| 0x0803 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x07EF | suspension bridge | 20 | 7 | 0 | 7 |
| 0x0811 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x1410 | platemail arms | 1 | 7 | 0 | 7 |
| 0x1762 | folded cloth | 1 | 7 | 0 | 7 |
| 0x13CC | leather tunic | 1 | 7 | 0 | 7 |
| 0x2203 | skull | 1 | 7 | 0 | 7 |
| 0x04B7 | wooden boards | 0 | 0 | 7 | 7 |
| 0x107A | stretched hide | 12 | 7 | 0 | 7 |
| 0x1273 | block | 2 | 7 | 0 | 7 |
| 0x0A00 | wall sconce | 0 | 7 | 0 | 7 |
| 0x1051 | axle%s% with gears | 1 | 7 | 0 | 7 |
| 0x080C | suspension bridge | 21 | 7 | 0 | 7 |
| 0x0807 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x2243 | broken crystals | 1 | 7 | 0 | 7 |
| 0x3B9B |  | 0 | 0 | 7 | 7 |
| 0x1A19 | lava | 10 | 7 | 0 | 7 |
| 0x2214 | crystal | 10 | 7 | 0 | 7 |
| 0x44CC | LobsterBuoyFrame02 S | 0 | 7 | 0 | 7 |
| 0x1BA3 | refuse | 2 | 7 | 0 | 7 |
| 0x0F82 | Dragon's Blood | 1 | 7 | 0 | 7 |
| 0x07E4 | suspension bridge | 20 | 7 | 0 | 7 |
| 0x0F86 | Mandrake Root%s% | 1 | 7 | 0 | 7 |
| 0x04EE | blue tiles | 0 | 7 | 0 | 7 |
| 0x0AE4 | carpet | 0 | 7 | 0 | 7 |
| 0x07F0 | suspension bridge | 20 | 7 | 0 | 7 |
| 0x1CCD | blood | 0 | 7 | 0 | 7 |
| 0x07B6 | wood | 5 | 0 | 7 | 7 |
| 0x56F3 | carpet | 1 | 7 | 0 | 7 |
| 0x3B9D |  | 0 | 0 | 7 | 7 |
| 0x1ECD | light | 0 | 7 | 0 | 7 |
| 0x1CE7 | legs | 1 | 7 | 0 | 7 |
| 0x0ADC |  | 0 | 0 | 7 | 7 |
| 0x09BB | roast pig%s% | 5 | 7 | 0 | 7 |
| 0x17EE | teleporter | 0 | 7 | 0 | 7 |
| 0x1CCF | blood | 0 | 7 | 0 | 7 |
| 0x0EDC | gravestone | 5 | 7 | 0 | 7 |
| 0x2E1F | acid | 0 | 7 | 0 | 7 |
| 0x103E | flour sifter | 1 | 7 | 0 | 7 |
| 0x0804 | suspension bridge | 21 | 7 | 0 | 7 |
| 0x104D | clock frame%s% | 1 | 7 | 0 | 7 |
| 0x1DAD | body | 1 | 7 | 0 | 7 |
| 0x56F1 | carpet | 1 | 7 | 0 | 7 |
| 0x07E3 | suspension bridge | 20 | 7 | 0 | 7 |
| 0x1BA4 | refuse | 2 | 7 | 0 | 7 |
| 0x0F85 | Ginseng | 1 | 7 | 0 | 7 |
| 0x04F0 | blue tiles | 0 | 7 | 0 | 7 |
| 0x3257 | swamp | 0 | 7 | 0 | 7 |
| 0x080B | suspension bridge | 21 | 7 | 0 | 7 |
| 0x14F6 | spyglass | 1 | 7 | 0 | 7 |
| 0x1CFD | blood | 0 | 7 | 0 | 7 |
| 0x0ADE |  | 0 | 0 | 7 | 7 |
| 0xAE43 | Tracks End East | 1 | 7 | 0 | 7 |
| 0x080A | suspension bridge | 21 | 7 | 0 | 7 |
| 0x04EF | blue tiles | 0 | 6 | 0 | 6 |
| 0x080D | suspension bridge | 21 | 6 | 0 | 6 |
| 0x0AE6 |  | 0 | 0 | 6 | 6 |
| 0x0DF0 | black staff | 1 | 6 | 0 | 6 |
| 0x1098 | carpet | 0 | 6 | 0 | 6 |
| 0x0DF3 |  wand | 1 | 6 | 0 | 6 |
| 0x0AE5 |  | 0 | 0 | 6 | 6 |
| 0x0EDE | gravestone | 5 | 6 | 0 | 6 |
| 0x4C3A | Wall_Shelves_wood | 10 | 6 | 0 | 6 |
| 0x3468 | lava | 0 | 6 | 0 | 6 |
| 0x1BA9 | refuse | 0 | 6 | 0 | 6 |
| 0x0AE3 |  | 0 | 0 | 6 | 6 |
| 0x04B6 | wooden boards | 0 | 0 | 6 | 6 |
| 0x1AD4 | nest with eggs | 1 | 6 | 0 | 6 |
| 0x166D | shackle | 0 | 6 | 0 | 6 |
| 0x3632 |  | 3 | 0 | 6 | 6 |
| 0x1763 | folded cloth | 1 | 6 | 0 | 6 |
| 0x07B2 | cave steps | 16 | 6 | 0 | 6 |
| 0x0654 | stone roof | 0 | 6 | 0 | 6 |
| 0x0DF5 | wand | 1 | 6 | 0 | 6 |
| 0x1068 | cut up leather | 1 | 6 | 0 | 6 |
| 0x09B2 | backpack | 1 | 6 | 0 | 6 |
| 0x3633 |  | 3 | 0 | 6 | 6 |
| 0x3EEC |  | 1 | 0 | 6 | 6 |
| 0x2250 | demon skull | 1 | 6 | 0 | 6 |
| 0x106D | stretched hide | 24 | 6 | 0 | 6 |
| 0x08CE | edging | 3 | 6 | 0 | 6 |
| 0x126C | block | 2 | 6 | 0 | 6 |
| 0x04AB | wooden boards | 0 | 0 | 6 | 6 |
| 0x1CCA | blood | 0 | 6 | 0 | 6 |
| 0x1BA5 | refuse | 2 | 6 | 0 | 6 |
| 0x1E76 | shelves | 6 | 6 | 0 | 6 |
| 0x116F | gravestone | 2 | 6 | 0 | 6 |
| 0x4C2A | Sea Coral SOUTH | 1 | 6 | 0 | 6 |
| 0x0813 | suspension bridge | 21 | 6 | 0 | 6 |
| 0x3B9C |  | 0 | 0 | 6 | 6 |
| 0x13EC | ringmail tunic | 1 | 6 | 0 | 6 |
| 0x363D |  | 20 | 0 | 6 | 6 |
| 0x09A8 | metal box | 3 | 6 | 0 | 6 |
| 0x3629 |  | 3 | 0 | 6 | 6 |
| 0x3B3C |  | 0 | 0 | 6 | 6 |
| 0x3628 |  | 3 | 0 | 6 | 6 |
| 0x1048 | globe | 3 | 6 | 0 | 6 |
| 0x116D | gravestone | 2 | 6 | 0 | 6 |
| 0x126B | block | 2 | 6 | 0 | 6 |
| 0x377A | sparkle | 0 | 6 | 0 | 6 |
| 0x0FD9 | tapestry | 0 | 6 | 0 | 6 |
| 0x0DD0 | fishing net | 0 | 6 | 0 | 6 |
| 0x1D06 | blood | 0 | 6 | 0 | 6 |
| 0x1CEB | legs | 1 | 6 | 0 | 6 |
| 0x13BB | chainmail  coif | 1 | 6 | 0 | 6 |
| 0x126F | block | 2 | 6 | 0 | 6 |
| 0x0DCC | fishing weight | 0 | 6 | 0 | 6 |
| 0x1E0C | stain | 0 | 6 | 0 | 6 |
| 0x1270 | block | 2 | 6 | 0 | 6 |
| 0x229E |  | 0 | 0 | 6 | 6 |
| 0x0FD7 | tapestry | 0 | 6 | 0 | 6 |
| 0x07E5 | suspension bridge | 20 | 6 | 0 | 6 |
| 0x1168 | gravestone | 2 | 6 | 0 | 6 |
| 0x18E9 | ginseng | 1 | 6 | 0 | 6 |
| 0x080E | suspension bridge | 21 | 6 | 0 | 6 |
| 0x1F2A | pedestal | 8 | 6 | 0 | 6 |
| 0x0F9C | bolt%s% of cloth | 1 | 6 | 0 | 6 |
| 0x1272 | block | 2 | 6 | 0 | 6 |
| 0x3B7F |  | 0 | 0 | 6 | 6 |
| 0x1DA3 | left leg | 1 | 6 | 0 | 6 |
| 0x361C |  | 0 | 0 | 6 | 6 |
| 0x04B0 | wooden boards | 0 | 0 | 6 | 6 |
| 0x110F | stone face | 0 | 6 | 0 | 6 |
| 0x0809 | suspension bridge | 21 | 6 | 0 | 6 |
| 0x0E20 | bloody bandage%s% | 1 | 6 | 0 | 6 |
| 0x31F5 | dirt | 0 | 6 | 0 | 6 |
| 0x0970 | stew | 1 | 6 | 0 | 6 |
| 0x3627 |  | 3 | 0 | 6 | 6 |
| 0x42B2 | large vase | 9 | 6 | 0 | 6 |
| 0x1A06 | skeleton | 0 | 6 | 0 | 6 |
| 0x09DF | dirty pot | 1 | 6 | 0 | 6 |
| 0x31F4 | dirt | 0 | 6 | 0 | 6 |
| 0x207F |  | 5 | 0 | 6 | 6 |
| 0x1082 | cut up leather | 1 | 6 | 0 | 6 |
| 0x1CD0 | blood | 0 | 6 | 0 | 6 |
| 0x3285 | swamp | 0 | 6 | 0 | 6 |
| 0x3B64 |  | 0 | 0 | 6 | 6 |
| 0x3621 |  | 0 | 0 | 6 | 6 |
| 0x0800 | suspension bridge | 21 | 6 | 0 | 6 |
| 0x1CDF | legs | 1 | 6 | 0 | 6 |
| 0x08CD | edging | 3 | 6 | 0 | 6 |
| 0x1515 | cloak | 1 | 6 | 0 | 6 |
| 0x31FB | dirt | 0 | 6 | 0 | 6 |
| 0x1E9C | pullies | 6 | 6 | 0 | 6 |
| 0x1CCE | blood | 0 | 6 | 0 | 6 |
| 0x1069 | stretched hide | 12 | 6 | 0 | 6 |
| 0x1516 | skirt | 1 | 6 | 0 | 6 |
| 0x0FB6 | horse shoes | 1 | 6 | 0 | 6 |
| 0x3B0E |  | 1 | 0 | 6 | 6 |
| 0x3266 | swamp | 0 | 6 | 0 | 6 |
| 0x1B6C | wall cracks | 0 | 6 | 0 | 6 |
| 0x22AA |  | 0 | 0 | 6 | 6 |
| 0x0E75 | backpack | 1 | 6 | 0 | 6 |
| 0x2E10 | acid | 0 | 6 | 0 | 6 |
| 0x1443 | two handed axe | 1 | 6 | 0 | 6 |
| 0x13E7 | ringmail armor | 1 | 6 | 0 | 6 |
| 0x325F | swamp | 0 | 6 | 0 | 6 |
| 0x07CD | wooden plank | 1 | 0 | 6 | 6 |
| 0x0F78 | Batwing%s% | 1 | 6 | 0 | 6 |
| 0x3634 |  | 3 | 0 | 5 | 5 |
| 0x1181 | gravestone | 2 | 5 | 0 | 5 |
| 0x1BA6 | refuse | 2 | 5 | 0 | 5 |
| 0x1056 | hinge%s% | 1 | 5 | 0 | 5 |
| 0x350C | waterfall | 5 | 5 | 0 | 5 |
| 0x362E |  | 3 | 0 | 5 | 5 |
| 0x241A | sculpture | 4 | 5 | 0 | 5 |
| 0x4C38 | Wall_Shelves_metal | 10 | 5 | 0 | 5 |
| 0x04A9 | wooden boards | 0 | 0 | 5 | 5 |
| 0x1F81 | glass | 1 | 5 | 0 | 5 |
| 0x0F7C | Bloodspawn | 1 | 5 | 0 | 5 |
| 0x13C7 | leather gorget | 1 | 5 | 0 | 5 |
| 0x175D | folded cloth | 1 | 5 | 0 | 5 |
| 0x1BD8 | boards | 2 | 5 | 0 | 5 |
| 0x0717 | marble stairs | 5 | 5 | 0 | 5 |
| 0x327F | swamp | 0 | 5 | 0 | 5 |
| 0x09AC | bushel | 3 | 5 | 0 | 5 |
| 0xAE42 | Tracks End North | 1 | 5 | 0 | 5 |
| 0x3622 |  | 0 | 0 | 5 | 5 |
| 0x0914 | dirt patch | 0 | 5 | 0 | 5 |
| 0x1809 | teleporter | 0 | 5 | 0 | 5 |
| 0x13E0 | studded tunic | 9 | 5 | 0 | 5 |
| 0x2218 | crystal | 10 | 5 | 0 | 5 |
| 0x2CFC | decorative vines | 0 | 5 | 0 | 5 |
| 0x408C | paver | 0 | 5 | 0 | 5 |
| 0x1182 | gravestone | 2 | 5 | 0 | 5 |
| 0x1413 | platemail gorget | 1 | 5 | 0 | 5 |
| 0x0EAF | tapestry | 1 | 5 | 0 | 5 |
| 0x1D8C | body | 2 | 5 | 0 | 5 |
| 0x1E07 | stain | 0 | 5 | 0 | 5 |
| 0x13AD | pillow | 1 | 5 | 0 | 5 |
| 0x1DFB | rug | 0 | 5 | 0 | 5 |
| 0x0784 | stone | 5 | 0 | 5 | 5 |
| 0x112B | fitting | 0 | 5 | 0 | 5 |
| 0x13A5 | pillow | 1 | 5 | 0 | 5 |
| 0x1C47 | coffin | 2 | 5 | 0 | 5 |
| 0x116B | gravestone | 2 | 5 | 0 | 5 |
| 0x0A58 | bedroll | 1 | 5 | 0 | 5 |
| 0x109D | carpet | 0 | 5 | 0 | 5 |
| 0x3455 | lava | 0 | 5 | 0 | 5 |
| 0x11C6 | flower pot | 1 | 5 | 0 | 5 |
| 0x1025 | arrow shafts | 1 | 5 | 0 | 5 |
| 0x3088 | elven loveseat | 1 | 5 | 0 | 5 |
| 0x4576 | Icicle Med East | 0 | 5 | 0 | 5 |
| 0x3258 | swamp | 0 | 5 | 0 | 5 |
| 0x0802 | suspension bridge | 21 | 5 | 0 | 5 |
| 0x14F4 | ship model | 1 | 5 | 0 | 5 |
| 0x07F3 | suspension bridge | 20 | 5 | 0 | 5 |
| 0x3268 | swamp | 0 | 5 | 0 | 5 |
| 0x1531 | skirt | 1 | 5 | 0 | 5 |
| 0x0711 | marble stairs | 5 | 5 | 0 | 5 |
| 0x0718 | marble stairs | 5 | 5 | 0 | 5 |
| 0x04C2 | wooden logs | 0 | 5 | 0 | 5 |
| 0x0495 | marble floor | 0 | 5 | 0 | 5 |
| 0x0888 | wooden rail | 5 | 5 | 0 | 5 |
| 0x13CA | leather tunic | 8 | 5 | 0 | 5 |
| 0x17FC | teleporter | 0 | 5 | 0 | 5 |
| 0x10FC | stone face | 0 | 5 | 0 | 5 |
| 0x3B09 |  | 1 | 0 | 5 | 5 |
| 0x375A | sparkle | 0 | 5 | 0 | 5 |
| 0x0F91 | Wyrm's Heart%s% | 1 | 5 | 0 | 5 |
| 0x0C51 | cotton | 1 | 5 | 0 | 5 |
| 0x0C66 | gourd%s% | 1 | 5 | 0 | 5 |
| 0x1803 | teleporter | 0 | 5 | 0 | 5 |
| 0x13B6 | scimitar | 1 | 5 | 0 | 5 |
| 0x1C9A | sarcophagus | 2 | 5 | 0 | 5 |
| 0x0004 | ankh | 12 | 5 | 0 | 5 |
| 0x49EC | Pulley Crane SOUTH | 1 | 5 | 0 | 5 |
| 0x1441 | cutlass | 1 | 5 | 0 | 5 |
| 0x17FD | teleporter | 0 | 5 | 0 | 5 |
| 0x1D8D | body | 2 | 5 | 0 | 5 |
| 0x1DB1 | right leg | 1 | 5 | 0 | 5 |
| 0x070E | marble stairs | 5 | 5 | 0 | 5 |
| 0x119A | floor spikes | 0 | 5 | 0 | 5 |
| 0x3262 | swamp | 0 | 5 | 0 | 5 |
| 0x13BD | chainmail tunic | 1 | 5 | 0 | 5 |
| 0x1E0E | stain | 0 | 5 | 0 | 5 |
| 0x1A69 | lava | 0 | 5 | 0 | 5 |
| 0x07FE | suspension bridge | 21 | 5 | 0 | 5 |
| 0x116A | gravestone | 2 | 5 | 0 | 5 |
| 0x0F83 | Executioner's Cap% | 1 | 5 | 0 | 5 |
| 0x1CD6 | blood | 0 | 5 | 0 | 5 |
| 0x0FC9 | shells | 1 | 5 | 0 | 5 |
| 0x13BC | chainmail leggings | 1 | 5 | 0 | 5 |
| 0x0D3D | fallen branch | 0 | 5 | 0 | 5 |
| 0x13D3 | leather tunic | 1 | 5 | 0 | 5 |
| 0x106C | stretched hide | 12 | 5 | 0 | 5 |
| 0x1067 | cut up leather | 1 | 5 | 0 | 5 |
| 0x07FF | suspension bridge | 21 | 5 | 0 | 5 |
| 0x1271 | block | 2 | 5 | 0 | 5 |
| 0x13C4 | chainmail tunic | 1 | 5 | 0 | 5 |
| 0x17FF | teleporter | 0 | 5 | 0 | 5 |
| 0x14E9 | moulding board | 1 | 5 | 0 | 5 |
| 0x1173 | gravestone | 2 | 5 | 0 | 5 |
| 0x1A56 | lava | 10 | 5 | 0 | 5 |
| 0x0FC6 | shells | 1 | 5 | 0 | 5 |
| 0x13D2 | leather leggings | 1 | 5 | 0 | 5 |
| 0x0ADD |  | 0 | 0 | 5 | 5 |
| 0x408B | paver | 0 | 5 | 0 | 5 |
| 0x185C | empty vials | 1 | 5 | 0 | 5 |
| 0x1179 | gravestone | 2 | 5 | 0 | 5 |
| 0x2CFA | decorative vines | 0 | 5 | 0 | 5 |
| 0x3B9A |  | 0 | 0 | 5 | 5 |
| 0x1AD3 | donuts | 1 | 5 | 0 | 5 |
| 0x1BD6 | shafts | 1 | 5 | 0 | 5 |
| 0x3556 | lava | 1 | 5 | 0 | 5 |
| 0x080F | suspension bridge | 21 | 5 | 0 | 5 |
| 0x0F49 | axe | 1 | 5 | 0 | 5 |
| 0x1EBA | tool kit | 1 | 5 | 0 | 5 |
| 0x13B1 | bow | 1 | 5 | 0 | 5 |
| 0x0713 | marble stairs | 5 | 5 | 0 | 5 |
| 0x0F43 | hatchet | 1 | 5 | 0 | 5 |
| 0x1801 | teleporter | 0 | 5 | 0 | 5 |
| 0x2470 | bamboo | 20 | 5 | 0 | 5 |
| 0x104F | clock parts | 1 | 5 | 0 | 5 |
| 0x1058 | sextant | 1 | 5 | 0 | 5 |
| 0x1A4C | lava | 10 | 5 | 0 | 5 |
| 0x0005 | ankh | 12 | 5 | 0 | 5 |
| 0x0F40 | arrows | 1 | 5 | 0 | 5 |
| 0x3282 | swamp | 0 | 5 | 0 | 5 |
| 0x0F37 | saddle | 3 | 5 | 0 | 5 |
| 0x1B64 | treasure | 0 | 5 | 0 | 5 |
| 0x105B | axle%s% | 1 | 5 | 0 | 5 |
| 0x3558 | lava | 1 | 5 | 0 | 5 |
| 0x1CD1 | blood | 0 | 5 | 0 | 5 |
| 0x1876 | iron wire | 1 | 5 | 0 | 5 |
| 0x1CDB | blood | 0 | 5 | 0 | 5 |
| 0x3B43 |  | 0 | 0 | 5 | 5 |
| 0x1804 | teleporter | 0 | 5 | 0 | 5 |
| 0x0719 | marble stairs | 5 | 5 | 0 | 5 |
| 0x2D72 | elven tapestry | 0 | 5 | 0 | 5 |
| 0x3B9E |  | 0 | 0 | 4 | 4 |
| 0x0F51 | dagger | 1 | 4 | 0 | 4 |
| 0x42FA | Large Stairs Carpet  | 5 | 4 | 0 | 4 |
| 0x1EFE | fancy shirt | 1 | 4 | 0 | 4 |
| 0x109E | carpet | 0 | 4 | 0 | 4 |
| 0x62DF | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x09FB | wall sconce | 0 | 4 | 0 | 4 |
| 0xAE5B | Tracks North Switch  | 1 | 4 | 0 | 4 |
| 0x143A | maul | 1 | 4 | 0 | 4 |
| 0x105C | axle%s% | 1 | 4 | 0 | 4 |
| 0x49F1 | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x0F87 | Eye%s% of Newt | 1 | 4 | 0 | 4 |
| 0x17E0 | teleporter | 0 | 4 | 0 | 4 |
| 0x49EA | Pulley Crane SOUTH | 10 | 4 | 0 | 4 |
| 0x1D8E | skeleton | 2 | 4 | 0 | 4 |
| 0x0A55 | bedroll | 1 | 4 | 0 | 4 |
| 0x17E4 | teleporter | 0 | 4 | 0 | 4 |
| 0x1E64 | trophy | 0 | 4 | 0 | 4 |
| 0x0DBF | fishing pole | 1 | 4 | 0 | 4 |
| 0x0FDA | tapestry | 0 | 4 | 0 | 4 |
| 0x17E7 | teleporter | 0 | 4 | 0 | 4 |
| 0x2240 | broken crystals | 1 | 4 | 0 | 4 |
| 0x1DB7 | barrle hoops | 1 | 4 | 0 | 4 |
| 0x175E | folded cloth | 1 | 4 | 0 | 4 |
| 0x1C24 | casket | 3 | 4 | 0 | 4 |
| 0x2245 | broken crystals | 1 | 4 | 0 | 4 |
| 0x1ECF | light | 0 | 4 | 0 | 4 |
| 0x62E2 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x62E6 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x62EF | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0EB7 | music stand | 5 | 4 | 0 | 4 |
| 0x0F52 | dagger | 1 | 4 | 0 | 4 |
| 0x62F8 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x62E5 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x17E9 | teleporter | 0 | 4 | 0 | 4 |
| 0x153E | full apron | 1 | 4 | 0 | 4 |
| 0x1CD2 | blood | 0 | 4 | 0 | 4 |
| 0x106A | stretched hide | 1 | 4 | 0 | 4 |
| 0x1E9D | pullies | 6 | 4 | 0 | 4 |
| 0x2D38 | wooden floor | 0 | 4 | 0 | 4 |
| 0x0EAE | tapestry | 1 | 4 | 0 | 4 |
| 0x13DF | studded leggings | 8 | 4 | 0 | 4 |
| 0x1C23 | casket | 3 | 4 | 0 | 4 |
| 0x49F0 | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x2FF2 | colorful tapestry | 0 | 4 | 0 | 4 |
| 0x13CD | leather sleeves | 1 | 4 | 0 | 4 |
| 0x0F4C | double axe | 1 | 4 | 0 | 4 |
| 0x62F7 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x13CB | leather leggings | 1 | 4 | 0 | 4 |
| 0x10E6 | inshave | 1 | 4 | 0 | 4 |
| 0x0F4F | crossbow | 1 | 4 | 0 | 4 |
| 0x17D9 | teleporter | 0 | 4 | 0 | 4 |
| 0x62E3 | BritanianShip Sails  | 21 | 4 | 0 | 4 |
| 0x42FD | Large Stairs Carpet  | 5 | 4 | 0 | 4 |
| 0x1BE1 | logs | 3 | 4 | 0 | 4 |
| 0x62DE | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x124E | rack | 3 | 4 | 0 | 4 |
| 0x1E70 | drawer | 1 | 4 | 0 | 4 |
| 0x1451 | bone helmet | 1 | 4 | 0 | 4 |
| 0x0E19 | cards | 1 | 4 | 0 | 4 |
| 0x13C3 | chainmail leggings | 1 | 4 | 0 | 4 |
| 0x1C48 | coffin | 2 | 4 | 0 | 4 |
| 0x10A0 | carpet | 0 | 4 | 0 | 4 |
| 0x126D | block | 2 | 4 | 0 | 4 |
| 0x49E8 | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x13B0 | war axe | 1 | 4 | 0 | 4 |
| 0x1914 | towel | 2 | 4 | 0 | 4 |
| 0x1BDB | boards | 2 | 4 | 0 | 4 |
| 0x1EA3 | net | 15 | 4 | 0 | 4 |
| 0x141A | platemail legs | 1 | 4 | 0 | 4 |
| 0x107F | stretched hide | 12 | 4 | 0 | 4 |
| 0x1C26 | casket | 3 | 4 | 0 | 4 |
| 0x3620 |  | 0 | 0 | 4 | 4 |
| 0x1B7B | metal shield | 1 | 4 | 0 | 4 |
| 0x13EF | ringmail sleeves | 1 | 4 | 0 | 4 |
| 0x2475 | cherry blossoms | 20 | 4 | 0 | 4 |
| 0xAE53 | Tracks Curve 03 West | 1 | 4 | 0 | 4 |
| 0x04AA | wooden boards | 0 | 0 | 4 | 4 |
| 0x160A | leg%s% of lamb | 1 | 4 | 0 | 4 |
| 0x1554 | vat | 8 | 4 | 0 | 4 |
| 0x04D6 | wooden planks | 0 | 0 | 4 | 4 |
| 0x0EED | gold coin | 0 | 4 | 0 | 4 |
| 0x0F9D | sewing kit | 1 | 4 | 0 | 4 |
| 0x13FB | large battle axe | 1 | 4 | 0 | 4 |
| 0x62E8 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x1D8B | body | 2 | 4 | 0 | 4 |
| 0x62ED | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0E18 | cards | 1 | 4 | 0 | 4 |
| 0x1080 | stretched hide | 12 | 4 | 0 | 4 |
| 0x03A0 | archway | 20 | 4 | 0 | 4 |
| 0x13C6 | Leather Gloves | 1 | 4 | 0 | 4 |
| 0x1C27 | casket | 3 | 4 | 0 | 4 |
| 0x1DA2 | right arm | 1 | 4 | 0 | 4 |
| 0x62EE | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0A01 | wall sconce | 0 | 4 | 0 | 4 |
| 0x0000 |  | 0 | 0 | 4 | 4 |
| 0x11A6 | gas trap | 0 | 4 | 0 | 4 |
| 0x62EB | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0C65 | gourd%s% | 1 | 4 | 0 | 4 |
| 0x1D8A | body | 2 | 4 | 0 | 4 |
| 0x0F5D | mace | 1 | 4 | 0 | 4 |
| 0x1555 | vat | 8 | 4 | 0 | 4 |
| 0x41EB | Wallset1 FloorB East | 0 | 4 | 0 | 4 |
| 0x112F | fitting | 0 | 4 | 0 | 4 |
| 0x307B | ArcaneCircle SOUTH | 1 | 4 | 0 | 4 |
| 0x0889 | wooden rail | 5 | 4 | 0 | 4 |
| 0x106B | stretched hide | 12 | 4 | 0 | 4 |
| 0x62E1 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0716 | marble stairs | 5 | 4 | 0 | 4 |
| 0x1C25 | casket | 3 | 4 | 0 | 4 |
| 0x1252 | rack | 3 | 4 | 0 | 4 |
| 0x62EA | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x1040 | pizza%s% | 1 | 4 | 0 | 4 |
| 0x42FC | Large Stairs Carpet  | 5 | 4 | 0 | 4 |
| 0x62E7 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0xAF19 | Hildebrandt bunting | 1 | 4 | 0 | 4 |
| 0x0FCC | shell | 1 | 4 | 0 | 4 |
| 0x4C2D | Sea Coral SOUTH | 1 | 4 | 0 | 4 |
| 0x04D4 | wooden planks | 0 | 0 | 4 | 4 |
| 0x1C49 | coffin | 2 | 4 | 0 | 4 |
| 0x0FBB | tongs | 1 | 4 | 0 | 4 |
| 0x1074 | training dummy | 18 | 4 | 0 | 4 |
| 0x17DF | teleporter | 0 | 4 | 0 | 4 |
| 0x1B28 | dirt | 0 | 4 | 0 | 4 |
| 0x41E8 | Wallset1 FloorB East | 0 | 4 | 0 | 4 |
| 0x0FA9 | dyes | 1 | 4 | 0 | 4 |
| 0x0F38 | saddle | 3 | 4 | 0 | 4 |
| 0x0F96 | bolt%s% of cloth | 1 | 4 | 0 | 4 |
| 0x1BA8 | refuse | 2 | 4 | 0 | 4 |
| 0x62F0 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x1550 | vat | 8 | 4 | 0 | 4 |
| 0x107D | stretched hide | 12 | 4 | 0 | 4 |
| 0x17DD | teleporter | 0 | 4 | 0 | 4 |
| 0x1558 | vat | 8 | 4 | 0 | 4 |
| 0x246C | bamboo | 20 | 4 | 0 | 4 |
| 0x071B | marble stairs | 5 | 4 | 0 | 4 |
| 0x0F80 | Daemon Bone%s% | 1 | 4 | 0 | 4 |
| 0x0C54 | cotton | 1 | 4 | 0 | 4 |
| 0x107E | stretched hide | 12 | 4 | 0 | 4 |
| 0x13F0 | ringmail leggings | 1 | 4 | 0 | 4 |
| 0x345F | lava | 0 | 4 | 0 | 4 |
| 0x1B42 | treasure | 0 | 4 | 0 | 4 |
| 0x41F3 | Wallset1 FloorB East | 0 | 4 | 0 | 4 |
| 0x1501 | plough | 1 | 4 | 0 | 4 |
| 0x62F5 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x107C | stretched hide | 12 | 4 | 0 | 4 |
| 0x062C | stone roof | 1 | 4 | 0 | 4 |
| 0x42FB | Large Stairs Carpet  | 5 | 4 | 0 | 4 |
| 0x62F6 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x1255 | rack | 3 | 4 | 0 | 4 |
| 0xAE52 | Tracks Curve 02 West | 1 | 4 | 0 | 4 |
| 0x0A57 | bedroll | 1 | 4 | 0 | 4 |
| 0x1254 | rack | 3 | 4 | 0 | 4 |
| 0x49EB | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x17DB | teleporter | 0 | 4 | 0 | 4 |
| 0x0FCA | shells | 1 | 4 | 0 | 4 |
| 0x1A0E | skeleton | 0 | 4 | 0 | 4 |
| 0x08F0 | stone rail | 5 | 4 | 0 | 4 |
| 0x0FC5 | shells | 1 | 4 | 0 | 4 |
| 0x13BF | chainmail tunic | 1 | 4 | 0 | 4 |
| 0x1253 | rack | 3 | 4 | 0 | 4 |
| 0x41F0 | Wallset1 FloorB East | 0 | 4 | 0 | 4 |
| 0x62E4 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x62F4 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x1DA1 | left arm | 1 | 4 | 0 | 4 |
| 0x0D23 | grapevines | 11 | 4 | 0 | 4 |
| 0x13AC | pillow | 1 | 4 | 0 | 4 |
| 0x18DD | mandrake root | 1 | 4 | 0 | 4 |
| 0x1B3C | dirt | 0 | 4 | 0 | 4 |
| 0x1E2B | oars | 8 | 4 | 0 | 4 |
| 0x1418 | platemail gloves | 1 | 4 | 0 | 4 |
| 0x17D8 | teleporter | 0 | 4 | 0 | 4 |
| 0x408D | paver | 0 | 4 | 0 | 4 |
| 0x49EE | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0xAE51 | Tracks Curve 01 West | 1 | 4 | 0 | 4 |
| 0x1180 | gravestone | 2 | 4 | 0 | 4 |
| 0x62F2 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x49ED | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x0FC7 | nautilus | 1 | 4 | 0 | 4 |
| 0x0F60 | longsword | 1 | 4 | 0 | 4 |
| 0x1768 | cut cloth | 1 | 4 | 0 | 4 |
| 0x117A | gravestone | 2 | 4 | 0 | 4 |
| 0x0E15 | cards | 1 | 4 | 0 | 4 |
| 0x2E2A | acid | 0 | 4 | 0 | 4 |
| 0x13BE | chainmail leggings | 1 | 4 | 0 | 4 |
| 0x49EF | Pulley Crane SOUTH | 1 | 4 | 0 | 4 |
| 0x1552 | vat | 8 | 4 | 0 | 4 |
| 0x62EC | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x0DCD | fishing weight | 0 | 4 | 0 | 4 |
| 0x1539 | long pants | 1 | 4 | 0 | 4 |
| 0x104E | clock frame%s% | 1 | 4 | 0 | 4 |
| 0x1CC9 | blood | 0 | 4 | 0 | 4 |
| 0x0E9E | tambourine | 5 | 4 | 0 | 4 |
| 0x0ADB |  | 0 | 0 | 4 | 4 |
| 0x1765 | cloth | 1 | 4 | 0 | 4 |
| 0x07F1 | suspension bridge | 20 | 4 | 0 | 4 |
| 0x62F1 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x42B3 | large vase | 7 | 4 | 0 | 4 |
| 0x2378 | decorative topiary | 5 | 4 | 0 | 4 |
| 0x0F84 | Garlic | 1 | 4 | 0 | 4 |
| 0x124F | rack | 3 | 4 | 0 | 4 |
| 0x163A | pilllow | 1 | 4 | 0 | 4 |
| 0x17DA | teleporter | 0 | 4 | 0 | 4 |
| 0x49E9 | Pulley Crane SOUTH | 10 | 4 | 0 | 4 |
| 0x17DE | teleporter | 0 | 4 | 0 | 4 |
| 0x0F45 | executioner's axe | 1 | 4 | 0 | 4 |
| 0x07F8 | suspension bridge | 20 | 4 | 0 | 4 |
| 0x0714 | marble stairs | 5 | 4 | 0 | 4 |
| 0x62E0 | BritanianShip Sails  | 0 | 4 | 0 | 4 |
| 0x362B |  | 3 | 0 | 4 | 4 |
| 0x13D9 | studded tunic | 9 | 4 | 0 | 4 |
| 0x1C22 | casket | 3 | 4 | 0 | 4 |
| 0x1E3B | bearskin rug | 0 | 3 | 0 | 3 |
| 0x0F8B | Pumice | 1 | 3 | 0 | 3 |
| 0x3080 | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x1F06 | bracelet | 1 | 3 | 0 | 3 |
| 0x0EC3 | cleaver | 1 | 3 | 0 | 3 |
| 0x0F7F | Brimstone | 1 | 3 | 0 | 3 |
| 0x2B6E | circlet | 1 | 3 | 0 | 3 |
| 0x0A94 | folded rich sheet  | 1 | 3 | 0 | 3 |
| 0x18E5 | nightshade | 1 | 3 | 0 | 3 |
| 0x56C8 | carpet | 1 | 3 | 0 | 3 |
| 0x46FE | ShipsWheel Mounted S | 1 | 3 | 0 | 3 |
| 0x13C2 | chainmail tunic | 1 | 3 | 0 | 3 |
| 0x1257 | rack | 3 | 3 | 0 | 3 |
| 0x3453 | lava | 0 | 3 | 0 | 3 |
| 0x185D | full vials | 1 | 3 | 0 | 3 |
| 0x1A60 | lava | 10 | 3 | 0 | 3 |
| 0x09F3 | pan | 1 | 3 | 0 | 3 |
| 0x31F6 | dirt | 0 | 3 | 0 | 3 |
| 0x070F | marble stairs | 5 | 3 | 0 | 3 |
| 0x3B3E |  | 0 | 0 | 3 | 3 |
| 0x0715 | marble stairs | 5 | 3 | 0 | 3 |
| 0x0003 | ankh | 12 | 3 | 0 | 3 |
| 0x09D5 | silverware | 1 | 3 | 0 | 3 |
| 0x185E | full vials | 1 | 3 | 0 | 3 |
| 0x2241 | broken crystals | 1 | 3 | 0 | 3 |
| 0x241B | sculpture | 4 | 3 | 0 | 3 |
| 0x1285 | pillory | 5 | 3 | 0 | 3 |
| 0x1177 | gravestone | 2 | 3 | 0 | 3 |
| 0x1B04 | floor cracks | 0 | 3 | 0 | 3 |
| 0xAE4E | Tracks Curve 01 Sout | 1 | 3 | 0 | 3 |
| 0xAE4A | Tracks Curve 03 Nort | 1 | 3 | 0 | 3 |
| 0x26CF | paladin sword | 1 | 3 | 0 | 3 |
| 0x0F95 | bolt%s% of cloth | 1 | 3 | 0 | 3 |
| 0x0C42 | dried herbs | 1 | 3 | 0 | 3 |
| 0x1BD5 | shafts | 1 | 3 | 0 | 3 |
| 0x3E7B | ship | 3 | 3 | 0 | 3 |
| 0x153D | full apron | 1 | 3 | 0 | 3 |
| 0x56BE | carpet | 1 | 3 | 0 | 3 |
| 0x1A9D | flax bundle%s% | 1 | 3 | 0 | 3 |
| 0x04BB | wooden boards | 0 | 0 | 3 | 3 |
| 0x14BF | Spirituality | 0 | 3 | 0 | 3 |
| 0x1C45 | coffin | 2 | 3 | 0 | 3 |
| 0x307D | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x14C1 | Spirituality | 0 | 3 | 0 | 3 |
| 0x0EEA | copper coin | 1 | 3 | 0 | 3 |
| 0x07FA | suspension bridge | 20 | 3 | 0 | 3 |
| 0x1406 | war mace | 1 | 3 | 0 | 3 |
| 0x0F97 | bolt%s% of cloth | 1 | 3 | 0 | 3 |
| 0x151C | decorative armor | 15 | 3 | 0 | 3 |
| 0x1B6E | wall cracks | 0 | 3 | 0 | 3 |
| 0xAE48 | Tracks Curve 01 Nort | 1 | 3 | 0 | 3 |
| 0x37B9 | glow | 0 | 3 | 0 | 3 |
| 0xAE5A | Tracks North Switch  | 1 | 3 | 0 | 3 |
| 0x56CB | carpet | 1 | 3 | 0 | 3 |
| 0x0C4F | cotton | 1 | 3 | 0 | 3 |
| 0x56DC | carpet | 1 | 3 | 0 | 3 |
| 0x14E6 | Chaos | 0 | 3 | 0 | 3 |
| 0x13CE | Leather Gloves | 1 | 3 | 0 | 3 |
| 0x3081 | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x1035 | saw | 1 | 3 | 0 | 3 |
| 0x345B | lava | 0 | 3 | 0 | 3 |
| 0x1C46 | coffin | 2 | 3 | 0 | 3 |
| 0x2249 | broken crystals | 1 | 3 | 0 | 3 |
| 0x3E8D | ship | 3 | 3 | 0 | 3 |
| 0x17E1 | teleporter | 0 | 3 | 0 | 3 |
| 0x56BF | carpet | 1 | 3 | 0 | 3 |
| 0x160B | pan of cookies | 1 | 3 | 0 | 3 |
| 0x1230 | Guillotine | 12 | 3 | 0 | 3 |
| 0x1E5F | bulletin board | 0 | 3 | 0 | 3 |
| 0x17E8 | teleporter | 0 | 3 | 0 | 3 |
| 0x1E39 | bearskin rug | 0 | 3 | 0 | 3 |
| 0x1110 | stone face | 0 | 3 | 0 | 3 |
| 0x04B5 | wooden boards | 0 | 0 | 3 | 3 |
| 0x1CCB | blood | 0 | 3 | 0 | 3 |
| 0x0710 | marble stairs | 5 | 3 | 0 | 3 |
| 0x3E8F | deck | 3 | 3 | 0 | 3 |
| 0x220A | crystal | 10 | 3 | 0 | 3 |
| 0x13EB | ringmail gloves | 1 | 3 | 0 | 3 |
| 0x1BDE | logs | 3 | 3 | 0 | 3 |
| 0x1C97 | sarcophagus lid | 2 | 3 | 0 | 3 |
| 0x106F | stretched hide | 12 | 3 | 0 | 3 |
| 0x0F0A | Green Potion | 1 | 3 | 0 | 3 |
| 0x3083 | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x125C | rack | 3 | 3 | 0 | 3 |
| 0x0E2F | crystal ball | 1 | 3 | 0 | 3 |
| 0x13C0 | chainmail coif | 1 | 3 | 0 | 3 |
| 0x3613 |  | 20 | 0 | 3 | 3 |
| 0x2246 | broken crystals | 1 | 3 | 0 | 3 |
| 0x04AD | wooden boards | 0 | 0 | 3 | 3 |
| 0x24C0 | lantern | 4 | 3 | 0 | 3 |
| 0x0F66 | easel with canvas | 12 | 3 | 0 | 3 |
| 0x3279 | swamp | 0 | 3 | 0 | 3 |
| 0x04C7 | wooden planks | 0 | 0 | 3 | 3 |
| 0x362A |  | 3 | 0 | 3 | 3 |
| 0x0146 | log post | 20 | 3 | 0 | 3 |
| 0x125A | rack | 3 | 3 | 0 | 3 |
| 0x0E78 | basin | 1 | 3 | 0 | 3 |
| 0xAE44 | Tracks End South | 1 | 3 | 0 | 3 |
| 0x0FCB | shell | 1 | 3 | 0 | 3 |
| 0x3255 | swamp | 0 | 3 | 0 | 3 |
| 0x1E9F | pulley | 6 | 3 | 0 | 3 |
| 0x56BD | carpet | 1 | 3 | 0 | 3 |
| 0x100A | archery butte | 10 | 3 | 0 | 3 |
| 0x062D | stone roof | 1 | 3 | 0 | 3 |
| 0x307C | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x1E3A | bearskin rug | 0 | 3 | 0 | 3 |
| 0x1DFA | rug | 0 | 3 | 0 | 3 |
| 0x362D |  | 3 | 0 | 3 | 3 |
| 0x56DA | carpet | 1 | 3 | 0 | 3 |
| 0x1370 | brush | 1 | 3 | 0 | 3 |
| 0x1530 | cloak | 1 | 3 | 0 | 3 |
| 0x56C0 | carpet | 1 | 3 | 0 | 3 |
| 0x0F4D | bardiche | 1 | 3 | 0 | 3 |
| 0x00C2 | wooden wall | 3 | 0 | 3 | 3 |
| 0x1BEB | gold ingots | 1 | 3 | 0 | 3 |
| 0x1256 | rack | 3 | 3 | 0 | 3 |
| 0x1E62 | trophy | 0 | 3 | 0 | 3 |
| 0x3E90 | ship | 3 | 3 | 0 | 3 |
| 0x13DE | studded sleeves | 7 | 3 | 0 | 3 |
| 0x117F | gravestone | 2 | 3 | 0 | 3 |
| 0x07B3 | wood | 5 | 0 | 3 | 3 |
| 0x1BAC | refuse | 2 | 3 | 0 | 3 |
| 0x0DF8 | pile%s% of wool | 1 | 3 | 0 | 3 |
| 0x02F0 | patch | 0 | 3 | 0 | 3 |
| 0x17E2 | teleporter | 0 | 3 | 0 | 3 |
| 0x56BB | carpet | 1 | 3 | 0 | 3 |
| 0x1E3E | bearskin rug | 0 | 3 | 0 | 3 |
| 0x1055 | hinge%s% | 1 | 3 | 0 | 3 |
| 0x17E3 | teleporter | 0 | 3 | 0 | 3 |
| 0x1609 | raw leg%s% of lamb | 1 | 3 | 0 | 3 |
| 0x3B10 |  | 1 | 0 | 3 | 3 |
| 0x31F7 | dirt | 0 | 3 | 0 | 3 |
| 0x1C96 | sarcophagus lid | 2 | 3 | 0 | 3 |
| 0x329F | lava | 0 | 3 | 0 | 3 |
| 0x125D | rack | 3 | 3 | 0 | 3 |
| 0x14C0 | Spirituality | 0 | 3 | 0 | 3 |
| 0x1022 | arrow fletching | 1 | 3 | 0 | 3 |
| 0x1517 | shirt | 1 | 3 | 0 | 3 |
| 0x3E7F | ship | 3 | 3 | 0 | 3 |
| 0x329E | lava | 0 | 3 | 0 | 3 |
| 0x1764 | folded cloth | 1 | 3 | 0 | 3 |
| 0x1B40 | treasure | 0 | 3 | 0 | 3 |
| 0x1099 | carpet | 0 | 3 | 0 | 3 |
| 0x14E3 | Chaos | 0 | 3 | 0 | 3 |
| 0x0057 | stone wall | 20 | 0 | 3 | 3 |
| 0x125B | rack | 3 | 3 | 0 | 3 |
| 0x1C99 | sarcophagus | 2 | 3 | 0 | 3 |
| 0x1537 | kilt | 1 | 3 | 0 | 3 |
| 0x1E37 | bearskin rug | 0 | 3 | 0 | 3 |
| 0x29FD | fire pit | 0 | 3 | 0 | 3 |
| 0x109F | carpet | 0 | 3 | 0 | 3 |
| 0x0F99 | bolt%s% of cloth | 1 | 3 | 0 | 3 |
| 0x307E | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x3E94 | ship | 3 | 3 | 0 | 3 |
| 0x1DB2 | left leg | 1 | 3 | 0 | 3 |
| 0x1043 | rolling pin | 1 | 3 | 0 | 3 |
| 0x1024 | arrow shafts | 1 | 3 | 0 | 3 |
| 0x1B9C | twigs | 1 | 3 | 0 | 3 |
| 0x109B | carpet | 0 | 3 | 0 | 3 |
| 0x1C44 | coffin | 2 | 3 | 0 | 3 |
| 0x2CFB | decorative vines | 0 | 3 | 0 | 3 |
| 0x1C9B | sarcophagus | 2 | 3 | 0 | 3 |
| 0x13FC | heavy crossbow | 1 | 3 | 0 | 3 |
| 0x2D3B | wooden floor | 0 | 3 | 0 | 3 |
| 0x09DC | dirty pot | 1 | 3 | 0 | 3 |
| 0x2987 | wooden floor | 0 | 3 | 0 | 3 |
| 0x3779 | sparkle | 0 | 3 | 0 | 3 |
| 0x0F8F | Grave Dust | 1 | 3 | 0 | 3 |
| 0x0F7E | Bone%s% | 1 | 3 | 0 | 3 |
| 0x3E7D | ship | 3 | 3 | 0 | 3 |
| 0x1BDD | log%s | 1 | 3 | 0 | 3 |
| 0x0F9E | scissors | 1 | 3 | 0 | 3 |
| 0x0C67 | gourd%s% | 1 | 3 | 0 | 3 |
| 0x21FC | skull spikes | 1 | 3 | 0 | 3 |
| 0x14C2 | Spirituality | 0 | 3 | 0 | 3 |
| 0x17FE | teleporter | 0 | 3 | 0 | 3 |
| 0x04D5 | wooden planks | 0 | 0 | 3 | 3 |
| 0x1802 | teleporter | 0 | 3 | 0 | 3 |
| 0x143B | maul | 1 | 3 | 0 | 3 |
| 0x2D6F | small elven tapestr | 0 | 3 | 0 | 3 |
| 0x4CDD | Resin | 1 | 3 | 0 | 3 |
| 0x07BC | carpeted stair | 5 | 3 | 0 | 3 |
| 0x1003 | spittoon | 1 | 3 | 0 | 3 |
| 0x56DD | carpet | 1 | 3 | 0 | 3 |
| 0x1DF7 | rug | 0 | 3 | 0 | 3 |
| 0x1E66 | trophy | 0 | 3 | 0 | 3 |
| 0x0D54 | vines | 0 | 3 | 0 | 3 |
| 0x1416 | platemail | 1 | 3 | 0 | 3 |
| 0x32C9 | dirt | 0 | 3 | 0 | 3 |
| 0xAE59 | Tracks North Switch  | 1 | 3 | 0 | 3 |
| 0x0E9C | drum | 5 | 3 | 0 | 3 |
| 0x0E80 | strong box | 3 | 3 | 0 | 3 |
| 0x3256 | swamp | 0 | 3 | 0 | 3 |
| 0x1E09 | stain | 0 | 3 | 0 | 3 |
| 0x1E6A | trophy | 0 | 3 | 0 | 3 |
| 0x1050 | clock parts | 1 | 3 | 0 | 3 |
| 0x1E3C | bearskin rug | 0 | 3 | 0 | 3 |
| 0x14E5 | Chaos | 0 | 3 | 0 | 3 |
| 0x103F | cookie mix | 1 | 3 | 0 | 3 |
| 0xAE50 | Tracks Curve 03 Sout | 1 | 3 | 0 | 3 |
| 0x14E4 | Chaos | 0 | 3 | 0 | 3 |
| 0x1C9E | sarcophagus | 2 | 3 | 0 | 3 |
| 0x1E38 | bearskin rug | 0 | 3 | 0 | 3 |
| 0x3B40 |  | 0 | 0 | 3 | 3 |
| 0x1407 | war mace | 1 | 3 | 0 | 3 |
| 0x1054 | gears | 1 | 3 | 0 | 3 |
| 0x1E91 | skinned deer | 3 | 3 | 0 | 3 |
| 0x2DF6 | elven reading desk | 5 | 3 | 0 | 3 |
| 0xAE58 | Tracks North Switch  | 1 | 3 | 0 | 3 |
| 0x0E88 | pitchfork | 1 | 3 | 0 | 3 |
| 0x0F8A | Pig Iron | 1 | 3 | 0 | 3 |
| 0x362C |  | 3 | 0 | 3 | 3 |
| 0x56DB | carpet | 1 | 3 | 0 | 3 |
| 0x1DA0 | head | 1 | 3 | 0 | 3 |
| 0x3082 | ArcaneCircle SOUTH | 1 | 3 | 0 | 3 |
| 0x1E36 | bearskin rug | 0 | 3 | 0 | 3 |
| 0x56C9 | carpet | 1 | 3 | 0 | 3 |
| 0x1C98 | sarcophagus lid | 2 | 3 | 0 | 3 |
| 0x102E | nails | 1 | 3 | 0 | 3 |
| 0x1E3D | bearskin rug | 0 | 3 | 0 | 3 |
| 0x3B3F |  | 0 | 0 | 3 | 3 |
| 0x09BD | silverware | 1 | 3 | 0 | 3 |
| 0x2832 | windchimes | 2 | 3 | 0 | 3 |
| 0x1A5B | lava | 10 | 3 | 0 | 3 |
| 0x0E9B | mortar and pestle | 1 | 3 | 0 | 3 |
| 0x1EA4 | net | 15 | 3 | 0 | 3 |
| 0x345D | lava | 0 | 3 | 0 | 3 |
| 0x10E5 | froe | 1 | 3 | 0 | 3 |
| 0xAE4F | Tracks Curve 02 Sout | 1 | 3 | 0 | 3 |
| 0x17E6 | teleporter | 0 | 3 | 0 | 3 |
| 0x1D8F | skeleton | 2 | 3 | 0 | 3 |
| 0x1397 | pillow | 1 | 3 | 0 | 3 |
| 0x1247 | guillotine | 12 | 3 | 0 | 3 |
| 0x2CF9 | decorative vines | 0 | 3 | 0 | 3 |
| 0x3E7A | deck | 3 | 3 | 0 | 3 |
| 0x56CA | carpet | 1 | 3 | 0 | 3 |
| 0x0C50 | cotton | 1 | 3 | 0 | 3 |
| 0x1CA0 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x0F81 | Fertile Dirt | 1 | 2 | 0 | 2 |
| 0x14A2 | Honesty | 0 | 2 | 0 | 2 |
| 0x11C0 | crumbling floor | 0 | 2 | 0 | 2 |
| 0x13D8 | studded leggings | 8 | 2 | 0 | 2 |
| 0x0A59 | bedroll | 1 | 2 | 0 | 2 |
| 0x0FA5 | checkers | 1 | 2 | 0 | 2 |
| 0x14D0 | Humility | 0 | 2 | 0 | 2 |
| 0x100B | archery butte | 10 | 2 | 0 | 2 |
| 0x4BFC | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x13AF | war axe | 1 | 2 | 0 | 2 |
| 0x13B5 | scimitar | 1 | 2 | 0 | 2 |
| 0x1CA4 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x4084 | decorative paver | 0 | 2 | 0 | 2 |
| 0x4086 | decorative paver | 0 | 2 | 0 | 2 |
| 0xAF1B | Hildebrandt shield | 1 | 2 | 0 | 2 |
| 0x345A | lava | 0 | 2 | 0 | 2 |
| 0x14A1 | Honesty | 0 | 2 | 0 | 2 |
| 0x1F84 | glass | 1 | 2 | 0 | 2 |
| 0x1015 | spinning wheel | 4 | 2 | 0 | 2 |
| 0x13E5 | ringmail leggings | 1 | 2 | 0 | 2 |
| 0x14BA | Valor | 0 | 2 | 0 | 2 |
| 0x10E7 | scorp | 1 | 2 | 0 | 2 |
| 0x0F90 | Dead Wood  | 1 | 2 | 0 | 2 |
| 0x1C6D | sarcophagus | 2 | 2 | 0 | 2 |
| 0x350E | waterfall | 5 | 2 | 0 | 2 |
| 0x1C63 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x22A0 |  | 0 | 0 | 2 | 2 |
| 0x1EEC | magical door | 0 | 2 | 0 | 2 |
| 0x1376 | horse barding | 0 | 2 | 0 | 2 |
| 0x3649 |  | 20 | 0 | 2 | 2 |
| 0x0E9D | tambourine | 5 | 2 | 0 | 2 |
| 0x1F79 | fur | 1 | 2 | 0 | 2 |
| 0x1C68 | sarcophagus lid | 2 | 2 | 0 | 2 |
| 0x14B9 | Valor | 0 | 2 | 0 | 2 |
| 0x982D | TrainingDummyAdv Fra | 1 | 2 | 0 | 2 |
| 0x1B7D | skeleton | 4 | 2 | 0 | 2 |
| 0x1C60 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1C6A | sarcophagus | 2 | 2 | 0 | 2 |
| 0x18EC | ginseng root | 1 | 2 | 0 | 2 |
| 0x1C2C | casket | 3 | 2 | 0 | 2 |
| 0x1CA5 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1C2A | casket | 3 | 2 | 0 | 2 |
| 0x0DEF | raw cotton | 1 | 2 | 0 | 2 |
| 0x09E7 | dirty pot | 1 | 2 | 0 | 2 |
| 0x2D4A | decorative acorn | 5 | 2 | 0 | 2 |
| 0x105E | springs | 1 | 2 | 0 | 2 |
| 0x0F6C | blue moongate | 0 | 2 | 0 | 2 |
| 0x1081 | cut up leather | 1 | 2 | 0 | 2 |
| 0x0D24 | grapevines | 11 | 2 | 0 | 2 |
| 0x3063 | MultiTile Assets | 5 | 2 | 0 | 2 |
| 0x41F1 | Wallset1 FloorB East | 0 | 2 | 0 | 2 |
| 0x13FD | heavy crossbow | 1 | 2 | 0 | 2 |
| 0x1C9D | sarcophagus | 2 | 2 | 0 | 2 |
| 0x4C31 | Sea Sponge SOUTH | 1 | 2 | 0 | 2 |
| 0x0FA2 | playing cards | 1 | 2 | 0 | 2 |
| 0x0DCF | fishing weight | 0 | 2 | 0 | 2 |
| 0x09BC | roast pig%s% | 5 | 2 | 0 | 2 |
| 0x1E6D | trophy | 0 | 2 | 0 | 2 |
| 0x0632 | stone roof | 1 | 2 | 0 | 2 |
| 0x1C3C | coffin | 2 | 2 | 0 | 2 |
| 0x9831 | TrainingDummyAdv Fra | 1 | 2 | 0 | 2 |
| 0x1C6B | sarcophagus | 2 | 2 | 0 | 2 |
| 0x18E4 | garlic bulb | 1 | 2 | 0 | 2 |
| 0x164E | catapult | 3 | 2 | 0 | 2 |
| 0x1DFC | rug | 0 | 2 | 0 | 2 |
| 0x1B29 | dirt | 0 | 2 | 0 | 2 |
| 0x1E9B | hook | 6 | 2 | 0 | 2 |
| 0x1F7F | glass of cider | 1 | 2 | 0 | 2 |
| 0x170C | boots | 1 | 2 | 0 | 2 |
| 0x374A | sparkle | 0 | 2 | 0 | 2 |
| 0x02DB |  | 1 | 0 | 2 | 2 |
| 0x22AB |  | 0 | 0 | 2 | 2 |
| 0x0FD6 | tapestry | 0 | 2 | 0 | 2 |
| 0x1284 | pillory | 3 | 2 | 0 | 2 |
| 0x9832 | TrainingDummyAdv Fra | 15 | 2 | 0 | 2 |
| 0x13B8 | long sword | 1 | 2 | 0 | 2 |
| 0x0A23 | lantern | 1 | 2 | 0 | 2 |
| 0x1023 | arrow fletching | 1 | 2 | 0 | 2 |
| 0x0A18 | lantern | 1 | 2 | 0 | 2 |
| 0x1C62 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1377 | horse barding | 0 | 2 | 0 | 2 |
| 0x3997 | Field of fire | 12 | 2 | 0 | 2 |
| 0x199E | bellows | 3 | 2 | 0 | 2 |
| 0x1DF5 | rug | 0 | 2 | 0 | 2 |
| 0x19B3 | fire | 0 | 2 | 0 | 2 |
| 0x3B38 |  | 0 | 0 | 2 | 2 |
| 0x373A | sparkle | 0 | 2 | 0 | 2 |
| 0x2DD7 | elven bow stringer | 1 | 2 | 0 | 2 |
| 0x1C66 | sarcophagus lid | 2 | 2 | 0 | 2 |
| 0x0FDF | tapestry | 0 | 2 | 0 | 2 |
| 0x18EA | ginseng | 1 | 2 | 0 | 2 |
| 0x0DFE | cut hair | 1 | 2 | 0 | 2 |
| 0x1372 | brush | 1 | 2 | 0 | 2 |
| 0x1518 | shirt | 1 | 2 | 0 | 2 |
| 0x1F02 | plain dress | 1 | 2 | 0 | 2 |
| 0x0F79 | Blackmoor | 1 | 2 | 0 | 2 |
| 0x0148 | log post | 20 | 2 | 0 | 2 |
| 0x0DFC | barber scissors | 1 | 2 | 0 | 2 |
| 0x1176 | gravestone | 2 | 2 | 0 | 2 |
| 0x149F | Honesty | 0 | 2 | 0 | 2 |
| 0x0FC4 | conch shell | 1 | 2 | 0 | 2 |
| 0x1CA3 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1760 | folded cloth | 1 | 2 | 0 | 2 |
| 0x0FE0 | tapestry | 0 | 2 | 0 | 2 |
| 0x1A65 | lava | 0 | 2 | 0 | 2 |
| 0x1B4E | treasure | 0 | 2 | 0 | 2 |
| 0x3454 | lava | 0 | 2 | 0 | 2 |
| 0x2A04 | fire pit | 0 | 2 | 0 | 2 |
| 0x1C9F | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1C87 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x0E1B | checkers | 1 | 2 | 0 | 2 |
| 0x408E | paver | 0 | 2 | 0 | 2 |
| 0x221E | crystal | 10 | 2 | 0 | 2 |
| 0x3EA3 | ship | 3 | 2 | 0 | 2 |
| 0x32A0 | lava | 0 | 2 | 0 | 2 |
| 0x1822 | alchemical symbol | 0 | 2 | 0 | 2 |
| 0x071C | marble stairs | 5 | 2 | 0 | 2 |
| 0x41EA | Wallset1 FloorB East | 0 | 2 | 0 | 2 |
| 0x1D47 | body | 4 | 2 | 0 | 2 |
| 0x0E24 | empty vial%s% | 1 | 2 | 0 | 2 |
| 0x1C8B | sarcophagus | 2 | 2 | 0 | 2 |
| 0x4BFF | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x1DB0 | left arm | 1 | 2 | 0 | 2 |
| 0x44D1 | Crab SOUTH | 1 | 2 | 0 | 2 |
| 0x32A1 | lava | 0 | 2 | 0 | 2 |
| 0x1283 | pillory | 3 | 2 | 0 | 2 |
| 0x1288 | pillory | 5 | 2 | 0 | 2 |
| 0x1F00 | fancy dress | 1 | 2 | 0 | 2 |
| 0x1287 | pillory | 5 | 2 | 0 | 2 |
| 0x9830 | TrainingDummyAdv Fra | 15 | 2 | 0 | 2 |
| 0x163C | pillow | 1 | 2 | 0 | 2 |
| 0x407F | decorative paver | 0 | 2 | 0 | 2 |
| 0x982E | TrainingDummyAdv Fra | 15 | 2 | 0 | 2 |
| 0x1E2A | oars | 8 | 2 | 0 | 2 |
| 0x14C9 | Honor | 0 | 2 | 0 | 2 |
| 0x1D75 | skeleton | 4 | 2 | 0 | 2 |
| 0xAE61 | Tracks East Switch S | 1 | 2 | 0 | 2 |
| 0x1C9C | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1851 | scales | 1 | 2 | 0 | 2 |
| 0x1507 | harrow | 0 | 2 | 0 | 2 |
| 0x2849 | sculpture | 4 | 2 | 0 | 2 |
| 0x1C65 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1810 | hourglass | 1 | 2 | 0 | 2 |
| 0x1286 | pillory | 15 | 2 | 0 | 2 |
| 0x1800 | teleporter | 0 | 2 | 0 | 2 |
| 0x1F0D | morning glory | 0 | 2 | 0 | 2 |
| 0x1EB8 | tool kit | 1 | 2 | 0 | 2 |
| 0x13C5 | leather sleeves | 1 | 2 | 0 | 2 |
| 0x1E6E | board | 3 | 2 | 0 | 2 |
| 0x0659 | slate roof | 0 | 2 | 0 | 2 |
| 0x1D45 | body | 4 | 2 | 0 | 2 |
| 0x1649 | catapult | 3 | 2 | 0 | 2 |
| 0x1D44 | body | 4 | 2 | 0 | 2 |
| 0x1503 | plough | 1 | 2 | 0 | 2 |
| 0x223E | broken crystals | 1 | 2 | 0 | 2 |
| 0x1B48 | treasure | 0 | 2 | 0 | 2 |
| 0x1DF3 | rug | 0 | 2 | 0 | 2 |
| 0x0EAD | tapestry | 1 | 2 | 0 | 2 |
| 0x1D74 | skeleton | 4 | 2 | 0 | 2 |
| 0x1E61 | trophy | 0 | 2 | 0 | 2 |
| 0x1059 | sextant parts | 1 | 2 | 0 | 2 |
| 0x106E | stretched hide | 12 | 2 | 0 | 2 |
| 0x0A1B | hanging lantern | 0 | 2 | 0 | 2 |
| 0x1BAB | refuse | 2 | 2 | 0 | 2 |
| 0x1E9A | hook | 6 | 2 | 0 | 2 |
| 0x1C64 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x13A9 | pillow | 1 | 2 | 0 | 2 |
| 0x1B5B | treasure | 0 | 2 | 0 | 2 |
| 0x4046 | gargoyle floormirror | 20 | 2 | 0 | 2 |
| 0x09FC | wall sconce | 0 | 2 | 0 | 2 |
| 0x1B73 | buckler  | 1 | 2 | 0 | 2 |
| 0x08CB | edging | 3 | 2 | 0 | 2 |
| 0x11E4 | grass mat | 1 | 2 | 0 | 2 |
| 0x184A | heating stand | 1 | 2 | 0 | 2 |
| 0x1C8A | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1E63 | trophy | 0 | 2 | 0 | 2 |
| 0x13FE | katana | 1 | 2 | 0 | 2 |
| 0x1169 | gravestone | 2 | 2 | 0 | 2 |
| 0x1C11 |  | 1 | 0 | 2 | 2 |
| 0x1C8D | sarcophagus | 2 | 2 | 0 | 2 |
| 0xAE60 | Tracks East Switch S | 1 | 2 | 0 | 2 |
| 0x1D48 | body | 4 | 2 | 0 | 2 |
| 0x9995 | Roasting Pig Grill F | 1 | 2 | 0 | 2 |
| 0x13F4 | crook | 1 | 2 | 0 | 2 |
| 0x1F94 | glass of water | 1 | 2 | 0 | 2 |
| 0x14FD | lockpicks | 1 | 2 | 0 | 2 |
| 0x127E | pillory | 3 | 2 | 0 | 2 |
| 0x01C9 | hide wall | 3 | 0 | 2 | 2 |
| 0x2FDC |  | 10 | 0 | 2 | 2 |
| 0x097F | frypan | 1 | 2 | 0 | 2 |
| 0x0F08 | Blue Potion | 1 | 2 | 0 | 2 |
| 0x1761 | folded cloth | 1 | 2 | 0 | 2 |
| 0x1C88 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1ECA | pylon | 4 | 2 | 0 | 2 |
| 0x14FE | lockpicks | 1 | 2 | 0 | 2 |
| 0x13E8 | ringmail armor | 1 | 2 | 0 | 2 |
| 0x1C6E | sarcophagus | 2 | 2 | 0 | 2 |
| 0x0712 | marble stairs | 5 | 2 | 0 | 2 |
| 0x14B7 | Valor | 0 | 2 | 0 | 2 |
| 0x14C7 | Honor | 0 | 2 | 0 | 2 |
| 0x1E60 | trophy | 0 | 2 | 0 | 2 |
| 0x4BFD | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x1CDC | blood | 0 | 2 | 0 | 2 |
| 0x1B56 | treasure | 0 | 2 | 0 | 2 |
| 0x14D2 | Humility | 0 | 2 | 0 | 2 |
| 0x3AFA |  | 1 | 0 | 2 | 2 |
| 0x1C28 | casket | 3 | 2 | 0 | 2 |
| 0x34EE | waterfall | 5 | 2 | 0 | 2 |
| 0x1289 | pillory | 3 | 2 | 0 | 2 |
| 0x1E67 | trophy | 0 | 2 | 0 | 2 |
| 0x41EC | Wallset1 FloorB East | 0 | 2 | 0 | 2 |
| 0x4080 | decorative paver | 0 | 2 | 0 | 2 |
| 0xAE49 | Tracks Curve 02 Nort | 1 | 2 | 0 | 2 |
| 0x1D46 | body | 4 | 2 | 0 | 2 |
| 0x0EAC | tapestry | 1 | 2 | 0 | 2 |
| 0x34FE | waterfall | 10 | 2 | 0 | 2 |
| 0x34B5 | whirlpool | 0 | 2 | 0 | 2 |
| 0x14D1 | Humility | 0 | 2 | 0 | 2 |
| 0x2409 | fan | 2 | 2 | 0 | 2 |
| 0x17DC | teleporter | 0 | 2 | 0 | 2 |
| 0x1C67 | sarcophagus lid | 2 | 2 | 0 | 2 |
| 0x1C69 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x053A | bricks | 0 | 2 | 0 | 2 |
| 0x13DB | studded tunic | 1 | 2 | 0 | 2 |
| 0x32CA | dirt | 0 | 2 | 0 | 2 |
| 0x3EA4 | ship | 3 | 2 | 0 | 2 |
| 0x1500 | plough | 1 | 2 | 0 | 2 |
| 0x164F | catapult | 3 | 2 | 0 | 2 |
| 0x24C3 | paper lantern | 10 | 2 | 0 | 2 |
| 0x13D1 | leather tunic | 8 | 2 | 0 | 2 |
| 0x1D76 | skeleton | 4 | 2 | 0 | 2 |
| 0x0F46 | executioner's axe | 1 | 2 | 0 | 2 |
| 0x407E | decorative paver | 0 | 2 | 0 | 2 |
| 0x103D | dough | 1 | 2 | 0 | 2 |
| 0x0372 | patch | 0 | 2 | 0 | 2 |
| 0x1B05 | floor cracks | 0 | 2 | 0 | 2 |
| 0x0D22 | grapevines | 11 | 2 | 0 | 2 |
| 0x09E2 | frypan | 1 | 2 | 0 | 2 |
| 0x1B46 | treasure | 0 | 2 | 0 | 2 |
| 0x09E3 | pot | 1 | 2 | 0 | 2 |
| 0x13C9 | leather leggings | 7 | 2 | 0 | 2 |
| 0x44CD | LobsterBuoyFrame03 S | 0 | 2 | 0 | 2 |
| 0x13BA | viking sword | 1 | 2 | 0 | 2 |
| 0x1281 | pillory | 5 | 2 | 0 | 2 |
| 0x1371 | brush | 1 | 2 | 0 | 2 |
| 0x9833 | TrainingDummyAdv Fra | 1 | 2 | 0 | 2 |
| 0x04BC | wooden boards | 0 | 0 | 2 | 2 |
| 0x185B | empty vials | 1 | 2 | 0 | 2 |
| 0x0C64 | gourd%s% | 1 | 2 | 0 | 2 |
| 0x0EDB | gravestone | 5 | 2 | 0 | 2 |
| 0x14CF | Humility | 0 | 2 | 0 | 2 |
| 0x071D | marble stairs | 5 | 2 | 0 | 2 |
| 0x3B3A |  | 0 | 0 | 2 | 2 |
| 0x1C89 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x14C8 | Honor | 0 | 2 | 0 | 2 |
| 0x2856 | sword display | 4 | 2 | 0 | 2 |
| 0x1C51 | coffin | 2 | 2 | 0 | 2 |
| 0x1C29 | casket | 3 | 2 | 0 | 2 |
| 0x4C00 | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x14A0 | Honesty | 0 | 2 | 0 | 2 |
| 0x1BD4 | shaft%s | 1 | 2 | 0 | 2 |
| 0x0EB1 | standing harp | 10 | 2 | 0 | 2 |
| 0x0E2D | crystal ball | 1 | 2 | 0 | 2 |
| 0x1C86 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x0557 | stone roof | 1 | 2 | 0 | 2 |
| 0x12A7 | tarot | 1 | 2 | 0 | 2 |
| 0x1E9E | pulley | 6 | 2 | 0 | 2 |
| 0xAE63 | Tracks East Switch S | 1 | 2 | 0 | 2 |
| 0x0658 | slate roof | 0 | 2 | 0 | 2 |
| 0x4573 | Icicle Med South | 0 | 2 | 0 | 2 |
| 0x14CA | Honor | 0 | 2 | 0 | 2 |
| 0x1C8C | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1C8F | sarcophagus | 2 | 2 | 0 | 2 |
| 0x153B | half apron | 0 | 2 | 0 | 2 |
| 0x4C5E | Monastery Bell L SOU | 12 | 2 | 0 | 2 |
| 0x1E88 | skinned goat | 2 | 2 | 0 | 2 |
| 0xAE62 | Tracks East Switch S | 1 | 2 | 0 | 2 |
| 0x1C8E | sarcophagus | 2 | 2 | 0 | 2 |
| 0x00C1 | wooden wall | 3 | 0 | 2 | 2 |
| 0x2855 | sword display | 4 | 2 | 0 | 2 |
| 0x361F |  | 0 | 0 | 2 | 2 |
| 0x1C2D | casket | 3 | 2 | 0 | 2 |
| 0x1BF2 | iron ingot%s | 1 | 2 | 0 | 2 |
| 0x3AF9 |  | 1 | 0 | 2 | 2 |
| 0x07F7 | suspension bridge | 20 | 2 | 0 | 2 |
| 0x127F | pillory | 5 | 2 | 0 | 2 |
| 0x1687 | catapult | 3 | 2 | 0 | 2 |
| 0x1CD3 | blood | 0 | 2 | 0 | 2 |
| 0x5732 | crystalline blackroc | 1 | 2 | 0 | 2 |
| 0x345C | lava | 0 | 2 | 0 | 2 |
| 0x4085 | decorative paver | 0 | 2 | 0 | 2 |
| 0x0F68 | easel with canvas | 12 | 2 | 0 | 2 |
| 0x307F | ArcaneCircle SOUTH | 1 | 2 | 0 | 2 |
| 0x1DBA | leather cap | 1 | 2 | 0 | 2 |
| 0x0E17 | cards | 1 | 2 | 0 | 2 |
| 0x0F89 | Obsidian | 1 | 2 | 0 | 2 |
| 0x04C6 | wooden planks | 0 | 0 | 2 | 2 |
| 0x13D6 | studded gorget | 1 | 2 | 0 | 2 |
| 0x1C2E | casket | 2 | 2 | 0 | 2 |
| 0x1C2B | casket | 3 | 2 | 0 | 2 |
| 0x0D20 | grapevines | 1 | 2 | 0 | 2 |
| 0x0E16 | cards | 1 | 2 | 0 | 2 |
| 0x13F1 | ringmail leggings | 1 | 2 | 0 | 2 |
| 0x3B42 |  | 0 | 0 | 2 | 2 |
| 0x0EB4 | lute | 3 | 2 | 0 | 2 |
| 0x0DF9 | bale%s% of cotton | 1 | 2 | 0 | 2 |
| 0x04C8 | wooden planks | 0 | 0 | 2 | 2 |
| 0x1280 | pillory | 5 | 2 | 0 | 2 |
| 0x143F | halberd | 1 | 2 | 0 | 2 |
| 0x0E8A | quarter staff | 1 | 2 | 0 | 2 |
| 0x1C6C | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1538 | kilt | 1 | 2 | 0 | 2 |
| 0x1021 | feathers | 1 | 2 | 0 | 2 |
| 0x13FA | large battle axe | 1 | 2 | 0 | 2 |
| 0x3457 | lava | 0 | 2 | 0 | 2 |
| 0x102F | nails | 1 | 2 | 0 | 2 |
| 0x09E8 | dirty pan | 1 | 2 | 0 | 2 |
| 0x982F | TrainingDummyAdv Fra | 1 | 2 | 0 | 2 |
| 0x1CA6 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1F0E | morning glory | 0 | 2 | 0 | 2 |
| 0x1B45 | treasure | 0 | 2 | 0 | 2 |
| 0x1BD3 | feathers | 1 | 2 | 0 | 2 |
| 0x44D9 | ClockNice Frame01 EA | 5 | 2 | 0 | 2 |
| 0x41EF | Wallset1 FloorB East | 0 | 2 | 0 | 2 |
| 0x1ECC | pylon | 4 | 2 | 0 | 2 |
| 0x284B | green tea | 4 | 2 | 0 | 2 |
| 0x3647 |  | 20 | 0 | 2 | 2 |
| 0x1C61 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x1CB4 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x0EB3 | lute | 3 | 2 | 0 | 2 |
| 0x1CA2 | sarcophagus | 2 | 2 | 0 | 2 |
| 0x2227 | crystal | 10 | 2 | 0 | 2 |
| 0x229B |  | 0 | 0 | 2 | 2 |
| 0x46BA | CrabMount SOUTH | 1 | 2 | 0 | 2 |
| 0x1BED | gold ingots | 1 | 2 | 0 | 2 |
| 0x46BC | Mounted Lobster SOUT | 1 | 2 | 0 | 2 |
| 0x4C01 | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x13ED | ringmail tunic | 1 | 2 | 0 | 2 |
| 0x44D2 | Crab EAST | 1 | 2 | 0 | 2 |
| 0x07B0 | cave steps | 22 | 2 | 0 | 2 |
| 0x19B1 | fire | 0 | 2 | 0 | 2 |
| 0x3062 | MultiTile Assets | 5 | 2 | 0 | 2 |
| 0x1E0B | stain | 0 | 2 | 0 | 2 |
| 0x14B8 | Valor | 0 | 2 | 0 | 2 |
| 0x2472 | cherry blossoms | 20 | 2 | 0 | 2 |
| 0x4BFE | Davies Locker SOUTH | 5 | 2 | 0 | 2 |
| 0x447A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0E5A |  | 0 | 0 | 1 | 1 |
| 0x445C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x441B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x170F | shoes | 1 | 1 | 0 | 1 |
| 0x1027 | chisels | 1 | 1 | 0 | 1 |
| 0x442E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x150C | Sacrifice | 0 | 1 | 0 | 1 |
| 0x1E0A | stain | 0 | 1 | 0 | 1 |
| 0x4423 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x13E2 | studded tunic | 1 | 1 | 0 | 1 |
| 0x8DB3 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x181E | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x3EB6 | ship | 3 | 1 | 0 | 1 |
| 0x443C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1476 | telescope | 10 | 1 | 0 | 1 |
| 0x446E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x165F | catapult | 3 | 1 | 0 | 1 |
| 0x1486 | telescope | 10 | 1 | 0 | 1 |
| 0x448E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0C68 | sprouts | 1 | 1 | 0 | 1 |
| 0x3E89 | gang plank | 3 | 1 | 0 | 1 |
| 0x09E0 | pot | 1 | 1 | 0 | 1 |
| 0x3E78 | rudder | 3 | 1 | 0 | 1 |
| 0x1E0D | stain | 0 | 1 | 0 | 1 |
| 0x0415 | Missing_Name | 0 | 1 | 0 | 1 |
| 0x4B8B | VanillaPlantMedium S | 1 | 1 | 0 | 1 |
| 0x1E65 | trophy | 0 | 1 | 0 | 1 |
| 0x0EBF | sheet music | 5 | 1 | 0 | 1 |
| 0x30F2 | sinkhole | 0 | 1 | 0 | 1 |
| 0x43F8 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1AB9 | lighthouse | 12 | 1 | 0 | 1 |
| 0x1B72 | bronze shield | 1 | 1 | 0 | 1 |
| 0x0A1C | hanging lantern | 0 | 1 | 0 | 1 |
| 0x444D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4466 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x146D | telescope | 10 | 1 | 0 | 1 |
| 0x1487 | telescope | 10 | 1 | 0 | 1 |
| 0x4B6A | CityFlag Skara SOUTH | 0 | 1 | 0 | 1 |
| 0x14AA | Compassion | 0 | 1 | 0 | 1 |
| 0x13AE | pillow | 1 | 1 | 0 | 1 |
| 0x1C12 | Bell of Courage | 1 | 1 | 0 | 1 |
| 0x1488 | telescope | 10 | 1 | 0 | 1 |
| 0x4B71 | CityFlag Vesper EAST | 0 | 1 | 0 | 1 |
| 0x30F6 | sinkhole | 0 | 1 | 0 | 1 |
| 0x4577 | Icicle Small East | 0 | 1 | 0 | 1 |
| 0x403A | gargoyle floor mirro | 20 | 1 | 0 | 1 |
| 0x19F8 | vise | 3 | 1 | 0 | 1 |
| 0x63BF | Harpsichord SOUTH | 1 | 1 | 0 | 1 |
| 0x148E | telescope | 10 | 1 | 0 | 1 |
| 0x1CB1 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x4451 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4444 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4424 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x147E | telescope | 10 | 1 | 0 | 1 |
| 0x1E40 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x0C7F | ear%s% of corn | 1 | 1 | 0 | 1 |
| 0x44BA | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1827 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x12F6 | lava | 0 | 1 | 0 | 1 |
| 0x8DBB | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x13D0 | leather leggings | 7 | 1 | 0 | 1 |
| 0x145B | telescope | 10 | 1 | 0 | 1 |
| 0x4433 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2460 | sand | 0 | 1 | 0 | 1 |
| 0x12B1 | scaffold | 15 | 1 | 0 | 1 |
| 0x1CAE | sarcophagus lid | 2 | 1 | 0 | 1 |
| 0x30FE | sinkhole | 0 | 1 | 0 | 1 |
| 0x3101 | sinkhole | 0 | 1 | 0 | 1 |
| 0x4B65 | CityFlag Jhelom SOUT | 0 | 1 | 0 | 1 |
| 0x44B4 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x445B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3E9E | ship | 3 | 1 | 0 | 1 |
| 0x440F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x444C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4409 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4428 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x09B6 | fried eggs | 1 | 1 | 0 | 1 |
| 0x1C7F | sarcophagus lid | 2 | 1 | 0 | 1 |
| 0x1A6D | lava | 0 | 1 | 0 | 1 |
| 0x4400 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1C00 | leather shorts | 1 | 1 | 0 | 1 |
| 0x1BC0 | stone trigger | 0 | 1 | 0 | 1 |
| 0x43FF | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3AF6 | ree | 0 | 1 | 0 | 1 |
| 0x1461 | telescope | 10 | 1 | 0 | 1 |
| 0x1C74 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1ABB | lighthouse | 12 | 1 | 0 | 1 |
| 0x8DA1 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1471 | telescope | 10 | 1 | 0 | 1 |
| 0x30EC | sinkhole | 0 | 1 | 0 | 1 |
| 0x20FB | sea serpent | 0 | 1 | 0 | 1 |
| 0x10A6 | spinning wheel | 5 | 1 | 0 | 1 |
| 0x1020 | feathers | 1 | 1 | 0 | 1 |
| 0x44A4 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1CBB | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1C56 | coffin | 2 | 1 | 0 | 1 |
| 0x3E9B | ship | 3 | 1 | 0 | 1 |
| 0x2D71 | elven tapestry | 0 | 1 | 0 | 1 |
| 0x3D6B | corpse of rabbit | 0 | 1 | 0 | 1 |
| 0x0A37 | drawer | 0 | 1 | 0 | 1 |
| 0x8DAD | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x442C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3105 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1514 | Serpent Crest | 0 | 1 | 0 | 1 |
| 0x1E77 | board | 3 | 1 | 0 | 1 |
| 0x1C70 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x3EA8 | ship | 3 | 1 | 0 | 1 |
| 0x1821 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x2B71 | Raven Helm | 1 | 1 | 0 | 1 |
| 0x1B5C | treasure | 0 | 1 | 0 | 1 |
| 0x3180 | Lt_Armor_Kilt | 2 | 1 | 0 | 1 |
| 0x4410 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x443A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x42BC | bust | 0 | 1 | 0 | 1 |
| 0x0019 | wooden wall | 3 | 0 | 1 | 1 |
| 0x1ABA | lighthouse | 12 | 1 | 0 | 1 |
| 0x0F5F | broadsword | 1 | 1 | 0 | 1 |
| 0x14AC | Compassion | 0 | 1 | 0 | 1 |
| 0x14AE | Compassion | 0 | 1 | 0 | 1 |
| 0x4439 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1BC4 | Order shield | 1 | 1 | 0 | 1 |
| 0x1483 | telescope | 10 | 1 | 0 | 1 |
| 0x1654 | catapult | 3 | 1 | 0 | 1 |
| 0x100F | gold key | 1 | 1 | 0 | 1 |
| 0x4463 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B2F | dirt | 0 | 1 | 0 | 1 |
| 0x4421 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DBC | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x43E5 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x13E9 | ringmail sleeves | 1 | 1 | 0 | 1 |
| 0x4425 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x140D | norse helm | 1 | 1 | 0 | 1 |
| 0x1AA4 | lighthouse | 12 | 1 | 0 | 1 |
| 0x3087 | elven loveseat | 1 | 1 | 0 | 1 |
| 0x8DA7 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x4401 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0377 | patch | 0 | 1 | 0 | 1 |
| 0x163B | pillow | 1 | 1 | 0 | 1 |
| 0x145F | telescope | 10 | 1 | 0 | 1 |
| 0x1495 | telescope | 10 | 1 | 0 | 1 |
| 0x2DF5 | elven reading desk | 5 | 1 | 0 | 1 |
| 0x1466 | telescope | 10 | 1 | 0 | 1 |
| 0x108E | lever | 2 | 1 | 0 | 1 |
| 0x3BB7 |  | 0 | 0 | 1 | 1 |
| 0x4450 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0374 | patch | 0 | 1 | 0 | 1 |
| 0x1C09 | leather skirt | 1 | 1 | 0 | 1 |
| 0x166E | whip | 1 | 1 | 0 | 1 |
| 0x14A7 | Compassion | 0 | 1 | 0 | 1 |
| 0x1ABD | lighthouse | 12 | 1 | 0 | 1 |
| 0x63C1 | Harpsichord SOUTH | 1 | 1 | 0 | 1 |
| 0x8DA8 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x41E4 | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x1C41 | coffin | 2 | 1 | 0 | 1 |
| 0x8DAE | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1E95 | rabbit on a spit | 3 | 1 | 0 | 1 |
| 0x11B5 | floor saw | 5 | 1 | 0 | 1 |
| 0x13EA | ringmail sleeves | 1 | 1 | 0 | 1 |
| 0x0C34 | sheets | 3 | 1 | 0 | 1 |
| 0x13FF | katana | 1 | 1 | 0 | 1 |
| 0x0E13 | chessmen | 1 | 1 | 0 | 1 |
| 0x1460 | telescope | 10 | 1 | 0 | 1 |
| 0x44AB | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1826 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x1E28 | oar | 0 | 1 | 0 | 1 |
| 0x0C33 | sheets | 3 | 1 | 0 | 1 |
| 0x447E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44A6 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4420 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B61 | treasure | 0 | 1 | 0 | 1 |
| 0x2848 | sculpture | 4 | 1 | 0 | 1 |
| 0x3104 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1BF0 | iron ingots | 1 | 1 | 0 | 1 |
| 0x4408 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0FDB | tapestry | 0 | 1 | 0 | 1 |
| 0x1467 | telescope | 10 | 1 | 0 | 1 |
| 0x2821 |  | 0 | 0 | 1 | 1 |
| 0x44BB | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3E9D | ship | 3 | 1 | 0 | 1 |
| 0x4413 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1497 | telescope | 10 | 1 | 0 | 1 |
| 0x362F |  | 3 | 0 | 1 | 1 |
| 0x3E9C | ship | 3 | 1 | 0 | 1 |
| 0x2E2B | acid | 0 | 1 | 0 | 1 |
| 0x14B4 | Justice | 0 | 1 | 0 | 1 |
| 0x1481 | telescope | 10 | 1 | 0 | 1 |
| 0x3109 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1477 | telescope | 10 | 1 | 0 | 1 |
| 0x365E |  | 0 | 0 | 1 | 1 |
| 0x43E3 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1485 | telescope | 10 | 1 | 0 | 1 |
| 0x1F03 | robe | 1 | 1 | 0 | 1 |
| 0x350D | waterfall | 5 | 1 | 0 | 1 |
| 0x1463 | telescope | 10 | 1 | 0 | 1 |
| 0x4482 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1EEA | magical door | 0 | 1 | 0 | 1 |
| 0x124D | iron maiden | 15 | 1 | 0 | 1 |
| 0x1C42 | coffin | 2 | 1 | 0 | 1 |
| 0x4437 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x18E8 | nightshade | 1 | 1 | 0 | 1 |
| 0x071A | marble stairs | 5 | 1 | 0 | 1 |
| 0x13C8 | leather sleeves | 6 | 1 | 0 | 1 |
| 0x1CB9 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x8DB9 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x30FF | sinkhole | 0 | 1 | 0 | 1 |
| 0x44AD | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x63BE | Harpsichord SOUTH | 1 | 1 | 0 | 1 |
| 0x1C50 | coffin | 2 | 1 | 0 | 1 |
| 0x1BFA | silver ingots | 1 | 1 | 0 | 1 |
| 0x1BFC | crossbow bolts | 1 | 1 | 0 | 1 |
| 0x43F0 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4435 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x30E9 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1464 | telescope | 10 | 1 | 0 | 1 |
| 0x1CBE | sarcophagus | 2 | 1 | 0 | 1 |
| 0x147B | telescope | 10 | 1 | 0 | 1 |
| 0x1CB5 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x4497 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3EA0 | ship | 3 | 1 | 0 | 1 |
| 0x2D0C | elvin wash basin | 5 | 1 | 0 | 1 |
| 0x365F |  | 0 | 0 | 1 | 1 |
| 0x1440 | cutlass | 1 | 1 | 0 | 1 |
| 0x055B | stone roof | 1 | 1 | 0 | 1 |
| 0x17E5 | teleporter | 0 | 1 | 0 | 1 |
| 0x0A67 | sheets | 2 | 1 | 0 | 1 |
| 0x11E3 | grass mat | 1 | 1 | 0 | 1 |
| 0x30F9 | sinkhole | 0 | 1 | 0 | 1 |
| 0x41DD | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x14AF | Justice | 0 | 1 | 0 | 1 |
| 0x0BB4 | Inn | 0 | 1 | 0 | 1 |
| 0x0FD5 | tapestry | 0 | 1 | 0 | 1 |
| 0x3631 |  | 3 | 0 | 1 | 1 |
| 0x1C71 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x5738 | crystal shards | 1 | 1 | 0 | 1 |
| 0x181D | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x44AE | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x448D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1BF1 | iron ingots | 1 | 1 | 0 | 1 |
| 0x1FAD | wave | 0 | 1 | 0 | 1 |
| 0x0FD8 | tapestry | 0 | 1 | 0 | 1 |
| 0x1820 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x4429 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0147 | log post | 20 | 1 | 0 | 1 |
| 0x0018 | wooden wall | 3 | 0 | 1 | 1 |
| 0x0F71 | rack of canvases | 12 | 1 | 0 | 1 |
| 0x1DF2 | rug | 0 | 1 | 0 | 1 |
| 0x184B | heating stand | 1 | 1 | 0 | 1 |
| 0x1E41 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x1344 | lava | 0 | 1 | 0 | 1 |
| 0x4B5C | MeerCorpse SOUTH | 1 | 1 | 0 | 1 |
| 0x1E29 | oar | 0 | 1 | 0 | 1 |
| 0x4407 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DA3 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x2209 | crystal | 10 | 1 | 0 | 1 |
| 0x4475 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x13A4 | pillow | 1 | 1 | 0 | 1 |
| 0x1D1C | body | 4 | 1 | 0 | 1 |
| 0x1489 | telescope | 10 | 1 | 0 | 1 |
| 0x1459 | telescope | 10 | 1 | 0 | 1 |
| 0x8DB0 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x8DB1 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1CB3 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1ECE | light | 0 | 1 | 0 | 1 |
| 0x0E08 | wig stand | 0 | 1 | 0 | 1 |
| 0x441C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x443D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B4C | treasure | 0 | 1 | 0 | 1 |
| 0x4496 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1FC6 | wave | 0 | 1 | 0 | 1 |
| 0x43FA | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0F06 | Black Potion | 1 | 1 | 0 | 1 |
| 0x41E3 | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x0F9F | scissors | 1 | 1 | 0 | 1 |
| 0x43F4 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0FC1 | paints and brush | 1 | 1 | 0 | 1 |
| 0x1016 | spinning wheel | 4 | 1 | 0 | 1 |
| 0x1C3D | coffin | 2 | 1 | 0 | 1 |
| 0x43EA | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44A3 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DB4 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x4470 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4412 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4445 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E35 | scarecrow | 15 | 1 | 0 | 1 |
| 0x444B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0F65 | easel | 12 | 1 | 0 | 1 |
| 0x4478 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44A9 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x14F3 | ship model | 1 | 1 | 0 | 1 |
| 0x2474 | cherry blossoms | 20 | 1 | 0 | 1 |
| 0x448B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x108C | lever | 2 | 1 | 0 | 1 |
| 0x43E0 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x147F | telescope | 10 | 1 | 0 | 1 |
| 0x1EAE | winch | 3 | 1 | 0 | 1 |
| 0x3EA5 | ship | 3 | 1 | 0 | 1 |
| 0x1ABF | lighthouse | 12 | 1 | 0 | 1 |
| 0x19B6 | fire | 0 | 1 | 0 | 1 |
| 0x440B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1083 | uncooked pizza%s% | 0 | 1 | 0 | 1 |
| 0x3106 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1825 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x148C | telescope | 10 | 1 | 0 | 1 |
| 0x146C | telescope | 10 | 1 | 0 | 1 |
| 0x26B7 | zoogi fungus | 1 | 1 | 0 | 1 |
| 0x1E89 | skinned goat | 2 | 1 | 0 | 1 |
| 0x4499 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x41E2 | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x30F3 | sinkhole | 0 | 1 | 0 | 1 |
| 0x18DA | ValentineRose DropSp | 1 | 1 | 0 | 1 |
| 0x1ACF | lighthouse | 12 | 1 | 0 | 1 |
| 0x0F09 | White Potion | 1 | 1 | 0 | 1 |
| 0x3560 | lava | 0 | 1 | 0 | 1 |
| 0x1472 | telescope | 10 | 1 | 0 | 1 |
| 0x43F9 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1502 | plough | 1 | 1 | 0 | 1 |
| 0x1F74 | fur | 1 | 1 | 0 | 1 |
| 0x0F44 | hatchet | 1 | 1 | 0 | 1 |
| 0x245E | sand | 0 | 1 | 0 | 1 |
| 0x0F6A | easel with canvas | 12 | 1 | 0 | 1 |
| 0x4C66 | Landscape L 01 SOUTH | 1 | 1 | 0 | 1 |
| 0x150E | Sacrifice | 0 | 1 | 0 | 1 |
| 0x1DF9 | rug | 0 | 1 | 0 | 1 |
| 0x447C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x43FC | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x446C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4C60 | Landscape S 01 SOUTH | 1 | 1 | 0 | 1 |
| 0x124A | iron maiden | 15 | 1 | 0 | 1 |
| 0x44A5 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x9994 | Roasting Pig Grill E | 1 | 1 | 0 | 1 |
| 0x4480 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DB5 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x146F | telescope | 10 | 1 | 0 | 1 |
| 0x240C | fan | 2 | 1 | 0 | 1 |
| 0x175F | folded cloth | 1 | 1 | 0 | 1 |
| 0x3103 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1CEE | liver | 1 | 1 | 0 | 1 |
| 0x30F4 | sinkhole | 0 | 1 | 0 | 1 |
| 0x2A49 | Sword Display | 4 | 1 | 0 | 1 |
| 0x43ED | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0BF0 | Archers' Guild | 0 | 1 | 0 | 1 |
| 0x14B1 | Justice | 0 | 1 | 0 | 1 |
| 0x30EE | sinkhole | 0 | 1 | 0 | 1 |
| 0x4B70 | CityFlag Minoc EAST | 0 | 1 | 0 | 1 |
| 0x1493 | telescope | 10 | 1 | 0 | 1 |
| 0x1ACD | lighthouse | 12 | 1 | 0 | 1 |
| 0x1852 | scales | 1 | 1 | 0 | 1 |
| 0x13DD | studded gloves | 1 | 1 | 0 | 1 |
| 0x19F4 | vise | 3 | 1 | 0 | 1 |
| 0x1494 | telescope | 10 | 1 | 0 | 1 |
| 0x171C | jester hat | 1 | 1 | 0 | 1 |
| 0x1B3B | dirt | 0 | 1 | 0 | 1 |
| 0x3E31 | Field of fire | 12 | 1 | 0 | 1 |
| 0x4414 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2D73 | elven wall map | 0 | 1 | 0 | 1 |
| 0x02F1 | patch | 0 | 1 | 0 | 1 |
| 0x448C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0EB0 |  | 0 | 0 | 1 | 1 |
| 0x2471 | cherry blossoms | 20 | 1 | 0 | 1 |
| 0x446D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3660 |  | 15 | 0 | 1 | 1 |
| 0x30E8 | sinkhole | 0 | 1 | 0 | 1 |
| 0x149A | telescope | 10 | 1 | 0 | 1 |
| 0x1B44 | treasure | 0 | 1 | 0 | 1 |
| 0x1CB7 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x12CA | bust | 0 | 1 | 0 | 1 |
| 0x4422 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x284E | miso | 4 | 1 | 0 | 1 |
| 0x1282 | pillory | 5 | 1 | 0 | 1 |
| 0x449B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E32 | dartboard w/knives | 0 | 1 | 0 | 1 |
| 0x13D7 | studded sleeves | 7 | 1 | 0 | 1 |
| 0x1479 | telescope | 10 | 1 | 0 | 1 |
| 0x18B5 | ballista | 7 | 1 | 0 | 1 |
| 0x1C53 | coffin | 2 | 1 | 0 | 1 |
| 0x4416 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x63C2 | Harpsichord SOUTH | 1 | 1 | 0 | 1 |
| 0x3108 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1ABC | lighthouse | 12 | 1 | 0 | 1 |
| 0x8DA6 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x440D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x18DE | mandrake root | 1 | 1 | 0 | 1 |
| 0x442B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1655 | catapult | 3 | 1 | 0 | 1 |
| 0x44B5 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1469 | telescope | 10 | 1 | 0 | 1 |
| 0x1E82 | lathe work | 2 | 1 | 0 | 1 |
| 0x449A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1EB7 | empty tool kit | 1 | 1 | 0 | 1 |
| 0x1E44 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x449E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x24CD | paper screen | 10 | 1 | 0 | 1 |
| 0x3100 | sinkhole | 0 | 1 | 0 | 1 |
| 0x2D06 | simple elvin armoir | 15 | 1 | 0 | 1 |
| 0x13F2 | ringmail gloves | 1 | 1 | 0 | 1 |
| 0x8DA9 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x9C18 | safe left | 10 | 1 | 0 | 1 |
| 0x30F5 | sinkhole | 0 | 1 | 0 | 1 |
| 0x28DD | bonsai tree | 2 | 1 | 0 | 1 |
| 0x30E7 | sinkhole | 0 | 1 | 0 | 1 |
| 0x30E2 | elven wash basin | 5 | 1 | 0 | 1 |
| 0x0DFB | feathers | 1 | 1 | 0 | 1 |
| 0x1CB2 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1474 | telescope | 10 | 1 | 0 | 1 |
| 0x1468 | telescope | 10 | 1 | 0 | 1 |
| 0x2D0B | elvin wash basin | 5 | 1 | 0 | 1 |
| 0x1813 | hourglass | 1 | 1 | 0 | 1 |
| 0x4468 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1028 | dovetail saw | 1 | 1 | 0 | 1 |
| 0x148B | telescope | 10 | 1 | 0 | 1 |
| 0x4449 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DA4 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1C80 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x0555 | stone roof | 1 | 1 | 0 | 1 |
| 0x1E47 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x4481 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x445E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1ACE | lighthouse | 12 | 1 | 0 | 1 |
| 0x09E4 | pot | 1 | 1 | 0 | 1 |
| 0x1E43 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x1462 | telescope | 10 | 1 | 0 | 1 |
| 0x1484 | telescope | 10 | 1 | 0 | 1 |
| 0x148F | telescope | 10 | 1 | 0 | 1 |
| 0x24CF | paper screen | 10 | 1 | 0 | 1 |
| 0x1409 | close helm | 1 | 1 | 0 | 1 |
| 0x1D54 | skeleton | 4 | 1 | 0 | 1 |
| 0x4B8C | VanillaPlantLarge SO | 1 | 1 | 0 | 1 |
| 0x1D1B | body | 4 | 1 | 0 | 1 |
| 0x1C85 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x11A8 | gas trap | 0 | 1 | 0 | 1 |
| 0x1EBB | tool kit | 3 | 1 | 0 | 1 |
| 0x0500 | flagstones | 0 | 1 | 0 | 1 |
| 0x4454 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x04AE | wooden boards | 0 | 0 | 1 | 1 |
| 0x11E2 | grass mat | 1 | 1 | 0 | 1 |
| 0x30E1 | elven wash basin | 5 | 1 | 0 | 1 |
| 0x43F1 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44A0 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x41F2 | Wallset1 FloorB East | 0 | 1 | 0 | 1 |
| 0x13B9 | viking sword | 1 | 1 | 0 | 1 |
| 0xAF1D | Hildebrandt tapestry | 1 | 1 | 0 | 1 |
| 0x4432 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3635 |  | 3 | 0 | 1 | 1 |
| 0x0DE2 | kindling | 1 | 1 | 0 | 1 |
| 0x1C7E | sarcophagus lid | 2 | 1 | 0 | 1 |
| 0x1663 | catapult | 3 | 1 | 0 | 1 |
| 0x4448 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x449C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1266 | wall carving | 0 | 1 | 0 | 1 |
| 0x44AC | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x9A19 | Marble Bar Sink Fill | 9 | 1 | 0 | 1 |
| 0x4453 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x150F | Sacrifice | 0 | 1 | 0 | 1 |
| 0x1C72 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1C07 | leather armor | 1 | 1 | 0 | 1 |
| 0x3464 | lava | 0 | 1 | 0 | 1 |
| 0x1009 | basin | 1 | 1 | 0 | 1 |
| 0x4419 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1BC5 | Order shield | 1 | 1 | 0 | 1 |
| 0x18DF | mandrake | 1 | 1 | 0 | 1 |
| 0x4462 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x24C4 | paper lantern | 10 | 1 | 0 | 1 |
| 0x9FE8 | Vault East | 4 | 1 | 0 | 1 |
| 0x0FC2 |  | 1 | 0 | 1 | 1 |
| 0x4B62 | CityFlag Trinsic SOU | 0 | 1 | 0 | 1 |
| 0x148D | telescope | 10 | 1 | 0 | 1 |
| 0x20FA | reaper | 0 | 1 | 0 | 1 |
| 0x4487 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x062F | stone roof | 1 | 1 | 0 | 1 |
| 0x44BF | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1095 | lever | 2 | 1 | 0 | 1 |
| 0x444F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2833 | windchimes | 2 | 1 | 0 | 1 |
| 0x2E2C | acid | 0 | 1 | 0 | 1 |
| 0x3561 | lava | 0 | 1 | 0 | 1 |
| 0x1F7A | fur | 1 | 1 | 0 | 1 |
| 0x020E |  | 1 | 0 | 1 | 1 |
| 0x167E | catapult | 3 | 1 | 0 | 1 |
| 0x43F3 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x40BD | horse | 5 | 1 | 0 | 1 |
| 0x445F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x63C0 | Harpsichord SOUTH | 1 | 1 | 0 | 1 |
| 0x1482 | telescope | 10 | 1 | 0 | 1 |
| 0x1470 | telescope | 10 | 1 | 0 | 1 |
| 0x4469 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44B9 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2D51 | Arcane Circle Scrol | 0 | 1 | 0 | 1 |
| 0x1044 | spam | 1 | 1 | 0 | 1 |
| 0x318C |  | 1 | 0 | 1 | 1 |
| 0x2854 | sword display | 4 | 1 | 0 | 1 |
| 0x145A | telescope | 10 | 1 | 0 | 1 |
| 0x1544 | skullcap | 1 | 1 | 0 | 1 |
| 0x1C82 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x13E1 | studded leggings | 1 | 1 | 0 | 1 |
| 0x8DB2 | ShipSails D2 NORTH | 20 | 1 | 0 | 1 |
| 0x1B67 | treasure | 0 | 1 | 0 | 1 |
| 0x4440 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2FDD | tall crystal | 10 | 1 | 0 | 1 |
| 0x4574 | Icicle Small South | 0 | 1 | 0 | 1 |
| 0x14AD | Compassion | 0 | 1 | 0 | 1 |
| 0x1D19 | body | 4 | 1 | 0 | 1 |
| 0x0A17 | lantern | 1 | 1 | 0 | 1 |
| 0x442D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1C75 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x30EA | sinkhole | 0 | 1 | 0 | 1 |
| 0x246A | sand | 0 | 1 | 0 | 1 |
| 0x147C | telescope | 10 | 1 | 0 | 1 |
| 0x140B | helmet | 1 | 1 | 0 | 1 |
| 0x41DC | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x1CB0 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x0C36 | sheets | 3 | 1 | 0 | 1 |
| 0x1AC7 | lighthouse | 12 | 1 | 0 | 1 |
| 0x44B6 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1CCC | blood | 0 | 1 | 0 | 1 |
| 0x14B5 | Justice | 0 | 1 | 0 | 1 |
| 0x153C | half apron | 0 | 1 | 0 | 1 |
| 0x30FD | sinkhole | 0 | 1 | 0 | 1 |
| 0x171A | feathered hat | 1 | 1 | 0 | 1 |
| 0x447D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x446A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2D60 | Arcane Empower | 0 | 1 | 0 | 1 |
| 0x4441 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E48 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x2461 | sand | 0 | 1 | 0 | 1 |
| 0x295F | futon | 3 | 1 | 0 | 1 |
| 0x1D58 | skeleton | 4 | 1 | 0 | 1 |
| 0x1ACA | lighthouse | 12 | 1 | 0 | 1 |
| 0x448F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1F78 | fur | 1 | 1 | 0 | 1 |
| 0x1824 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x446F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4446 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x165E | catapult | 3 | 1 | 0 | 1 |
| 0x1AAD | lighthouse | 12 | 1 | 0 | 1 |
| 0x4431 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x150B | Sacrifice | 0 | 1 | 0 | 1 |
| 0x07C1 | platform | 2 | 1 | 0 | 1 |
| 0x146E | telescope | 10 | 1 | 0 | 1 |
| 0x447F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0F0D | Purple Potion | 1 | 1 | 0 | 1 |
| 0x140F | bascinet | 1 | 1 | 0 | 1 |
| 0x062E | stone roof | 1 | 1 | 0 | 1 |
| 0x1400 | kryss | 1 | 1 | 0 | 1 |
| 0x4465 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B57 | treasure | 0 | 1 | 0 | 1 |
| 0x376A | sparkle | 0 | 1 | 0 | 1 |
| 0x1AD0 | lighthouse | 12 | 1 | 0 | 1 |
| 0x44B7 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3513 | waterfall | 10 | 1 | 0 | 1 |
| 0x04AC | wooden boards | 0 | 0 | 1 | 1 |
| 0x2D1F | Magical Shortbow | 1 | 1 | 0 | 1 |
| 0x1B39 | dirt | 0 | 1 | 0 | 1 |
| 0x3E8A | ship | 3 | 1 | 0 | 1 |
| 0x0EBB | music stand | 5 | 1 | 0 | 1 |
| 0x2224 | crystal | 10 | 1 | 0 | 1 |
| 0x0EAA | tapestry | 0 | 1 | 0 | 1 |
| 0x1CBD | sarcophagus | 2 | 1 | 0 | 1 |
| 0x0E12 | chessmen | 1 | 1 | 0 | 1 |
| 0x1F01 | plain dress | 1 | 1 | 0 | 1 |
| 0x3EA6 | ship | 3 | 1 | 0 | 1 |
| 0x1F04 | robe | 1 | 1 | 0 | 1 |
| 0x04DD | wooden planks | 0 | 0 | 1 | 1 |
| 0x448A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4456 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3451 | lava | 0 | 1 | 0 | 1 |
| 0x18B0 | ballista | 7 | 1 | 0 | 1 |
| 0x4489 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B5A | treasure | 0 | 1 | 0 | 1 |
| 0x3228 | swamp | 0 | 1 | 0 | 1 |
| 0x1AB0 | lighthouse | 12 | 1 | 0 | 1 |
| 0x2B73 | Winged Helm | 1 | 1 | 0 | 1 |
| 0x4467 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0A24 | lantern | 1 | 1 | 0 | 1 |
| 0x2A46 | sword display | 4 | 1 | 0 | 1 |
| 0x449F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x14B3 | Justice | 0 | 1 | 0 | 1 |
| 0x150A | Sacrifice | 0 | 1 | 0 | 1 |
| 0x164D | catapult | 3 | 1 | 0 | 1 |
| 0x0EE8 | grave | 0 | 1 | 0 | 1 |
| 0x1379 | horse barding | 0 | 1 | 0 | 1 |
| 0x3999 | Field of fire | 12 | 1 | 0 | 1 |
| 0x19B4 | fire | 0 | 1 | 0 | 1 |
| 0x9A97 | Coffee Grinder SOUTH | 3 | 1 | 0 | 1 |
| 0x43E4 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1CB8 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x1C81 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x3798 | death vortex | 2 | 1 | 0 | 1 |
| 0x5721 | daemon claw | 1 | 1 | 0 | 1 |
| 0x1031 | jointing plane | 1 | 1 | 0 | 1 |
| 0x148A | telescope | 10 | 1 | 0 | 1 |
| 0x13DC | studded sleeves | 1 | 1 | 0 | 1 |
| 0x43E1 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x444A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2228 | crystal | 10 | 1 | 0 | 1 |
| 0x344F | lava | 0 | 1 | 0 | 1 |
| 0x1AA7 | lighthouse | 12 | 1 | 0 | 1 |
| 0x43EC | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1108 | spike trap | 0 | 1 | 0 | 1 |
| 0x2469 | sand | 0 | 1 | 0 | 1 |
| 0x1011 | key ring | 1 | 1 | 0 | 1 |
| 0x1B27 | dirt | 0 | 1 | 0 | 1 |
| 0x07C6 | platform | 2 | 1 | 0 | 1 |
| 0x4474 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x9EDB | incense_south | 1 | 1 | 0 | 1 |
| 0x8DB8 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x41DF | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x1465 | telescope | 10 | 1 | 0 | 1 |
| 0x0558 | stone roof | 1 | 1 | 0 | 1 |
| 0x1766 | cut cloth | 1 | 1 | 0 | 1 |
| 0x1C83 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x44BC | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B32 | dirt | 0 | 1 | 0 | 1 |
| 0x1AAB | lighthouse | 12 | 1 | 0 | 1 |
| 0x164B | catapult | 3 | 1 | 0 | 1 |
| 0x44A7 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3EA7 | ship | 3 | 1 | 0 | 1 |
| 0x1267 | wall carving | 0 | 1 | 0 | 1 |
| 0x1ACB | lighthouse | 12 | 1 | 0 | 1 |
| 0x449D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1F77 | fur | 1 | 1 | 0 | 1 |
| 0x4491 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4418 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x447B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0BAB | Healer | 0 | 1 | 0 | 1 |
| 0x1DF1 | rug | 0 | 1 | 0 | 1 |
| 0x443B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1C76 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x573E | void orb | 1 | 1 | 0 | 1 |
| 0x1490 | telescope | 10 | 1 | 0 | 1 |
| 0x2818 | cabinet | 8 | 1 | 0 | 1 |
| 0x4464 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0379 | dagges | 0 | 1 | 0 | 1 |
| 0x2256 | small bagball | 2 | 1 | 0 | 1 |
| 0x1B47 | treasure | 0 | 1 | 0 | 1 |
| 0x42C4 | colorful tapestry | 0 | 1 | 0 | 1 |
| 0x441F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4405 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B5D | treasure | 0 | 1 | 0 | 1 |
| 0x1456 | bone helmet | 1 | 1 | 0 | 1 |
| 0x1093 | lever | 2 | 1 | 0 | 1 |
| 0x145D | telescope | 10 | 1 | 0 | 1 |
| 0x145C | telescope | 10 | 1 | 0 | 1 |
| 0x0FE4 | tapestry | 0 | 1 | 0 | 1 |
| 0x1B69 | treasure | 0 | 1 | 0 | 1 |
| 0x234E | sosaria tapestry | 1 | 1 | 0 | 1 |
| 0x5722 | vial of vitriol | 1 | 1 | 0 | 1 |
| 0x14A8 | Compassion | 0 | 1 | 0 | 1 |
| 0x44B8 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1F7D | glass of cider | 1 | 1 | 0 | 1 |
| 0x2D2A | Elven Composite Lo | 1 | 1 | 0 | 1 |
| 0x170D | sandals | 1 | 1 | 0 | 1 |
| 0x441A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x34B7 | whirlpool | 0 | 1 | 0 | 1 |
| 0x43FE | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x442A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1937 | crank | 0 | 1 | 0 | 1 |
| 0x1442 | two handed axe | 1 | 1 | 0 | 1 |
| 0x8DA2 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x02F4 | patch | 0 | 1 | 0 | 1 |
| 0x4436 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x56BA | carpet | 1 | 1 | 0 | 1 |
| 0x30F0 | sinkhole | 0 | 1 | 0 | 1 |
| 0x44C0 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0EDD | gravestone | 5 | 1 | 0 | 1 |
| 0x1F73 | fur | 1 | 1 | 0 | 1 |
| 0x1AB7 | lighthouse | 12 | 1 | 0 | 1 |
| 0x3630 |  | 3 | 0 | 1 | 1 |
| 0x2B5C | lava | 0 | 1 | 0 | 1 |
| 0x440C | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x140E | bascinet | 1 | 1 | 0 | 1 |
| 0x30ED | sinkhole | 0 | 1 | 0 | 1 |
| 0x1061 | upright loom | 8 | 1 | 0 | 1 |
| 0x44B1 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2DD9 | elven spinning whee | 5 | 1 | 0 | 1 |
| 0x56EF | carpet | 1 | 1 | 0 | 1 |
| 0x146A | telescope | 10 | 1 | 0 | 1 |
| 0x4417 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x2473 | cherry blossoms | 20 | 1 | 0 | 1 |
| 0x43E2 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x41ED | Wallset1 FloorB East | 0 | 1 | 0 | 1 |
| 0x1496 | telescope | 10 | 1 | 0 | 1 |
| 0x1F08 | necklace | 1 | 1 | 0 | 1 |
| 0x2851 | sword display | 4 | 1 | 0 | 1 |
| 0x1B6A | treasure | 0 | 1 | 0 | 1 |
| 0x13C1 | chainmail leggings | 1 | 1 | 0 | 1 |
| 0x1427 | Beeswax | 1 | 1 | 0 | 1 |
| 0x1AA6 | lighthouse | 12 | 1 | 0 | 1 |
| 0x4458 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x30FA | sinkhole | 0 | 1 | 0 | 1 |
| 0x1B59 | treasure | 0 | 1 | 0 | 1 |
| 0x43EF | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0A27 | candlabra | 5 | 1 | 0 | 1 |
| 0x1AAC | lighthouse | 12 | 1 | 0 | 1 |
| 0x0EAB | tapestry | 0 | 1 | 0 | 1 |
| 0x440E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1B6D | wall cracks | 0 | 1 | 0 | 1 |
| 0x31FA | dirt | 0 | 1 | 0 | 1 |
| 0x3B39 |  | 0 | 0 | 1 | 1 |
| 0x0EF0 | silver coin | 0 | 1 | 0 | 1 |
| 0x1823 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x365B |  | 15 | 0 | 1 | 1 |
| 0x8DAF | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x07C2 | platform | 2 | 1 | 0 | 1 |
| 0x37C3 | glow | 0 | 1 | 0 | 1 |
| 0x446B | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DA0 | ShipSails D2 NORTH | 20 | 1 | 0 | 1 |
| 0x4473 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E90 | skinned deer | 3 | 1 | 0 | 1 |
| 0x0E61 | rune | 0 | 1 | 0 | 1 |
| 0x1026 | chisels | 1 | 1 | 0 | 1 |
| 0x0FA6 | game board | 1 | 1 | 0 | 1 |
| 0x44AF | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x5743 | chaga mushroom | 1 | 1 | 0 | 1 |
| 0x0631 | stone roof | 1 | 1 | 0 | 1 |
| 0x44B0 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1265 | wall carving | 0 | 1 | 0 | 1 |
| 0x00E1 | stone arch | 3 | 0 | 1 | 1 |
| 0x2229 | crystal | 10 | 1 | 0 | 1 |
| 0x11B0 | floor saw | 5 | 1 | 0 | 1 |
| 0x4426 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x30EB | sinkhole | 0 | 1 | 0 | 1 |
| 0x4447 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1062 | upright loom | 8 | 1 | 0 | 1 |
| 0x2251 | demon skull | 1 | 1 | 0 | 1 |
| 0x4B72 | CityFlag NewMagencia | 0 | 1 | 0 | 1 |
| 0x8DBD | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x4498 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x121F | wall carving | 0 | 1 | 0 | 1 |
| 0x19AD | fire | 0 | 1 | 0 | 1 |
| 0x1C55 | coffin | 2 | 1 | 0 | 1 |
| 0x30F8 | sinkhole | 0 | 1 | 0 | 1 |
| 0x14B0 | Justice | 0 | 1 | 0 | 1 |
| 0x440A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1D1A | body | 4 | 1 | 0 | 1 |
| 0x09B5 | eggs | 1 | 1 | 0 | 1 |
| 0x0E14 | chessmen | 1 | 1 | 0 | 1 |
| 0x1659 | catapult | 3 | 1 | 0 | 1 |
| 0x4434 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0F4A | axe | 1 | 1 | 0 | 1 |
| 0x4403 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x147A | telescope | 10 | 1 | 0 | 1 |
| 0x365A |  | 15 | 0 | 1 | 1 |
| 0x1AB3 | lighthouse | 12 | 1 | 0 | 1 |
| 0x30F7 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1767 | cloth | 1 | 1 | 0 | 1 |
| 0x1B58 | treasure | 0 | 1 | 0 | 1 |
| 0x3526 | waterfall | 3 | 1 | 0 | 1 |
| 0x1C7D | sarcophagus lid | 2 | 1 | 0 | 1 |
| 0x4457 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E34 | scarecrow | 15 | 1 | 0 | 1 |
| 0x0FDC | tapestry | 0 | 1 | 0 | 1 |
| 0x43FD | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1679 | catapult | 3 | 1 | 0 | 1 |
| 0x1425 | Beeswax | 1 | 1 | 0 | 1 |
| 0x4411 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x09B0 | belt pouch | 1 | 1 | 0 | 1 |
| 0x222B | crystal | 10 | 1 | 0 | 1 |
| 0x41E0 | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x14A9 | Compassion | 0 | 1 | 0 | 1 |
| 0x56B9 | carpet | 1 | 1 | 0 | 1 |
| 0x4490 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1646 | dark source | 0 | 1 | 0 | 1 |
| 0x3E9F | ship | 3 | 1 | 0 | 1 |
| 0x1E46 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x1C57 | coffin | 2 | 1 | 0 | 1 |
| 0x2E34 | acid | 0 | 1 | 0 | 1 |
| 0x320D | swamp | 0 | 1 | 0 | 1 |
| 0x0C52 | cotton | 1 | 1 | 0 | 1 |
| 0x0EC2 | cleaver | 1 | 1 | 0 | 1 |
| 0x164C | catapult | 3 | 1 | 0 | 1 |
| 0x3B7B |  | 0 | 0 | 1 | 1 |
| 0x0F07 | Orange Potion | 1 | 1 | 0 | 1 |
| 0x14B6 | Justice | 0 | 1 | 0 | 1 |
| 0x43EE | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x43E9 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E69 | trophy | 0 | 1 | 0 | 1 |
| 0x8DA5 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x165C | catapult | 3 | 1 | 0 | 1 |
| 0x56F0 | carpet | 1 | 1 | 0 | 1 |
| 0x1375 | bridle | 1 | 1 | 0 | 1 |
| 0x1ACC | lighthouse | 12 | 1 | 0 | 1 |
| 0x1C6F | sarcophagus | 2 | 1 | 0 | 1 |
| 0x4B63 | CityFlag Moonglow SO | 0 | 1 | 0 | 1 |
| 0x172E | jester's cap | 1 | 1 | 0 | 1 |
| 0x223F | broken crystals | 1 | 1 | 0 | 1 |
| 0x052A | sandstone floor | 0 | 1 | 0 | 1 |
| 0x44B2 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3B15 |  | 1 | 0 | 1 | 1 |
| 0x1DB9 | leather cap | 1 | 1 | 0 | 1 |
| 0x4442 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4430 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1C52 | coffin | 2 | 1 | 0 | 1 |
| 0x14EA | moulding board | 1 | 1 | 0 | 1 |
| 0x56EE | carpet | 1 | 1 | 0 | 1 |
| 0x1712 | thigh boots | 1 | 1 | 0 | 1 |
| 0x44AA | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x44BE | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x30FB | sinkhole | 0 | 1 | 0 | 1 |
| 0x2FE6 | tall crystal | 22 | 1 | 0 | 1 |
| 0x04BF | wooden boards | 0 | 0 | 1 | 1 |
| 0x220C | crystal | 10 | 1 | 0 | 1 |
| 0x0E1A | checkers | 1 | 1 | 0 | 1 |
| 0x3102 | sinkhole | 0 | 1 | 0 | 1 |
| 0x1885 | millstone | 0 | 1 | 0 | 1 |
| 0x44A8 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E42 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x4C64 | Landscape S 02 SOUTH | 1 | 1 | 0 | 1 |
| 0x403F | gargoyle sculpture ( | 20 | 1 | 0 | 1 |
| 0x12B2 | scaffold | 15 | 1 | 0 | 1 |
| 0x41E1 | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x41E9 | Wallset1 FloorB East | 0 | 1 | 0 | 1 |
| 0x1478 | telescope | 10 | 1 | 0 | 1 |
| 0x1473 | telescope | 10 | 1 | 0 | 1 |
| 0x1828 | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x4455 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1AC1 | lighthouse | 12 | 1 | 0 | 1 |
| 0x1B31 | dirt | 0 | 1 | 0 | 1 |
| 0x4461 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1498 | telescope | 10 | 1 | 0 | 1 |
| 0x1E6B | trophy | 0 | 1 | 0 | 1 |
| 0x8DB6 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1CB6 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x43F2 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DB7 | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1C84 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x4459 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0E89 | quarter staff | 1 | 1 | 0 | 1 |
| 0x12A5 | tarot | 1 | 1 | 0 | 1 |
| 0x4479 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4472 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4427 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x442F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1E45 | bearskin rug | 0 | 1 | 0 | 1 |
| 0x1B60 | treasure | 0 | 1 | 0 | 1 |
| 0x2D27 | Radiant Scimitar | 1 | 1 | 0 | 1 |
| 0x44B3 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x257E | shortsword | 1 | 1 | 0 | 1 |
| 0x30FC | sinkhole | 0 | 1 | 0 | 1 |
| 0x1499 | telescope | 10 | 1 | 0 | 1 |
| 0x3107 | sinkhole | 0 | 1 | 0 | 1 |
| 0x0FC8 | shell | 1 | 1 | 0 | 1 |
| 0x4B64 | CityFlag Brittain SO | 0 | 1 | 0 | 1 |
| 0x4402 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4488 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1CAF | sarcophagus lid | 2 | 1 | 0 | 1 |
| 0x43FB | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x0FBC | tongs | 1 | 1 | 0 | 1 |
| 0x444E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x11A0 | floor spikes | 0 | 1 | 0 | 1 |
| 0x3EAB | ship | 3 | 1 | 0 | 1 |
| 0x1CBF | sarcophagus | 2 | 1 | 0 | 1 |
| 0x234B | rose of trinsic | 1 | 1 | 0 | 1 |
| 0x1492 | telescope | 10 | 1 | 0 | 1 |
| 0x1C73 | sarcophagus | 2 | 1 | 0 | 1 |
| 0x4415 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x441D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1AAA | lighthouse | 12 | 1 | 0 | 1 |
| 0x4471 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x04CC | wooden planks | 0 | 0 | 1 | 1 |
| 0x1510 | Sacrifice | 0 | 1 | 0 | 1 |
| 0x1EFD | fancy shirt | 1 | 1 | 0 | 1 |
| 0x30EF | sinkhole | 0 | 1 | 0 | 1 |
| 0x1C4F | coffin | 2 | 1 | 0 | 1 |
| 0x5744 | silver snake skin | 1 | 1 | 0 | 1 |
| 0x1BE4 | copper ingots | 1 | 1 | 0 | 1 |
| 0x1378 | horse barding | 0 | 1 | 0 | 1 |
| 0x245F | sand | 0 | 1 | 0 | 1 |
| 0x445D | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1C43 | coffin | 2 | 1 | 0 | 1 |
| 0x443F | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DAA | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x310A | sinkhole | 0 | 1 | 0 | 1 |
| 0x1480 | telescope | 10 | 1 | 0 | 1 |
| 0x4443 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x30F1 | sinkhole | 0 | 1 | 0 | 1 |
| 0x4460 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x329D | lava | 0 | 1 | 0 | 1 |
| 0x0BB0 | Woodworker | 0 | 1 | 0 | 1 |
| 0x1543 | skullcap | 1 | 1 | 0 | 1 |
| 0x4404 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x181F | alchemical symbol | 0 | 1 | 0 | 1 |
| 0x0E79 | pouch | 1 | 1 | 0 | 1 |
| 0x13EE | ringmail sleeves | 1 | 1 | 0 | 1 |
| 0x443E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DAC | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x1EE5 | magical door | 0 | 1 | 0 | 1 |
| 0x04CD | wooden planks | 0 | 0 | 1 | 1 |
| 0x4438 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x9A96 | Machine Coffee | 3 | 1 | 0 | 1 |
| 0x1B6F | wall cracks | 0 | 1 | 0 | 1 |
| 0x1BDA | board%s | 1 | 1 | 0 | 1 |
| 0x1AC4 | lighthouse | 12 | 1 | 0 | 1 |
| 0x0C35 | sheets | 3 | 1 | 0 | 1 |
| 0x1491 | telescope | 10 | 1 | 0 | 1 |
| 0x1986 | bellows | 3 | 1 | 0 | 1 |
| 0x1AA8 | lighthouse | 12 | 1 | 0 | 1 |
| 0x14AB | Compassion | 0 | 1 | 0 | 1 |
| 0x44BD | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4406 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x8DBA | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x145E | telescope | 10 | 1 | 0 | 1 |
| 0x0FE3 | tapestry | 0 | 1 | 0 | 1 |
| 0x2DDA | elven spinning whee | 5 | 1 | 0 | 1 |
| 0x41DE | Wallset1 FloorC East | 0 | 1 | 0 | 1 |
| 0x1475 | telescope | 10 | 1 | 0 | 1 |
| 0x1D18 | body | 4 | 1 | 0 | 1 |
| 0x441E | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1D53 | skeleton | 4 | 1 | 0 | 1 |
| 0x41EE | Wallset1 FloorB East | 0 | 1 | 0 | 1 |
| 0x56ED | carpet | 1 | 1 | 0 | 1 |
| 0x8DAB | ShipSails D2 NORTH | 0 | 1 | 0 | 1 |
| 0x445A | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x4452 | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x1AA9 | lighthouse | 12 | 1 | 0 | 1 |
| 0x09E1 | pot | 1 | 1 | 0 | 1 |
| 0x1889 | millstones | 0 | 1 | 0 | 1 |
| 0x1511 | Sacrifice | 0 | 1 | 0 | 1 |
| 0x1AA5 | lighthouse | 12 | 1 | 0 | 1 |
| 0x0DCE | fishing weight | 0 | 1 | 0 | 1 |
| 0x1CBA | sarcophagus | 2 | 1 | 0 | 1 |
| 0x43EB | Virtue Floor East | 0 | 1 | 0 | 1 |
| 0x3D66 | corpse of skeleton | 0 | 1 | 0 | 1 |
| 0x150D | Sacrifice | 0 | 1 | 0 | 1 |
| 0x07F6 | suspension bridge | 20 | 1 | 0 | 1 |
| 0x14B2 | Justice | 0 | 1 | 0 | 1 |
| 0x5737 | elven fletching | 1 | 1 | 0 | 1 |

### 2.2 Felucca (facet 0): top 150 of 2915 graphics

Full list: `WorldData/0/census-0.csv`.

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x0CA7 | rushes | 0 | 8,110 | 0 | 8,110 |
| 0x21A3 | nodraw | 100 | 0 | 5,266 | 5,266 |
| 0x053B | cave floor | 0 | 4,374 | 0 | 4,374 |
| 0x053D | cave floor | 0 | 4,331 | 0 | 4,331 |
| 0x0D16 | mushroom | 1 | 4,326 | 0 | 4,326 |
| 0x0518 | cobblestones | 0 | 4,264 | 0 | 4,264 |
| 0x051D | stone pavers | 0 | 4,073 | 0 | 4,073 |
| 0x0515 | cobblestones | 0 | 3,996 | 0 | 3,996 |
| 0x053F | cave floor | 0 | 3,762 | 0 | 3,762 |
| 0x0D18 | mushroom | 1 | 3,712 | 0 | 3,712 |
| 0x053C | cave floor | 0 | 3,301 | 0 | 3,301 |
| 0x076C | sandstone | 10 | 3,239 | 0 | 3,239 |
| 0x08E1 | stalagmites | 10 | 3,052 | 0 | 3,052 |
| 0x053E | cave floor | 0 | 3,003 | 0 | 3,003 |
| 0x0524 | stone pavers Dark | 0 | 2,993 | 0 | 2,993 |
| 0x0C93 | blade plant | 2 | 2,651 | 0 | 2,651 |
| 0x051E | stone pavers | 0 | 2,541 | 0 | 2,541 |
| 0x0C98 | fan plant | 1 | 2,498 | 0 | 2,498 |
| 0x0709 | marble | 10 | 2,375 | 0 | 2,375 |
| 0x08E4 | stalagmites | 10 | 2,129 | 0 | 2,129 |
| 0x0D80 | vines | 0 | 2,041 | 0 | 2,041 |
| 0x08E0 | stalagmites | 10 | 2,005 | 0 | 2,005 |
| 0x04C1 | wooden logs | 0 | 1,977 | 0 | 1,977 |
| 0x0D82 | vines | 10 | 1,922 | 0 | 1,922 |
| 0x0D92 | vines | 0 | 1,914 | 0 | 1,914 |
| 0x0D93 | vines | 0 | 1,913 | 0 | 1,913 |
| 0x0D81 | vines | 10 | 1,913 | 0 | 1,913 |
| 0x0D91 | vines | 0 | 1,911 | 0 | 1,911 |
| 0x0517 | cobblestones | 0 | 1,906 | 0 | 1,906 |
| 0x0D6A | vines | 0 | 1,898 | 0 | 1,898 |
| 0x0D6D | vines | 0 | 1,894 | 0 | 1,894 |
| 0x0D6B | vines | 20 | 1,892 | 0 | 1,892 |
| 0x0D6C | vines | 20 | 1,887 | 0 | 1,887 |
| 0x0516 | cobblestones | 0 | 1,877 | 0 | 1,877 |
| 0x0D55 | vines | 20 | 1,834 | 0 | 1,834 |
| 0x0D56 | vines | 35 | 1,831 | 0 | 1,831 |
| 0x0520 | stone pavers | 0 | 1,775 | 0 | 1,775 |
| 0x0CA9 | snake plant | 3 | 1,761 | 0 | 1,761 |
| 0x0D34 | morning glories | 0 | 1,745 | 0 | 1,745 |
| 0x0519 | stone pavers Dark | 0 | 1,673 | 0 | 1,673 |
| 0x08E9 | stalagmites | 10 | 1,654 | 0 | 1,654 |
| 0x08E5 | stalagmites | 10 | 1,608 | 0 | 1,608 |
| 0x3609 |  | 10 | 0 | 1,607 | 1,607 |
| 0x07CA | wooden bridge | 1 | 1,513 | 0 | 1,513 |
| 0x08EA | stalagmites | 10 | 1,414 | 0 | 1,414 |
| 0x051F | stone pavers | 0 | 1,413 | 0 | 1,413 |
| 0x08E3 | flowstone | 10 | 1,370 | 0 | 1,370 |
| 0x0523 | stone pavers Dark | 0 | 1,368 | 0 | 1,368 |
| 0x08E6 | flowstone | 10 | 1,345 | 0 | 1,345 |
| 0x0521 | stone pavers Dark | 0 | 1,329 | 0 | 1,329 |
| 0x07CB | wooden bridge | 1 | 1,321 | 0 | 1,321 |
| 0x050D | marble floor | 0 | 1,142 | 0 | 1,142 |
| 0x07A3 | stone | 10 | 0 | 1,109 | 1,109 |
| 0x32AB | lava | 0 | 1,084 | 0 | 1,084 |
| 0x32B1 | lava | 0 | 1,038 | 0 | 1,038 |
| 0x32AD | lava | 0 | 960 | 0 | 960 |
| 0x32AF | lava | 0 | 953 | 0 | 953 |
| 0x08E2 | flowstone | 10 | 926 | 0 | 926 |
| 0x08E8 | flowstone | 16 | 902 | 0 | 902 |
| 0x2199 | nodraw | 3 | 0 | 891 | 891 |
| 0x04A0 | sandstone paver | 0 | 848 | 0 | 848 |
| 0x324F | sunken log | 0 | 811 | 0 | 811 |
| 0x0AC8 | carpet | 0 | 738 | 0 | 738 |
| 0x0750 | stone | 10 | 0 | 736 | 736 |
| 0x08E7 | stalagmites | 10 | 719 | 0 | 719 |
| 0xAE46 | Tracks Straight Sout | 1 | 710 | 0 | 710 |
| 0x0522 | stone pavers Dark | 0 | 678 | 0 | 678 |
| 0x055E | stone roof | 0 | 660 | 0 | 660 |
| 0x071E | stone | 10 | 0 | 659 | 659 |
| 0x050E | white marble | 0 | 655 | 0 | 655 |
| 0x055F | stone roof | 0 | 590 | 0 | 590 |
| 0x0573 | stone roof | 0 | 577 | 0 | 577 |
| 0x055C | stone roof | 1 | 574 | 0 | 574 |
| 0x0560 | stone roof | 0 | 559 | 0 | 559 |
| 0x0569 | stone roof | 0 | 554 | 0 | 554 |
| 0x0561 | stone roof | 0 | 553 | 0 | 553 |
| 0x049F | sandstone paver | 0 | 551 | 0 | 551 |
| 0x0540 | cave floor | 0 | 548 | 0 | 548 |
| 0x0577 | stone roof | 0 | 547 | 0 | 547 |
| 0x10EE | garbage | 0 | 540 | 0 | 540 |
| 0x0564 | stone roof | 0 | 508 | 0 | 508 |
| 0x0DC1 | water lilly | 0 | 505 | 0 | 505 |
| 0x17CD | snow | 0 | 503 | 0 | 503 |
| 0x0DC2 | water lilly | 0 | 490 | 0 | 490 |
| 0x0DBD | lilly pad | 0 | 489 | 0 | 489 |
| 0x324E | sunken log | 0 | 482 | 0 | 482 |
| 0x0576 | stone roof | 0 | 481 | 0 | 481 |
| 0x0DBC | lilly pad | 0 | 469 | 0 | 469 |
| 0x0DBE | lilly pads | 0 | 466 | 0 | 466 |
| 0x3250 | sunken log | 0 | 466 | 0 | 466 |
| 0x0DC3 | water lilly | 0 | 463 | 0 | 463 |
| 0x0EE3 | spiderweb | 0 | 460 | 0 | 460 |
| 0x0575 | stone roof | 0 | 459 | 0 | 459 |
| 0x0738 | wood | 10 | 0 | 445 | 445 |
| 0x049C | grey slate tile | 0 | 443 | 0 | 443 |
| 0x057A | stone roof | 0 | 442 | 0 | 442 |
| 0x0578 | stone roof | 0 | 441 | 0 | 441 |
| 0x055D | stone roof | 0 | 440 | 0 | 440 |
| 0x057B | stone roof | 0 | 432 | 0 | 432 |
| 0x0547 | cave floor | 0 | 430 | 0 | 430 |
| 0xAE47 | Tracks Straight East | 1 | 410 | 0 | 410 |
| 0x187E | spilled flour | 1 | 402 | 0 | 402 |
| 0x0548 | cave floor | 0 | 402 | 0 | 402 |
| 0x0574 | stone roof | 0 | 396 | 0 | 396 |
| 0x0549 | cave floor | 0 | 387 | 0 | 387 |
| 0x0EE6 | spiderweb | 0 | 385 | 0 | 385 |
| 0x057E | stone roof | 0 | 384 | 0 | 384 |
| 0x04F2 | blue tiles | 0 | 384 | 0 | 384 |
| 0x0AD1 | carpet | 0 | 377 | 0 | 377 |
| 0x0567 | stone roof | 0 | 377 | 0 | 377 |
| 0x049E | sandstone paver | 0 | 372 | 0 | 372 |
| 0x0ABE | carpet | 0 | 372 | 0 | 372 |
| 0x32A4 | lava | 0 | 367 | 0 | 367 |
| 0xAE31 | Wall Mine Beams 01 E | 1 | 360 | 0 | 360 |
| 0x0EE4 | spiderweb | 0 | 357 | 0 | 357 |
| 0x32A3 | lava | 0 | 356 | 0 | 356 |
| 0x0639 | stone roof | 1 | 347 | 0 | 347 |
| 0x0563 | stone roof | 0 | 346 | 0 | 346 |
| 0x0EE5 | spiderweb | 0 | 345 | 0 | 345 |
| 0x32A5 | lava | 0 | 343 | 0 | 343 |
| 0xAE34 | Wall Mine Beams 04 E | 1 | 342 | 0 | 342 |
| 0x04C5 | wooden logs | 0 | 333 | 0 | 333 |
| 0x2212 | crystal | 10 | 330 | 0 | 330 |
| 0x0570 | stone roof | 0 | 325 | 0 | 325 |
| 0x056B | stone roof | 0 | 325 | 0 | 325 |
| 0x0637 | stone roof | 1 | 320 | 0 | 320 |
| 0x32AE | lava | 0 | 319 | 0 | 319 |
| 0x063A | stone roof | 1 | 318 | 0 | 318 |
| 0x32A2 | lava | 0 | 317 | 0 | 317 |
| 0x056D | stone roof | 0 | 317 | 0 | 317 |
| 0xAE32 | Wall Mine Beams 02 E | 1 | 316 | 0 | 316 |
| 0x2198 | nodraw | 0 | 0 | 316 | 316 |
| 0x221A | crystal | 10 | 304 | 0 | 304 |
| 0xAE33 | Wall Mine Beams 03 E | 1 | 303 | 0 | 303 |
| 0x0571 | stone roof | 0 | 302 | 0 | 302 |
| 0xAE2D | Wall Mine Beams 03 S | 1 | 298 | 0 | 298 |
| 0xAE2B | Wall Mine Beams 01 S | 1 | 294 | 0 | 294 |
| 0xAE2C | Wall Mine Beams 02 S | 1 | 290 | 0 | 290 |
| 0x0508 | wooden logs | 0 | 288 | 0 | 288 |
| 0xAE2E | Wall Mine Beams 04 S | 1 | 287 | 0 | 287 |
| 0x2985 | wooden floor | 0 | 279 | 0 | 279 |
| 0x21BC | nodraw | 0 | 0 | 275 | 275 |
| 0x051C | stone pavers | 0 | 275 | 0 | 275 |
| 0x07BE | platform | 2 | 274 | 0 | 274 |
| 0x0C2D | debris | 1 | 272 | 0 | 272 |
| 0x051B | stone pavers | 0 | 270 | 0 | 270 |
| 0x04C3 | wooden logs | 0 | 270 | 0 | 270 |
| 0x328F | lava | 1 | 267 | 0 | 267 |
| 0x063D | marble roof | 0 | 263 | 0 | 263 |
| 0x0541 | cave floor | 0 | 262 | 0 | 262 |

### 2.3 Ilshenar (facet 2): top 150 of 1878 graphics

Full list: `WorldData/2/census-2.csv`.

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x21A3 | nodraw | 100 | 0 | 2,630 | 2,630 |
| 0x051E | stone pavers | 0 | 2,187 | 0 | 2,187 |
| 0x049C | grey slate tile | 0 | 2,024 | 0 | 2,024 |
| 0x051D | stone pavers | 0 | 1,808 | 0 | 1,808 |
| 0x0511 | marble floor | 0 | 1,491 | 0 | 1,491 |
| 0x0709 | marble | 10 | 1,410 | 0 | 1,410 |
| 0x051F | stone pavers | 0 | 1,341 | 0 | 1,341 |
| 0x0521 | stone pavers Dark | 0 | 1,244 | 0 | 1,244 |
| 0x0788 | stone | 10 | 0 | 1,110 | 1,110 |
| 0x07CA | wooden bridge | 1 | 1,102 | 0 | 1,102 |
| 0x0523 | stone pavers Dark | 0 | 1,053 | 0 | 1,053 |
| 0x07CB | wooden bridge | 1 | 1,051 | 0 | 1,051 |
| 0x0506 | wooden logs | 0 | 1,045 | 0 | 1,045 |
| 0x0504 | wooden logs | 0 | 1,040 | 0 | 1,040 |
| 0x0522 | stone pavers Dark | 0 | 1,026 | 0 | 1,026 |
| 0x21A4 | no draw | 4 | 0 | 1,014 | 1,014 |
| 0x0524 | stone pavers Dark | 0 | 1,003 | 0 | 1,003 |
| 0x0D18 | mushroom | 1 | 915 | 0 | 915 |
| 0x1CD9 | blood | 0 | 878 | 0 | 878 |
| 0x0C93 | blade plant | 2 | 821 | 0 | 821 |
| 0x071E | stone | 10 | 0 | 790 | 790 |
| 0x0C98 | fan plant | 1 | 753 | 0 | 753 |
| 0x0750 | stone | 10 | 0 | 696 | 696 |
| 0x0520 | stone pavers | 0 | 650 | 0 | 650 |
| 0x050D | marble floor | 0 | 578 | 0 | 578 |
| 0x0CA7 | rushes | 0 | 572 | 0 | 572 |
| 0x04C1 | wooden logs | 0 | 557 | 0 | 557 |
| 0x21BC | nodraw | 0 | 0 | 555 | 555 |
| 0x04FF | flagstones | 0 | 552 | 0 | 552 |
| 0x0CA9 | snake plant | 3 | 530 | 0 | 530 |
| 0x0519 | stone pavers Dark | 0 | 526 | 0 | 526 |
| 0x0D16 | mushroom | 1 | 513 | 0 | 513 |
| 0x0DAF | wheat | 0 | 484 | 0 | 484 |
| 0x10EE | garbage | 0 | 411 | 0 | 411 |
| 0x0D56 | vines | 35 | 409 | 0 | 409 |
| 0x0D55 | vines | 20 | 409 | 0 | 409 |
| 0x0DC3 | water lilly | 0 | 403 | 0 | 403 |
| 0x0D80 | vines | 0 | 398 | 0 | 398 |
| 0x0D81 | vines | 10 | 396 | 0 | 396 |
| 0x0D92 | vines | 0 | 394 | 0 | 394 |
| 0x0D91 | vines | 0 | 394 | 0 | 394 |
| 0x0D6A | vines | 0 | 394 | 0 | 394 |
| 0x0D93 | vines | 0 | 393 | 0 | 393 |
| 0x0DC1 | water lilly | 0 | 391 | 0 | 391 |
| 0x0D82 | vines | 10 | 391 | 0 | 391 |
| 0x0D6D | vines | 0 | 390 | 0 | 390 |
| 0x0D6B | vines | 20 | 390 | 0 | 390 |
| 0x0D6C | vines | 20 | 388 | 0 | 388 |
| 0x0509 | wooden logs | 0 | 386 | 0 | 386 |
| 0x070B | marble stairs | 5 | 382 | 0 | 382 |
| 0x070A | marble stairs | 5 | 382 | 0 | 382 |
| 0x0DC2 | water lilly | 0 | 381 | 0 | 381 |
| 0x070D | marble stairs | 5 | 378 | 0 | 378 |
| 0x070C | marble stairs | 5 | 374 | 0 | 374 |
| 0x0DBE | lilly pads | 0 | 374 | 0 | 374 |
| 0x0DBD | lilly pad | 0 | 347 | 0 | 347 |
| 0x0DBC | lilly pad | 0 | 338 | 0 | 338 |
| 0x0D34 | morning glories | 0 | 337 | 0 | 337 |
| 0x10EF | garbage | 0 | 333 | 0 | 333 |
| 0x050E | white marble | 0 | 333 | 0 | 333 |
| 0x0CF2 | vines | 0 | 318 | 0 | 318 |
| 0x1B11 | bone | 1 | 314 | 0 | 314 |
| 0x1B12 | bone | 1 | 311 | 0 | 311 |
| 0x063B | marble roof | 0 | 295 | 0 | 295 |
| 0x19AB | fire | 0 | 295 | 0 | 295 |
| 0x10F1 | garbage | 0 | 288 | 0 | 288 |
| 0x076C | sandstone | 10 | 287 | 0 | 287 |
| 0x1B17 | rib cage | 1 | 273 | 0 | 273 |
| 0x1B09 | bone pile | 1 | 263 | 0 | 263 |
| 0x050F | marble floor | 0 | 263 | 0 | 263 |
| 0x324F | sunken log | 0 | 260 | 0 | 260 |
| 0x1B1B | spine | 1 | 260 | 0 | 260 |
| 0x1AE2 | skull | 1 | 251 | 0 | 251 |
| 0x04FC | flagstones | 0 | 248 | 0 | 248 |
| 0x0505 | wooden logs | 0 | 239 | 0 | 239 |
| 0x1AE3 | skull | 1 | 225 | 0 | 225 |
| 0x1B18 | rib cage | 1 | 224 | 0 | 224 |
| 0x3291 | lava | 1 | 224 | 0 | 224 |
| 0x04C3 | wooden logs | 0 | 223 | 0 | 223 |
| 0x3288 | lava | 1 | 223 | 0 | 223 |
| 0x3289 | lava | 1 | 215 | 0 | 215 |
| 0x328A | lava | 1 | 214 | 0 | 214 |
| 0x328B | lava | 1 | 213 | 0 | 213 |
| 0x050A | wooden logs | 0 | 209 | 0 | 209 |
| 0x328F | lava | 1 | 205 | 0 | 205 |
| 0x3293 | lava | 1 | 199 | 0 | 199 |
| 0x0738 | wood | 10 | 0 | 194 | 194 |
| 0x08E3 | flowstone | 10 | 193 | 0 | 193 |
| 0x1B13 | jaw bone | 1 | 189 | 0 | 189 |
| 0x0E31 | brazier | 0 | 184 | 0 | 184 |
| 0x3292 | lava | 1 | 174 | 0 | 174 |
| 0x0CEE | vines | 0 | 170 | 0 | 170 |
| 0x329B | lava | 1 | 166 | 0 | 166 |
| 0x1B19 | bone shards | 1 | 166 | 0 | 166 |
| 0x329A | lava | 1 | 164 | 0 | 164 |
| 0x3290 | lava | 1 | 159 | 0 | 159 |
| 0x08E6 | flowstone | 10 | 158 | 0 | 158 |
| 0x3286 | lava | 1 | 151 | 0 | 151 |
| 0x053D | cave floor | 0 | 149 | 0 | 149 |
| 0x3250 | sunken log | 0 | 147 | 0 | 147 |
| 0x0579 | stone roof | 0 | 145 | 0 | 145 |
| 0x324E | sunken log | 0 | 143 | 0 | 143 |
| 0x3295 | lava | 1 | 142 | 0 | 142 |
| 0x0512 | marble floor | 0 | 140 | 0 | 140 |
| 0x1184 | gravestone | 6 | 140 | 0 | 140 |
| 0x328D | lava | 1 | 139 | 0 | 139 |
| 0x1B0A | bone pile | 1 | 138 | 0 | 138 |
| 0x122B | blood | 0 | 136 | 0 | 136 |
| 0x1C63 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x1C60 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x1C62 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x053F | cave floor | 0 | 132 | 0 | 132 |
| 0x1C65 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x1C61 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x1C64 | sarcophagus | 2 | 132 | 0 | 132 |
| 0x3294 | lava | 1 | 130 | 0 | 130 |
| 0x051B | stone pavers | 0 | 129 | 0 | 129 |
| 0x0508 | wooden logs | 0 | 128 | 0 | 128 |
| 0x11E7 | woven mat | 1 | 125 | 0 | 125 |
| 0x0577 | stone roof | 0 | 124 | 0 | 124 |
| 0x04C4 | wooden logs | 0 | 123 | 0 | 123 |
| 0x1B1A | bone shards | 1 | 122 | 0 | 122 |
| 0x0581 | slate roof | 0 | 122 | 0 | 122 |
| 0x0E62 | glowing rune | 0 | 121 | 0 | 121 |
| 0x0574 | stone roof | 0 | 120 | 0 | 120 |
| 0x049B | floor tile | 0 | 117 | 0 | 117 |
| 0x32AC | lava | 0 | 114 | 0 | 114 |
| 0x1B0F | bone pile | 1 | 111 | 0 | 111 |
| 0x32B0 | lava | 0 | 109 | 0 | 109 |
| 0x328C | lava | 1 | 109 | 0 | 109 |
| 0x122C | blood | 0 | 108 | 0 | 108 |
| 0x10F0 | garbage | 0 | 104 | 0 | 104 |
| 0x08E2 | flowstone | 10 | 104 | 0 | 104 |
| 0x122D | blood | 0 | 103 | 0 | 103 |
| 0x122E | blood | 0 | 103 | 0 | 103 |
| 0x3241 | swamp | 0 | 101 | 0 | 101 |
| 0x32A4 | lava | 0 | 101 | 0 | 101 |
| 0x19BB | brazier | 1 | 101 | 0 | 101 |
| 0x1AE0 | skull | 1 | 101 | 0 | 101 |
| 0x1B0D | bone pile | 1 | 100 | 0 | 100 |
| 0x0514 | marble floor | 0 | 100 | 0 | 100 |
| 0x1ADB | pile of skulls | 1 | 100 | 0 | 100 |
| 0x32AA | lava | 0 | 99 | 0 | 99 |
| 0x3523 | waterfall | 3 | 98 | 0 | 98 |
| 0x0E5C | glowing rune | 0 | 98 | 0 | 98 |
| 0x3213 | swamp | 0 | 98 | 0 | 98 |
| 0x1B0C | bone pile | 1 | 98 | 0 | 98 |
| 0x32AE | lava | 0 | 97 | 0 | 97 |
| 0x1B0B | bone pile | 1 | 97 | 0 | 97 |
| 0x1B16 | pelvis bone | 1 | 96 | 0 | 96 |

### 2.4 Malas (facet 3): top 150 of 1524 graphics

Full list: `WorldData/3/census-3.csv`.

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x053C | cave floor | 0 | 5,568 | 0 | 5,568 |
| 0x32A5 | lava | 0 | 4,865 | 0 | 4,865 |
| 0x32A3 | lava | 0 | 4,859 | 0 | 4,859 |
| 0x32A6 | lava | 0 | 4,848 | 0 | 4,848 |
| 0x32A9 | lava | 0 | 4,776 | 0 | 4,776 |
| 0x32AD | lava | 0 | 3,911 | 0 | 3,911 |
| 0x32AE | lava | 0 | 3,880 | 0 | 3,880 |
| 0x223B | broken crystals | 1 | 3,860 | 0 | 3,860 |
| 0x32AF | lava | 0 | 3,823 | 0 | 3,823 |
| 0x053F | cave floor | 0 | 3,799 | 0 | 3,799 |
| 0x32AC | lava | 0 | 3,787 | 0 | 3,787 |
| 0x051D | stone pavers | 0 | 3,618 | 0 | 3,618 |
| 0x32A2 | lava | 0 | 3,323 | 0 | 3,323 |
| 0x32A8 | lava | 0 | 3,321 | 0 | 3,321 |
| 0x32A4 | lava | 0 | 3,319 | 0 | 3,319 |
| 0x32A7 | lava | 0 | 3,313 | 0 | 3,313 |
| 0x223E | broken crystals | 1 | 3,304 | 0 | 3,304 |
| 0x053D | cave floor | 0 | 3,000 | 0 | 3,000 |
| 0x053E | cave floor | 0 | 2,838 | 0 | 2,838 |
| 0x2240 | broken crystals | 1 | 2,825 | 0 | 2,825 |
| 0x32AB | lava | 0 | 2,724 | 0 | 2,724 |
| 0x32B1 | lava | 0 | 2,682 | 0 | 2,682 |
| 0x223C | broken crystals | 1 | 2,681 | 0 | 2,681 |
| 0x32B0 | lava | 0 | 2,650 | 0 | 2,650 |
| 0x32AA | lava | 0 | 2,598 | 0 | 2,598 |
| 0x223D | broken crystals | 1 | 2,028 | 0 | 2,028 |
| 0x223A | broken crystals | 1 | 2,004 | 0 | 2,004 |
| 0x0D18 | mushroom | 1 | 1,908 | 0 | 1,908 |
| 0x076C | sandstone | 10 | 1,870 | 0 | 1,870 |
| 0x0D16 | mushroom | 1 | 1,837 | 0 | 1,837 |
| 0x0524 | stone pavers Dark | 0 | 1,746 | 0 | 1,746 |
| 0x223F | broken crystals | 1 | 1,451 | 0 | 1,451 |
| 0x2241 | broken crystals | 1 | 1,430 | 0 | 1,430 |
| 0x2245 | broken crystals | 1 | 1,429 | 0 | 1,429 |
| 0x0527 | sandstone floor | 0 | 1,247 | 0 | 1,247 |
| 0x053B | cave floor | 0 | 1,190 | 0 | 1,190 |
| 0x2198 | nodraw | 0 | 0 | 1,186 | 1,186 |
| 0x0C92 | untrimmed hedge | 6 | 1,165 | 0 | 1,165 |
| 0x049D | sandstone paver | 0 | 1,037 | 0 | 1,037 |
| 0x2243 | broken crystals | 1 | 1,018 | 0 | 1,018 |
| 0x050D | marble floor | 0 | 1,009 | 0 | 1,009 |
| 0x2242 | broken crystals | 1 | 1,000 | 0 | 1,000 |
| 0x0539 | bricks | 0 | 840 | 0 | 840 |
| 0x052B | sandstone floor | 0 | 837 | 0 | 837 |
| 0x2244 | broken crystals | 1 | 799 | 0 | 799 |
| 0x2246 | broken crystals | 1 | 772 | 0 | 772 |
| 0x1ADB | pile of skulls | 1 | 702 | 0 | 702 |
| 0x0AC8 | carpet | 0 | 692 | 0 | 692 |
| 0x0500 | flagstones | 0 | 655 | 0 | 655 |
| 0x0750 | stone | 10 | 0 | 642 | 642 |
| 0x0709 | marble | 10 | 636 | 0 | 636 |
| 0x04E2 | bricks | 0 | 632 | 0 | 632 |
| 0x04E6 | bricks | 0 | 544 | 0 | 544 |
| 0x049B | floor tile | 0 | 522 | 0 | 522 |
| 0x050F | marble floor | 0 | 521 | 0 | 521 |
| 0x053A | bricks | 0 | 498 | 0 | 498 |
| 0x2248 | broken crystals | 1 | 482 | 0 | 482 |
| 0x0C91 | untrimmed hedge | 6 | 458 | 0 | 458 |
| 0x0501 | flagstones | 0 | 424 | 0 | 424 |
| 0x0511 | marble floor | 0 | 377 | 0 | 377 |
| 0x1AD9 | pile of skulls | 1 | 363 | 0 | 363 |
| 0x050E | white marble | 0 | 322 | 0 | 322 |
| 0x1ADC | pile of skulls | 1 | 302 | 0 | 302 |
| 0x2249 | broken crystals | 1 | 294 | 0 | 294 |
| 0x2247 | broken crystals | 1 | 294 | 0 | 294 |
| 0x071E | stone | 10 | 0 | 290 | 290 |
| 0x0544 | cave floor | 0 | 276 | 0 | 276 |
| 0x0537 | bricks | 0 | 259 | 0 | 259 |
| 0x0549 | cave floor | 0 | 258 | 0 | 258 |
| 0x049E | sandstone paver | 0 | 256 | 0 | 256 |
| 0x0547 | cave floor | 0 | 254 | 0 | 254 |
| 0x0545 | cave floor | 0 | 249 | 0 | 249 |
| 0x21A4 | no draw | 4 | 0 | 248 | 248 |
| 0x0502 | flagstones | 0 | 239 | 0 | 239 |
| 0x1ADF | pile of skulls | 1 | 239 | 0 | 239 |
| 0x0540 | cave floor | 0 | 239 | 0 | 239 |
| 0x054B | cave floor | 0 | 236 | 0 | 236 |
| 0x054D | cave floor | 0 | 226 | 0 | 226 |
| 0x054E | cave floor | 0 | 216 | 0 | 216 |
| 0x054C | cave floor | 0 | 211 | 0 | 211 |
| 0x0880 | wooden rail | 5 | 211 | 0 | 211 |
| 0x0512 | marble floor | 0 | 208 | 0 | 208 |
| 0x0548 | cave floor | 0 | 205 | 0 | 205 |
| 0x0503 | flagstones | 0 | 200 | 0 | 200 |
| 0x0881 | wooden rail | 5 | 198 | 0 | 198 |
| 0x0514 | marble floor | 0 | 197 | 0 | 197 |
| 0x0581 | slate roof | 0 | 196 | 0 | 196 |
| 0x0513 | marble floor | 0 | 189 | 0 | 189 |
| 0x0D3B | fallen branch | 0 | 187 | 0 | 187 |
| 0x054A | cave floor | 0 | 186 | 0 | 186 |
| 0x0ABE | carpet | 0 | 181 | 0 | 181 |
| 0x0542 | cave floor | 0 | 176 | 0 | 176 |
| 0x0541 | cave floor | 0 | 170 | 0 | 170 |
| 0x1AD8 | pile of skulls | 1 | 166 | 0 | 166 |
| 0x0886 | wooden rail | 5 | 162 | 0 | 162 |
| 0x0887 | wooden rail | 5 | 158 | 0 | 158 |
| 0x0C98 | fan plant | 1 | 149 | 0 | 149 |
| 0x0CA7 | rushes | 0 | 145 | 0 | 145 |
| 0x0D3C | fallen branch | 0 | 144 | 0 | 144 |
| 0x1645 | blood | 0 | 138 | 0 | 138 |
| 0x0AB3 | rug | 0 | 132 | 0 | 132 |
| 0x04E3 | bricks | 0 | 127 | 0 | 127 |
| 0x0914 | dirt patch | 0 | 114 | 0 | 114 |
| 0x0525 | sandstone floor | 0 | 111 | 0 | 111 |
| 0x24BD | paper lantern | 2 | 110 | 0 | 110 |
| 0x1ADA | pile of skulls | 1 | 109 | 0 | 109 |
| 0x272E | wood floor | 0 | 108 | 0 | 108 |
| 0x1AE4 | skull | 1 | 107 | 0 | 107 |
| 0x31C0 |  | 0 | 0 | 102 | 102 |
| 0x0582 | slate roof | 0 | 102 | 0 | 102 |
| 0x19AB | fire | 0 | 95 | 0 | 95 |
| 0x244A | wood floor | 0 | 95 | 0 | 95 |
| 0x2448 | wood floor | 0 | 94 | 0 | 94 |
| 0x08E1 | stalagmites | 10 | 93 | 0 | 93 |
| 0x0AD0 | carpet | 0 | 92 | 0 | 92 |
| 0x04E5 | bricks | 0 | 90 | 0 | 90 |
| 0x0ACE | carpet | 0 | 89 | 0 | 89 |
| 0x31B6 |  | 0 | 0 | 88 | 88 |
| 0x1B0F | bone pile | 1 | 85 | 0 | 85 |
| 0x04E4 | bricks | 0 | 81 | 0 | 81 |
| 0x0519 | stone pavers Dark | 0 | 80 | 0 | 80 |
| 0x2224 | crystal | 10 | 80 | 0 | 80 |
| 0x221C | crystal | 10 | 80 | 0 | 80 |
| 0x0551 | cave floor | 0 | 80 | 0 | 80 |
| 0x1ADD | pile of skulls | 1 | 77 | 0 | 77 |
| 0x1B0A | bone pile | 1 | 77 | 0 | 77 |
| 0x31B7 |  | 0 | 0 | 75 | 75 |
| 0x070A | marble stairs | 5 | 75 | 0 | 75 |
| 0x04E8 | bricks | 0 | 74 | 0 | 74 |
| 0x2228 | crystal | 10 | 73 | 0 | 73 |
| 0x0585 | slate roof | 0 | 73 | 0 | 73 |
| 0x1B12 | bone | 1 | 72 | 0 | 72 |
| 0x2210 | crystal | 10 | 72 | 0 | 72 |
| 0x070C | marble stairs | 5 | 72 | 0 | 72 |
| 0x08E5 | stalagmites | 10 | 71 | 0 | 71 |
| 0x0522 | stone pavers Dark | 0 | 71 | 0 | 71 |
| 0x0E33 | brazier | 0 | 71 | 0 | 71 |
| 0x1B0C | bone pile | 1 | 69 | 0 | 69 |
| 0x2208 | crystal | 10 | 68 | 0 | 68 |
| 0x220A | crystal | 10 | 68 | 0 | 68 |
| 0x221E | crystal | 10 | 68 | 0 | 68 |
| 0x2214 | crystal | 10 | 67 | 0 | 67 |
| 0x1B16 | pelvis bone | 1 | 67 | 0 | 67 |
| 0x2216 | crystal | 10 | 66 | 0 | 66 |
| 0x221D | crystal | 10 | 65 | 0 | 65 |
| 0x2207 | crystal | 10 | 64 | 0 | 64 |
| 0x2213 | crystal | 10 | 64 | 0 | 64 |
| 0x0AC6 | carpet | 0 | 64 | 0 | 64 |
| 0x2449 | wood floor | 0 | 64 | 0 | 64 |
| 0x0AA9 | Rug East | 0 | 64 | 0 | 64 |

### 2.5 Tokuno (facet 4): top 150 of 906 graphics

Full list: `WorldData/4/census-4.csv`.

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x21A4 | no draw | 4 | 0 | 2,218 | 2,218 |
| 0x2746 | wood floor | 0 | 1,410 | 0 | 1,410 |
| 0x2445 | wood floor | 0 | 990 | 0 | 990 |
| 0x0D18 | mushroom | 1 | 839 | 0 | 839 |
| 0x0D16 | mushroom | 1 | 803 | 0 | 803 |
| 0x2750 | wood floor | 0 | 747 | 0 | 747 |
| 0x053C | cave floor | 0 | 559 | 0 | 559 |
| 0x2722 | wood floor | 0 | 380 | 0 | 380 |
| 0x053F | cave floor | 0 | 367 | 0 | 367 |
| 0x053D | cave floor | 0 | 314 | 0 | 314 |
| 0x272B | wood floor | 0 | 278 | 0 | 278 |
| 0x053E | cave floor | 0 | 264 | 0 | 264 |
| 0x2435 | wood floor | 0 | 231 | 0 | 231 |
| 0x2436 | wood floor | 0 | 208 | 0 | 208 |
| 0x2437 | wood floor | 0 | 204 | 0 | 204 |
| 0x272A | wood floor | 0 | 168 | 0 | 168 |
| 0x274E | wood floor | 0 | 135 | 0 | 135 |
| 0x21A3 | nodraw | 100 | 0 | 115 | 115 |
| 0x2462 | sand | 0 | 114 | 0 | 114 |
| 0x2728 | wood floor | 0 | 114 | 0 | 114 |
| 0x2726 | wood floor | 0 | 110 | 0 | 110 |
| 0x0D55 | vines | 20 | 108 | 0 | 108 |
| 0x0D56 | vines | 35 | 108 | 0 | 108 |
| 0x324F | sunken log | 0 | 95 | 0 | 95 |
| 0x2463 | sand | 0 | 91 | 0 | 91 |
| 0x243E | wood floor | 0 | 91 | 0 | 91 |
| 0x274F | wood floor | 0 | 86 | 0 | 86 |
| 0x0720 | wood | 5 | 0 | 84 | 84 |
| 0x053B | cave floor | 0 | 76 | 0 | 76 |
| 0x0DBD | lilly pad | 0 | 73 | 0 | 73 |
| 0x2748 | wood floor | 0 | 71 | 0 | 71 |
| 0x0C92 | untrimmed hedge | 6 | 69 | 0 | 69 |
| 0x32B1 | lava | 0 | 69 | 0 | 69 |
| 0x24BB | paper lantern | 2 | 67 | 0 | 67 |
| 0x0C98 | fan plant | 1 | 67 | 0 | 67 |
| 0x0DC2 | water lilly | 0 | 66 | 0 | 66 |
| 0x246C | bamboo | 20 | 65 | 0 | 65 |
| 0x0AB3 | rug | 0 | 63 | 0 | 63 |
| 0x0DC3 | water lilly | 0 | 63 | 0 | 63 |
| 0x0CA7 | rushes | 0 | 59 | 0 | 59 |
| 0x0CF2 | vines | 0 | 58 | 0 | 58 |
| 0x0DC1 | water lilly | 0 | 56 | 0 | 56 |
| 0x2446 | wood floor | 0 | 56 | 0 | 56 |
| 0x0DBE | lilly pads | 0 | 56 | 0 | 56 |
| 0x3250 | sunken log | 0 | 55 | 0 | 55 |
| 0x2723 | wood floor | 0 | 54 | 0 | 54 |
| 0x2724 | wood floor | 0 | 54 | 0 | 54 |
| 0x1779 | rock | 1 | 53 | 0 | 53 |
| 0x24C9 | paper lantern | 10 | 52 | 0 | 52 |
| 0x0D6A | vines | 0 | 51 | 0 | 51 |
| 0x0CEE | vines | 0 | 51 | 0 | 51 |
| 0x0D6C | vines | 20 | 51 | 0 | 51 |
| 0x3213 | swamp | 0 | 51 | 0 | 51 |
| 0x0D6B | vines | 20 | 51 | 0 | 51 |
| 0x0CA9 | snake plant | 3 | 50 | 0 | 50 |
| 0x2986 | wooden floor | 0 | 49 | 0 | 49 |
| 0x2447 | wood floor | 0 | 48 | 0 | 48 |
| 0x0D6D | vines | 0 | 48 | 0 | 48 |
| 0x0DBC | lilly pad | 0 | 47 | 0 | 47 |
| 0x2438 | wood floor | 0 | 46 | 0 | 46 |
| 0x322C | swamp | 0 | 45 | 0 | 45 |
| 0x0D34 | morning glories | 0 | 44 | 0 | 44 |
| 0x3241 | swamp | 0 | 42 | 0 | 42 |
| 0x0AEB |  | 0 | 0 | 42 | 42 |
| 0x0D81 | vines | 10 | 41 | 0 | 41 |
| 0x0C53 | cotton | 1 | 41 | 0 | 41 |
| 0x0C93 | blade plant | 2 | 41 | 0 | 41 |
| 0x0D82 | vines | 10 | 41 | 0 | 41 |
| 0x0D80 | vines | 0 | 41 | 0 | 41 |
| 0x324E | sunken log | 0 | 39 | 0 | 39 |
| 0x24BD | paper lantern | 2 | 39 | 0 | 39 |
| 0x0D91 | vines | 0 | 38 | 0 | 38 |
| 0x0D92 | vines | 0 | 37 | 0 | 37 |
| 0x0D93 | vines | 0 | 37 | 0 | 37 |
| 0x32AB | lava | 0 | 37 | 0 | 37 |
| 0x0C91 | untrimmed hedge | 6 | 37 | 0 | 37 |
| 0x0547 | cave floor | 0 | 36 | 0 | 36 |
| 0x32AD | lava | 0 | 35 | 0 | 35 |
| 0x0CED | vines | 0 | 35 | 0 | 35 |
| 0x1DD3 | rug | 0 | 35 | 0 | 35 |
| 0x2428 | wood floor | 0 | 34 | 0 | 34 |
| 0x2427 | wood floor | 0 | 34 | 0 | 34 |
| 0x0CF1 | vines | 0 | 34 | 0 | 34 |
| 0x32A2 | lava | 0 | 34 | 0 | 34 |
| 0x32A3 | lava | 0 | 34 | 0 | 34 |
| 0x32A4 | lava | 0 | 34 | 0 | 34 |
| 0x2426 | wood floor | 0 | 34 | 0 | 34 |
| 0x32A5 | lava | 0 | 34 | 0 | 34 |
| 0x0CEF | vines | 0 | 34 | 0 | 34 |
| 0x2425 | wood floor | 0 | 34 | 0 | 34 |
| 0x32AC | lava | 0 | 32 | 0 | 32 |
| 0x0549 | cave floor | 0 | 32 | 0 | 32 |
| 0x0AA9 | Rug East | 0 | 32 | 0 | 32 |
| 0x0AB4 | rug | 0 | 31 | 0 | 31 |
| 0x246D | bamboo | 20 | 31 | 0 | 31 |
| 0x0886 | wooden rail | 5 | 31 | 0 | 31 |
| 0x0AB6 | rug | 0 | 31 | 0 | 31 |
| 0x054D | cave floor | 0 | 31 | 0 | 31 |
| 0x32AE | lava | 0 | 30 | 0 | 30 |
| 0x2751 | wood floor | 0 | 30 | 0 | 30 |
| 0x0544 | cave floor | 0 | 30 | 0 | 30 |
| 0x2432 | wood floor | 0 | 29 | 0 | 29 |
| 0x2434 | wood floor | 0 | 29 | 0 | 29 |
| 0x17CD | snow | 0 | 29 | 0 | 29 |
| 0x241B | sculpture | 4 | 29 | 0 | 29 |
| 0x2470 | bamboo | 20 | 29 | 0 | 29 |
| 0x0548 | cave floor | 0 | 29 | 0 | 29 |
| 0x2419 | sculpture | 4 | 29 | 0 | 29 |
| 0x0540 | cave floor | 0 | 28 | 0 | 28 |
| 0x1B10 | bone pile | 1 | 28 | 0 | 28 |
| 0x0887 | wooden rail | 5 | 28 | 0 | 28 |
| 0x246F | bamboo | 20 | 28 | 0 | 28 |
| 0x054A | cave floor | 0 | 28 | 0 | 28 |
| 0x0721 | wood | 10 | 0 | 28 | 28 |
| 0x32AF | lava | 0 | 28 | 0 | 28 |
| 0x241A | sculpture | 4 | 27 | 0 | 27 |
| 0x2431 | wood floor | 0 | 26 | 0 | 26 |
| 0x2433 | wood floor | 0 | 26 | 0 | 26 |
| 0x1B7F | skeleton | 4 | 25 | 0 | 25 |
| 0x0CEB | vines | 0 | 25 | 0 | 25 |
| 0x0511 | marble floor | 0 | 25 | 0 | 25 |
| 0x32AA | lava | 0 | 25 | 0 | 25 |
| 0x13AB | pillow | 1 | 25 | 0 | 25 |
| 0x18E0 | mandrake | 1 | 25 | 0 | 25 |
| 0x32A8 | lava | 0 | 24 | 0 | 24 |
| 0x2818 | cabinet | 8 | 24 | 0 | 24 |
| 0x1B12 | bone | 1 | 23 | 0 | 23 |
| 0x0AB7 | rug | 0 | 23 | 0 | 23 |
| 0x0AB5 | rug | 0 | 23 | 0 | 23 |
| 0x242A | wood floor | 0 | 23 | 0 | 23 |
| 0x1B0B | bone pile | 1 | 23 | 0 | 23 |
| 0x245C | pond | 0 | 22 | 0 | 22 |
| 0x054B | cave floor | 0 | 22 | 0 | 22 |
| 0x13A8 | pillow | 1 | 22 | 0 | 22 |
| 0x24D0 | paper screen | 10 | 22 | 0 | 22 |
| 0x0ADA |  | 0 | 0 | 22 | 22 |
| 0x0C2D | debris | 1 | 21 | 0 | 21 |
| 0x274B | wood floor | 0 | 21 | 0 | 21 |
| 0x32A9 | lava | 0 | 21 | 0 | 21 |
| 0x1D8E | skeleton | 2 | 21 | 0 | 21 |
| 0x0C54 | cotton | 1 | 21 | 0 | 21 |
| 0x10D7 | small web | 0 | 21 | 0 | 21 |
| 0x32B0 | lava | 0 | 21 | 0 | 21 |
| 0x0AAD | Rug East | 0 | 21 | 0 | 21 |
| 0x1D8F | skeleton | 2 | 21 | 0 | 21 |
| 0x08E5 | stalagmites | 10 | 21 | 0 | 21 |
| 0x241E | urn  | 2 | 20 | 0 | 20 |
| 0x2474 | cherry blossoms | 20 | 20 | 0 | 20 |
| 0x0C2E | debris | 1 | 20 | 0 | 20 |
| 0x1B0E | bone pile | 1 | 20 | 0 | 20 |

### 2.6 Ter Mur (facet 5): top 150 of 2835 graphics

Full list: `WorldData/5/census-5.csv`.

| Graphic | Tiledata name | H | Boxes | Not drawn | Total |
|---|---|---|---|---|---|
| 0x21A3 | nodraw | 100 | 0 | 25,356 | 25,356 |
| 0x4DEA | Floor EAST98 | 0 | 18,974 | 0 | 18,974 |
| 0x5690 | hover over | 0 | 18,114 | 0 | 18,114 |
| 0x9B4F | eodon | 10 | 11,460 | 0 | 11,460 |
| 0x9B39 | floor tile | 0 | 9,660 | 0 | 9,660 |
| 0x0511 | marble floor | 0 | 9,448 | 0 | 9,448 |
| 0x9AEA | eodon | 10 | 8,221 | 0 | 8,221 |
| 0x41AF | MarbleFloor East | 0 | 5,935 | 0 | 5,935 |
| 0x32A3 | lava | 0 | 4,010 | 0 | 4,010 |
| 0x04FC | flagstones | 0 | 4,001 | 0 | 4,001 |
| 0x32A2 | lava | 0 | 3,918 | 0 | 3,918 |
| 0x32A4 | lava | 0 | 3,434 | 0 | 3,434 |
| 0x9ACF | floor tile | 0 | 3,181 | 0 | 3,181 |
| 0x049C | grey slate tile | 0 | 2,948 | 0 | 2,948 |
| 0x9B34 | floor tile | 0 | 2,874 | 0 | 2,874 |
| 0x32A5 | lava | 0 | 2,811 | 0 | 2,811 |
| 0x9BCB | clouds | 2 | 2,718 | 0 | 2,718 |
| 0x32AE | lava | 0 | 2,634 | 0 | 2,634 |
| 0x0709 | marble | 10 | 2,564 | 0 | 2,564 |
| 0x0537 | bricks | 0 | 2,518 | 0 | 2,518 |
| 0x0490 | BlackTile | 0 | 2,319 | 0 | 2,319 |
| 0x32AC | lava | 0 | 2,198 | 0 | 2,198 |
| 0x21A4 | no draw | 4 | 0 | 2,128 | 2,128 |
| 0x32AB | lava | 0 | 2,017 | 0 | 2,017 |
| 0x32AA | lava | 0 | 1,978 | 0 | 1,978 |
| 0x32AF | lava | 0 | 1,862 | 0 | 1,862 |
| 0x050A | wooden logs | 0 | 1,688 | 0 | 1,688 |
| 0x361C |  | 0 | 0 | 1,616 | 1,616 |
| 0x32B1 | lava | 0 | 1,486 | 0 | 1,486 |
| 0x04E2 | bricks | 0 | 1,387 | 0 | 1,387 |
| 0x32AD | lava | 0 | 1,319 | 0 | 1,319 |
| 0x3517 | waterfall | 10 | 1,234 | 0 | 1,234 |
| 0x32B0 | lava | 0 | 1,223 | 0 | 1,223 |
| 0x0580 | slate roof | 0 | 1,197 | 0 | 1,197 |
| 0x32A7 | lava | 0 | 1,182 | 0 | 1,182 |
| 0x21BC | nodraw | 0 | 0 | 1,145 | 1,145 |
| 0x0579 | stone roof | 0 | 1,124 | 0 | 1,124 |
| 0x0495 | marble floor | 0 | 1,109 | 0 | 1,109 |
| 0x41B0 | MarbleFloor East | 0 | 1,017 | 0 | 1,017 |
| 0x50E3 | Marble Floor Chocola | 0 | 1,016 | 0 | 1,016 |
| 0x32A8 | lava | 0 | 1,006 | 0 | 1,006 |
| 0x063B | marble roof | 0 | 997 | 0 | 997 |
| 0x133E | lava | 0 | 992 | 0 | 992 |
| 0x32A6 | lava | 0 | 963 | 0 | 963 |
| 0x0512 | marble floor | 0 | 917 | 0 | 917 |
| 0x0513 | marble floor | 0 | 905 | 0 | 905 |
| 0x0506 | wooden logs | 0 | 890 | 0 | 890 |
| 0x32A9 | lava | 0 | 857 | 0 | 857 |
| 0x1331 | lava | 0 | 831 | 0 | 831 |
| 0x0496 | marble floor | 0 | 797 | 0 | 797 |
| 0x050C | wooden logs | 0 | 782 | 0 | 782 |
| 0x057F | slate roof | 0 | 746 | 0 | 746 |
| 0x0CEE | vines | 0 | 745 | 0 | 745 |
| 0x0ABE | carpet | 0 | 736 | 0 | 736 |
| 0x0514 | marble floor | 0 | 705 | 0 | 705 |
| 0x0573 | stone roof | 0 | 704 | 0 | 704 |
| 0x41F1 | Wallset1 FloorB East | 0 | 655 | 0 | 655 |
| 0x0576 | stone roof | 0 | 649 | 0 | 649 |
| 0x051A | stone pavers | 0 | 648 | 0 | 648 |
| 0x4D2F | StaitCube East | 5 | 645 | 0 | 645 |
| 0x1098 | carpet | 0 | 629 | 0 | 629 |
| 0x41EA | Wallset1 FloorB East | 0 | 627 | 0 | 627 |
| 0x435F | GargStairA2 north | 5 | 616 | 0 | 616 |
| 0x1349 | lava | 0 | 586 | 0 | 586 |
| 0x134B | lava | 0 | 574 | 0 | 574 |
| 0x1343 | lava | 0 | 560 | 0 | 560 |
| 0x41ED | Wallset1 FloorB East | 0 | 559 | 0 | 559 |
| 0x049D | sandstone paver | 0 | 555 | 0 | 555 |
| 0x050D | marble floor | 0 | 517 | 0 | 517 |
| 0x1346 | lava | 0 | 503 | 0 | 503 |
| 0x9B51 | eodon stairs | 5 | 496 | 0 | 496 |
| 0x0519 | stone pavers Dark | 0 | 484 | 0 | 484 |
| 0x0CF2 | vines | 0 | 472 | 0 | 472 |
| 0x46CC | ruin Floor01 | 0 | 465 | 0 | 465 |
| 0x41F5 | Wallset1 FloorB East | 0 | 461 | 0 | 461 |
| 0x0750 | stone | 10 | 0 | 460 | 460 |
| 0x435E | GargStairA2 east | 5 | 453 | 0 | 453 |
| 0x351D | waterfall | 3 | 425 | 0 | 425 |
| 0x1339 | lava | 0 | 424 | 0 | 424 |
| 0x3511 | waterfall | 10 | 413 | 0 | 413 |
| 0x9B33 | floor tile | 0 | 409 | 0 | 409 |
| 0x432D | Floor B2 East | 0 | 398 | 0 | 398 |
| 0x132F | lava | 0 | 395 | 0 | 395 |
| 0x12F0 | lava | 0 | 387 | 0 | 387 |
| 0x9B31 | floor tile | 0 | 376 | 0 | 376 |
| 0x0C98 | fan plant | 1 | 370 | 0 | 370 |
| 0x0C93 | blade plant | 2 | 370 | 0 | 370 |
| 0x070A | marble stairs | 5 | 366 | 0 | 366 |
| 0x0AC7 | carpet | 0 | 355 | 0 | 355 |
| 0x3523 | waterfall | 3 | 346 | 0 | 346 |
| 0x329F | lava | 0 | 346 | 0 | 346 |
| 0x070B | marble stairs | 5 | 344 | 0 | 344 |
| 0x4361 | GargStairA2 west | 5 | 342 | 0 | 342 |
| 0x34F9 | waterfall | 10 | 342 | 0 | 342 |
| 0x41B6 | MarbleTrim East | 0 | 340 | 0 | 340 |
| 0x1328 | lava | 0 | 338 | 0 | 338 |
| 0x41F4 | Wallset1 FloorB East | 0 | 334 | 0 | 334 |
| 0x1330 | lava | 0 | 328 | 0 | 328 |
| 0x1340 | lava | 0 | 313 | 0 | 313 |
| 0x0639 | stone roof | 1 | 311 | 0 | 311 |
| 0x134D | lava | 0 | 304 | 0 | 304 |
| 0x4360 | GargStairA2 south | 5 | 296 | 0 | 296 |
| 0x19AB | fire | 0 | 292 | 0 | 292 |
| 0x1341 | lava | 0 | 292 | 0 | 292 |
| 0x3005 | floor tile  CMI | 0 | 292 | 0 | 292 |
| 0x133B | lava | 0 | 283 | 0 | 283 |
| 0x04C3 | wooden logs | 0 | 281 | 0 | 281 |
| 0x04C4 | wooden logs | 0 | 277 | 0 | 277 |
| 0x0AD1 | carpet | 0 | 273 | 0 | 273 |
| 0x070D | marble stairs | 5 | 273 | 0 | 273 |
| 0x9AD0 | floor tile | 0 | 271 | 0 | 271 |
| 0x04C5 | wooden logs | 0 | 265 | 0 | 265 |
| 0x46CD | ruin Floor02 | 0 | 257 | 0 | 257 |
| 0x1333 | lava | 0 | 256 | 0 | 256 |
| 0x050E | white marble | 0 | 255 | 0 | 255 |
| 0x0D81 | vines | 10 | 253 | 0 | 253 |
| 0x12F1 | lava | 0 | 252 | 0 | 252 |
| 0x0508 | wooden logs | 0 | 250 | 0 | 250 |
| 0x0505 | wooden logs | 0 | 249 | 0 | 249 |
| 0x34FF | waterfall | 3 | 249 | 0 | 249 |
| 0x2198 | nodraw | 0 | 0 | 249 | 249 |
| 0x9B37 | floor tile | 0 | 249 | 0 | 249 |
| 0x9AD2 | floor tile | 0 | 248 | 0 | 248 |
| 0x41B8 | MarbleTrim East | 0 | 247 | 0 | 247 |
| 0x0AC8 | carpet | 0 | 246 | 0 | 246 |
| 0x12EE | lava | 0 | 241 | 0 | 241 |
| 0x9ACE | floor tile | 0 | 238 | 0 | 238 |
| 0x363A |  | 20 | 0 | 232 | 232 |
| 0x0D6C | vines | 20 | 232 | 0 | 232 |
| 0x329E | lava | 0 | 231 | 0 | 231 |
| 0x4301 | Large Stairs South | 5 | 230 | 0 | 230 |
| 0x0D6D | vines | 0 | 228 | 0 | 228 |
| 0x9ACC | floor tile | 0 | 227 | 0 | 227 |
| 0x0D92 | vines | 0 | 225 | 0 | 225 |
| 0x363B |  | 20 | 0 | 224 | 224 |
| 0x50E5 | Marble Floor Checker | 0 | 221 | 0 | 221 |
| 0x0D6B | vines | 20 | 219 | 0 | 219 |
| 0x0D80 | vines | 0 | 216 | 0 | 216 |
| 0x0D93 | vines | 0 | 215 | 0 | 215 |
| 0x0D82 | vines | 10 | 215 | 0 | 215 |
| 0x9AEC | eodon stairs | 5 | 210 | 0 | 210 |
| 0x132A | lava | 0 | 210 | 0 | 210 |
| 0x0911 | dirt patch | 0 | 209 | 0 | 209 |
| 0x08E6 | flowstone | 10 | 206 | 0 | 206 |
| 0x3505 | waterfall | 3 | 205 | 0 | 205 |
| 0x1327 | lava | 0 | 201 | 0 | 201 |
| 0x070C | marble stairs | 5 | 200 | 0 | 200 |
| 0x34F3 | waterfall | 10 | 196 | 0 | 196 |
| 0x42E3 | Large Stairs East | 5 | 193 | 0 | 193 |
| 0x9B35 | floor tile | 0 | 192 | 0 | 192 |


## 3. Items

**How determined.** `Content/Items.json` has 5102 kinds (`ItemCatalog`: Type, Graphic, Layer, Category, optional Weapon/Armor). Tile names come from `ServUO/ClientFiles/tiledata.mul` (the file the game's `UoData.Tiles` reads at runtime; the format limits names to 20 characters, which the in-game matcher sees too). Three places draw items:
- **On the ground**: `GroundItem` shows one pouch prefab (`GroundItem.prefab` → `_pouch`) for every kind; it does not use `PropCatalog`. (`PropCatalog` serves map statics and the old protocol viewer `EntityViews`.)
- **Held (layers 1 and 2)**: `SidekickDresser.Arm` matches the tile name against `SidekickWardrobe.Weapons` (10 groups: shield|buckler|kite|heater; bow but not bowl; staff|crook|wand; spear|pike|pitchfork|halberd|bardiche|war fork|scythe|lance|glaive; pickaxe|shovel|spade; dagger|kryss|knife|skinning; axe|hatchet; mace|club|maul|scepter|hammer pick|war hammer; hammer|tongs|sledge; sword|blade|katana|scimitar|cutlass|cleaver|rapier). No match = empty hand.
- **Worn**: `SidekickDresser.Dress` covers OuterTorso, MiddleTorso, InnerTorso, Shirt, Arms, Gloves, OuterLegs, InnerLegs, Pants, Shoes, Helm, Cloak, plus hair, beard and the apron on Waist. Gear is not modelled per item: it picks an outfit of the same material class (cloth, leather, chain, plate, ...) with a part for the region. Layers outside this list are never drawn.

### 3.1 On the ground / dropped: all 5102 kinds use the generic pouch

Nothing in code gives any item kind its own ground model. Kinds by Category show what art would be needed:

| Category | Kinds |
|---|---|
| Other | 2681 |
| Armor | 483 |
| Weapon | 372 |
| Clothing | 364 |
| Container | 266 |
| Resource | 195 |
| Food | 190 |
| Tool | 115 |
| Scroll | 115 |
| Shield | 85 |
| Jewelry | 81 |
| Light | 60 |
| Potion | 38 |
| Instrument | 22 |
| Gem | 18 |
| Reagent | 16 |
| Gold | 1 |

### 3.2 Held in hand: weapons, shields, tools and lights with no model

350 held kinds match a wardrobe group (some art, one model per group). **118 do not** and show empty hands:

| Kind | Graphic | Tile name | Layer | Category |
|---|---|---|---|---|
| DupresShield | 0x2B01 |  | TwoHanded | Shield |
| GargoylesPickaxe | 0x-001 |  | None | Weapon |
| BarbedWhip | 0xA289 | barbed whip | OneHanded | Weapon |
| BarbedWhipOfPlundering | 0xA289 | barbed whip | OneHanded | Weapon |
| BladedWhip | 0xA28B | barbed whip | OneHanded | Weapon |
| BladedWhipOfPlundering | 0xA28B | barbed whip | OneHanded | Weapon |
| BookOfMasteries | 0x225A | bard spell book | OneHanded | Other |
| Bokuto | 0x27A8 | bokuto | OneHanded | Weapon |
| PeasantsBokuto | 0x27A8 | bokuto | OneHanded | Weapon |
| BoneHarvester | 0x26BB | bone harvester | OneHanded | Weapon |
| BreathOfTheDead | 0x26BB | bone harvester | OneHanded | Weapon |
| CullingBlade | 0x26BB | bone harvester | OneHanded | Weapon |
| BookOfBushido | 0x238C | book of bushido | OneHanded | Other |
| BookOfNinjitsu | 0x23A0 | book of ninjitsu | OneHanded | Other |
| Boomerang | 0x08FF | boomerang | OneHanded | Weapon |
| RaptorClaw | 0x08FF | boomerang | OneHanded | Weapon |
| StormCaller | 0x08FF | boomerang | OneHanded | Weapon |
| Candle | 0x0A28 | candle | TwoHanded | Light |
| AbyssReaver | 0x0901 | cyclone | OneHanded | Weapon |
| BansheesCall | 0x0901 | cyclone | OneHanded | Weapon |
| Cyclone | 0x0901 | cyclone | OneHanded | Weapon |
| HalawasHuntingBowGargoyle | 0x0901 | cyclone | OneHanded | Weapon |
| StoneSlithClaw | 0x0901 | cyclone | OneHanded | Weapon |
| WindOfCorruption | 0x0901 | cyclone | OneHanded | Weapon |
| Daisho | 0x27A9 | daisho | TwoHanded | Weapon |
| SwordsOfProsperity | 0x27A9 | daisho | TwoHanded | Weapon |
| DarkSource | 0x1646 | dark source | TwoHanded | Other |
| BarbedLongbow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| BlightGrippedLongbow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| ElvenCompositeLongbow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| FaerieFire | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| FrozenLongbow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| LongbowOfMight | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| SilvanisFeywoodBow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| SlayerLongbow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| WildfireBow | 0x2D1E | elven composite lo | TwoHanded | Weapon |
| AdventurersMachete | 0x2D35 | elven machete | OneHanded | Weapon |
| BoneMachete | 0x2D35 | elven machete | OneHanded | Weapon |
| DiseasedMachete | 0x2D35 | elven machete | OneHanded | Weapon |
| ElvenMachete | 0x2D35 | elven machete | OneHanded | Weapon |
| MacheteOfDefense | 0x2D35 | elven machete | OneHanded | Weapon |
| OrcishMachete | 0x2D35 | elven machete | OneHanded | Weapon |
| RighteousAnger | 0x2D35 | elven machete | OneHanded | Weapon |
| BegFishingPole | 0x0DC0 | fishing pole | OneHanded | Tool |
| CaddelliteFishingPole | 0x0DC0 | fishing pole | OneHanded | Other |
| CaptainBlackheartsFishingPole | 0x0DC0 | fishing pole | OneHanded | Other |
| FishingPole | 0x0DC0 | fishing pole | OneHanded | Tool |
| XenrrFishingPole | 0x0DC0 | fishing pole | OneHanded | Tool |
| Fukiya | 0x27AA | fukiya | OneHanded | Weapon |
| GargishTalwar | 0x0908 | gargish talwar | TwoHanded | Weapon |
| LightInTheVoid | 0x0908 | gargish talwar | TwoHanded | Weapon |
| GargishWoodenShield | 0x4200 | gargish wooden shiel | TwoHanded | Shield |
| GrugorsShieldGargoyle | 0x4200 | gargish wooden shiel | TwoHanded | Shield |
| MysticsGuard | 0x4200 | gargish wooden shiel | TwoHanded | Shield |
| ResonantShieldOfVengeance | 0x4200 | gargish wooden shiel | TwoHanded | Shield |
| GargishBardiche | 0x48B4 | gargoyle bardich eas | TwoHanded | Weapon |
| GargishButcherKnife | 0x48B6 | gargoyle butcherskni | OneHanded | Weapon |
| GargishSmugglersEdge | 0x48B6 | gargoyle butcherskni | OneHanded | Weapon |
| GargishDaisho | 0x48D0 | gargoyle daisho east | TwoHanded | Weapon |
| GargishTekagi | 0x48CE | gargoyle tekagi east | TwoHanded | Weapon |
| GargishTessen | 0x48CC | gargoyle tessen east | TwoHanded | Weapon |
| GargishWarFork | 0x48BE | gargoyle warfork eas | OneHanded | Weapon |
| HailstormGargoyle | 0x48BE | gargoyle warfork eas | OneHanded | Weapon |
| DarkenedSky | 0x27AD | kama | TwoHanded | Weapon |
| Kama | 0x27AD | kama | TwoHanded | Weapon |
| Lajatang | 0x27A7 | lajatang | TwoHanded | Weapon |
| BegLantern | 0x0A25 | lantern | TwoHanded | Light |
| Lantern | 0x0A25 | lantern | TwoHanded | Light |
| LanternOfLight | 0x0A25 | lantern | TwoHanded | Light |
| LanternOfSouls | 0x0A25 | lantern | TwoHanded | Light |
| LibraryFriendLantern | 0x0A25 | lantern | TwoHanded | Light |
| SmugglersLantern | 0x0A25 | lantern | TwoHanded | Light |
| TwilightLantern | 0x0A25 | lantern | TwoHanded | Light |
| LightSource | 0x1647 | light source | TwoHanded | Other |
| CorgulsHandbookOnMysticism | 0x2D9D | mysticspellbook east | OneHanded | Other |
| FallenMysticsSpellbook | 0x2D9D | mysticspellbook east | OneHanded | Other |
| MysticBook | 0x2D9D | mysticspellbook east | OneHanded | Other |
| CompleteNecromancerSpellbook | 0x2253 | necromancer book | OneHanded | Other |
| CorgulsHandbookOnTheUndead | 0x2253 | necromancer book | OneHanded | Other |
| JuonarsGrimoire | 0x2253 | necromancer book | OneHanded | Other |
| NecromancerSpellbook | 0x2253 | necromancer book | OneHanded | Other |
| OssianGrimoire | 0x2253 | necromancer book | OneHanded | Other |
| NoDachi | 0x27A2 | no-dachi | TwoHanded | Weapon |
| SwordOfTheStampede | 0x27A2 | no-dachi | TwoHanded | Weapon |
| TheDestroyer | 0x27A2 | no-dachi | TwoHanded | Weapon |
| TheDragonsTail | 0x27A2 | no-dachi | TwoHanded | Weapon |
| DragonNunchaku | 0x27AE | nunchaku | TwoHanded | Weapon |
| Nunchaku | 0x27AE | nunchaku | TwoHanded | Weapon |
| BookOfChivalry | 0x2252 | paladin spellbook | OneHanded | Other |
| DemonForks | 0x27AF | sai | TwoHanded | Weapon |
| Sai | 0x27AF | sai | TwoHanded | Weapon |
| SpellweavingBook | 0x2D50 | spell weaving book | OneHanded | Other |
| AnonsSpellbook | 0x0EFA | spellbook | OneHanded | Other |
| ClaininsSpellbook | 0x0EFA | spellbook | OneHanded | Other |
| ConjurersGrimoire | 0x0EFA | spellbook | OneHanded | Other |
| CultistsRitualTome | 0x0EFA | spellbook | OneHanded | Other |
| GrandGrimoire | 0x0EFA | spellbook | OneHanded | Other |
| HallowedSpellbook | 0x0EFA | spellbook | OneHanded | Other |
| ScrappersCompendium | 0x0EFA | spellbook | OneHanded | Other |
| Spellbook | 0x0EFA | spellbook | OneHanded | Other |
| TomeOfEnlightenment | 0x0EFA | spellbook | OneHanded | Other |
| TomeOfLostKnowledge | 0x0EFA | spellbook | OneHanded | Other |
| ClawsOfTheBerserker | 0x27AB | tekagi | TwoHanded | Weapon |
| Tekagi | 0x27AB | tekagi | TwoHanded | Weapon |
| PilferedDancerFans | 0x27A3 | tessen | TwoHanded | Weapon |
| Tessen | 0x27A3 | tessen | TwoHanded | Weapon |
| WindsEdge | 0x27A3 | tessen | TwoHanded | Weapon |
| Exiler | 0x27A6 | tetsubo | TwoHanded | Weapon |
| Tetsubo | 0x27A6 | tetsubo | TwoHanded | Weapon |
| CandlewoodTorch | 0x0F6B | torch%es% | TwoHanded | Shield |
| Matches | 0x0F6B | torch%es% | TwoHanded | Other |
| Torch | 0x0F6B | torch%es% | TwoHanded | Light |
| Wakizashi | 0x27A4 | wakizashi | OneHanded | Weapon |
| AdmiralJacksPumpkinSpiceAle | 0xA40B | weapon_bottle | TwoHanded | Shield |
| HalawasHuntingBow | 0x27A5 | yumi | TwoHanded | Weapon |
| HanzosBow | 0x27A5 | yumi | TwoHanded | Weapon |
| TheHorselord | 0x27A5 | yumi | TwoHanded | Weapon |
| Yumi | 0x27A5 | yumi | TwoHanded | Weapon |

By tile name (what art to source): spellbook ×10; elven composite lo ×9; elven machete ×7; lantern ×7; cyclone ×6; fishing pole ×5; necromancer book ×5; barbed whip ×4; gargish wooden shiel ×4; yumi ×4; no-dachi ×4; bone harvester ×3; boomerang ×3; torch%es% ×3; mysticspellbook east ×3; tessen ×3; bokuto ×2; tekagi ×2; daisho ×2; kama ×2; sai ×2; nunchaku ×2; (unnamed) ×2; tetsubo ×2; gargoyle butcherskni ×2; gargish talwar ×2; gargoyle warfork eas ×2; weapon_bottle ×1; book of bushido ×1; paladin spellbook ×1; bard spell book ×1; book of ninjitsu ×1; candle ×1; dark source ×1; fukiya ×1; gargoyle bardich eas ×1; gargoyle daisho east ×1; gargoyle tekagi east ×1; gargoyle tessen east ×1; lajatang ×1; light source ×1; spell weaving book ×1; wakizashi ×1

### 3.3 Worn: layers that are never drawn

303 kinds sit on a layer the dresser ignores. `Backpack` and `Mount` are skipped on purpose (see `Signature`); `FirstValid`/`Invalid` are ServUO placeholder layers. The real gaps are jewellery, talismans and non-apron waist items.

| Layer | Kinds | Verdict |
|---|---|---|
| Neck | 62 | missing (necklaces, gorgets, beads) |
| Earrings | 36 | missing |
| Talisman | 35 | missing |
| FirstValid | 34 | ServUO placeholder layer, check items |
| Backpack | 32 | by design: no backpack model on the back |
| Ring | 29 | missing |
| Bracelet | 26 | missing |
| Invalid | 25 | ServUO placeholder layer, check items |
| Waist | 21 | missing (sashes, half-aprons; only `apron` names draw) |
| Mount | 3 | missing (no mount models, see 1.2) |

| Kind | Graphic | Tile name | Layer | Category |
|---|---|---|---|---|
| AlchemistCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| AlchemistsSatchel | 0x0E75 | backpack | Backpack | Container |
| AllegiancePouch | 0x0E75 | backpack | Backpack | Container |
| AnniversaryBag21st | 0x0E75 | backpack | Backpack | Container |
| Backpack | 0x0E75 | backpack | Backpack | Container |
| BaseCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| BaseRewardBackpack | 0x0E75 | backpack | Backpack | Container |
| BuyBackpack | 0x0E75 | backpack | Backpack | Container |
| CarpentersCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| CarpentersSatchel | 0x0E75 | backpack | Backpack | Container |
| ChefsSatchel | 0x0E75 | backpack | Backpack | Container |
| CreatureBackpack | 0x0E75 | backpack | Backpack | Container |
| DecorativeKitchenSet | 0x0E75 | backpack | Backpack | Container |
| DecorativeStableSet | 0x0E75 | backpack | Backpack | Container |
| DustyAdventurersBackpack | 0x0E75 | backpack | Backpack | Container |
| DustyExplorersBackpack | 0x0E75 | backpack | Backpack | Container |
| DustyHuntersBackpack | 0x0E75 | backpack | Backpack | Container |
| EodonianRewardBag | 0x0E75 | backpack | Backpack | Container |
| FletcherCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| FletchersSatchel | 0x0E75 | backpack | Backpack | Container |
| LumberjacksSatchel | 0x0E75 | backpack | Backpack | Container |
| MinersQuestSatchel | 0x0E75 | backpack | Backpack | Container |
| MyrmidexRewardBag | 0x0E75 | backpack | Backpack | Container |
| PlagueBeastBackpack | 0x261B | plague beast lord | Backpack | Container |
| SmithsCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| SmithsSatchel | 0x0E75 | backpack | Backpack | Container |
| StewardBackpack | 0x0E75 | backpack | Backpack | Container |
| StrongBackpack | 0x0E75 | backpack | Backpack | Container |
| TailorsCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| TinkersCraftsmanSatchel | 0x0E75 | backpack | Backpack | Container |
| TinkersSatchel | 0x0E75 | backpack | Backpack | Container |
| VendorBackpack | 0x0E75 | backpack | Backpack | Container |
| AlchemistsBauble | 0x1086 | bracelet | Bracelet | Jewelry |
| BraceletOfBinding | 0x1086 | bracelet | Bracelet | Jewelry |
| BraceletOfHealth | 0x1086 | bracelet | Bracelet | Jewelry |
| BraceletOfPrimalConsumption | 0x1086 | bracelet | Bracelet | Jewelry |
| BraceletOfProtection | 0x1086 | bracelet | Bracelet | Jewelry |
| BraceletOfResilience | 0x1086 | bracelet | Bracelet | Jewelry |
| BrilliantAmberBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| ClaspOfConcentration | 0x1F06 | bracelet | Bracelet | Jewelry |
| DarkSapphireBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| EnchantedCoralBracelet | 0x1F06 | bracelet | Bracelet | Jewelry |
| FireRubyBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| GargishBracelet | 0x4211 | gargish bracelet | Bracelet | Jewelry |
| GoldBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| GreaterBraceletOfBinding | 0x1086 | bracelet | Bracelet | Jewelry |
| HolidayTimepiece | 0x1086 | bracelet | Bracelet | Tool |
| JadeArmband | 0x1086 | bracelet | Bracelet | Jewelry |
| NovoBleue | 0x1086 | bracelet | Bracelet | Jewelry |
| OrnamentOfTheMagician | 0x1086 | bracelet | Bracelet | Jewelry |
| ResilientBracer | 0x1086 | bracelet | Bracelet | Jewelry |
| SilverBracelet | 0x1F06 | bracelet | Bracelet | Jewelry |
| SoleilRouge | 0x1086 | bracelet | Bracelet | Jewelry |
| TokenOfHolyFavor | 0x1086 | bracelet | Bracelet | Jewelry |
| Venom | 0x1086 | bracelet | Bracelet | Jewelry |
| VesperCollectionBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| WhitePearlBracelet | 0x1086 | bracelet | Bracelet | Jewelry |
| WristWatch | 0x1086 | bracelet | Bracelet | Tool |
| BestialEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| DragonJadeEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| EarringsOfProtection | 0x1087 | earrings | Earrings | Jewelry |
| EarringsOfVillainousEpiphany | 0x4213 | gargish earrings | Earrings | Armor |
| EarringsOfVirtuousEpiphany | 0x4213 | gargish earrings | Earrings | Armor |
| FishermansEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| GargishBrightsightLenses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| GargishEarringsBearingTheCrestOfBlackthorn1 | 0x4213 | gargish earrings | Earrings | Armor |
| GargishEarringsBearingTheCrestOfBlackthorn2 | 0x4213 | gargish earrings | Earrings | Armor |
| GargishEarringsBearingTheCrestOfBlackthorn3 | 0x4213 | gargish earrings | Earrings | Armor |
| GargishFoldedSteelGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishGlassesBearingTheCrestOfBlackthorn1 | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishGlassesBearingTheCrestOfBlackthorn2 | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishGlassesBearingTheCrestOfBlackthorn3 | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishGlassesOfTheArts | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishMaceAndShieldGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishNictitatingLens | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishPrismaticLenses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishTradesGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishTreasuresAndTrinketsGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GargishWizardsCrystalGlasses | 0x4644 | gargoyleglasses sout | Earrings | Armor |
| GoldEarrings | 0x1087 | earrings | Earrings | Jewelry |
| KrampusMinionEarrings | 0xA295 | holiday_gargoyle_ear | Earrings | Armor |
| MerchantsTrinket | 0x1087 | earrings | Earrings | Jewelry |
| MorphEarrings | 0x1087 | earrings | Earrings | Jewelry |
| NocturneEarrings | 0x1F07 | earrings | Earrings | Jewelry |
| ObsidianEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| SilverEarrings | 0x1F07 | earrings | Earrings | Jewelry |
| TransparentHeart | 0x1087 | earrings | Earrings | Jewelry |
| VesperCollectionEarrings | 0x1087 | earrings | Earrings | Jewelry |
| VirtuososEarpieces | 0x4213 | gargish earrings | Earrings | Armor |
| VvVGargishEarrings | 0x4213 | gargish earrings | Earrings | Armor |
| WamapsBoneEarrings | 0x1087 | earrings | Earrings | Jewelry |
| WamapsBoneEarringsGargoyle | 0x4213 | gargish earrings | Earrings | Armor |
| BaseCostume | 0x19BC | halloween mask | FirstValid | Shield |
| BloodwormCostume | 0x19BC | halloween mask | FirstValid | Shield |
| CentaurCostume | 0x19BC | halloween mask | FirstValid | Shield |
| CyclopsCostume | 0x19BC | halloween mask | FirstValid | Shield |
| DragonWolfCostume | 0x19BC | halloween mask | FirstValid | Shield |
| DrakeCostume | 0x19BC | halloween mask | FirstValid | Shield |
| DreamWraithCostume | 0x19BC | halloween mask | FirstValid | Shield |
| EtherealWarriorCostume | 0x19BC | halloween mask | FirstValid | Shield |
| ExodusMinionCostume | 0x19BC | halloween mask | FirstValid | Shield |
| FireElementalCostume | 0x19BC | halloween mask | FirstValid | Shield |
| GazerCostume | 0x19BC | halloween mask | FirstValid | Shield |
| GiantPixieCostume | 0x19BC | halloween mask | FirstValid | Shield |
| GiantToadCostume | 0x19BC | halloween mask | FirstValid | Shield |
| GoreFiendCostume | 0x19BC | halloween mask | FirstValid | Shield |
| LadyOfTheSnowCostume | 0x19BC | halloween mask | FirstValid | Shield |
| MaddeningHorrorCostume | 0x19BC | halloween mask | FirstValid | Shield |
| MinotaurCostume | 0x19BC | halloween mask | FirstValid | Shield |
| MongbatCostume | 0x19BC | halloween mask | FirstValid | Shield |
| OniCostume | 0x19BC | halloween mask | FirstValid | Shield |
| OphidianMatriarchCostume | 0x19BC | halloween mask | FirstValid | Shield |
| OphidianWarriorCostume | 0x19BC | halloween mask | FirstValid | Shield |
| PixieCostume | 0x19BC | halloween mask | FirstValid | Shield |
| SabertoothedTigerCostume | 0x19BC | halloween mask | FirstValid | Shield |
| SatyrCostume | 0x19BC | halloween mask | FirstValid | Shield |
| ShadowWyrmCostume | 0x19BC | halloween mask | FirstValid | Shield |
| SkeletonCostume | 0x19BC | halloween mask | FirstValid | Shield |
| SkitteringHopperCostume | 0x19BC | halloween mask | FirstValid | Shield |
| SolenWarriorCostume | 0x19BC | halloween mask | FirstValid | Shield |
| TerathanWarriorCostume | 0x19BC | halloween mask | FirstValid | Shield |
| TitanCostume | 0x19BC | halloween mask | FirstValid | Shield |
| VoidWandererCostume | 0x19BC | halloween mask | FirstValid | Shield |
| WispCostume | 0x19BC | halloween mask | FirstValid | Shield |
| WolfSpiderCostume | 0x19BC | halloween mask | FirstValid | Shield |
| ZombieCostume | 0x19BC | halloween mask | FirstValid | Shield |
| ChargerOfTheFallen | 0x2D9C |  | Invalid | Other |
| EtherealAncientHellHound | 0x3FFD | ancient hell hound | Invalid | Other |
| EtherealBeetle | 0x260F | giant beetle | Invalid | Other |
| EtherealBoura | 0x46F8 | boura south | Invalid | Other |
| EtherealCuSidhe | 0x2D96 | cu sidhe | Invalid | Other |
| EtherealHiryu | 0x276A | hiryu | Invalid | Other |
| EtherealHorse | 0x20DD | horse frame | Invalid | Other |
| EtherealKirin | 0x25A0 | kirin  | Invalid | Other |
| EtherealLasher | 0x9E35 | horse_unicorn_rb_sta | Invalid | Other |
| EtherealLlama | 0x20F6 | llama | Invalid | Other |
| EtherealMount | 0x-001 |  | Invalid | Other |
| EtherealOstard | 0x2135 | desert ostard | Invalid | Other |
| EtherealReptalon | 0x2D95 | chimera | Invalid | Other |
| EtherealRidgeback | 0x2615 | ridgeback | Invalid | Other |
| EtherealSerpentineDragon | 0xA010 | dragon_asian_mount | Invalid | Other |
| EtherealSwampDragon | 0x2619 | swamp dragon | Invalid | Other |
| EtherealTarantula | 0x9DD6 | tarantula_statue | Invalid | Other |
| EtherealTiger | 0x9844 | tiger statue | Invalid | Other |
| EtherealUnicorn | 0x25CE | unicorn  | Invalid | Other |
| EtherealWarBoar | 0xA554 | war_boar_mount | Invalid | Other |
| GMEthVirtual | 0x-001 |  | Invalid | Tool |
| GMEthereal | 0x0000 |  | Invalid | Tool |
| RideablePolarBear | 0x20E1 | polar bear frame | Invalid | Other |
| Runebook | 0x22C5 | runebook | Invalid | Other |
| RunicAtlas | 0x9C16 | large runebook right | Invalid | Other |
| BoatMountItem | 0x3E96 | prow | Mount | Other |
| MountItem | 0x-001 |  | Mount | Other |
| VirtualMountItem | 0x3EA0 | ship | Mount | Other |
| AloronsGorget | 0x7829 | collar_tiger_h | Neck | Armor |
| AmuletOfRighteousness | 0x1F08 | necklace | Neck | Jewelry |
| AnkhPendant | 0x3BB5 |  | Neck | Jewelry |
| AquaPendant | 0x1088 | necklace | Neck | Jewelry |
| BestialGorget | 0x13C7 | leather gorget | Neck | Armor |
| BestialNecklace | 0x4210 | gargish necklace | Neck | Armor |
| DarkwoodGorget | 0x2B69 | elven plate gorget | Neck | Armor |
| DetectiveCredentials | 0x1088 | necklace | Neck | Jewelry |
| ElegantCollar | 0xA40F | neck_mempo | Neck | Armor |
| ElegantCollarOfFortune | 0xA40F | neck_mempo | Neck | Armor |
| FellowshipMedallion | 0xA429 | fellowship_medallion | Neck | Other |
| GargishAmulet | 0x4D0B | gargish necklace | Neck | Armor |
| GargishNecklace | 0x4210 | gargish necklace | Neck | Armor |
| GargishStoneAmulet | 0x4D0A | gargish necklace | Neck | Armor |
| GladiatorsCollar | 0x1413 | platemail gorget | Neck | Armor |
| GoldBeadNecklace | 0x1089 | necklace | Neck | Jewelry |
| GoldNecklace | 0x1088 | necklace | Neck | Jewelry |
| GorgetOfVillainousEpiphany | 0x1413 | platemail gorget | Neck | Armor |
| GorgetOfVirtuousEpiphany | 0x1413 | platemail gorget | Neck | Armor |
| HideGorget | 0x2B76 | hide gorget | Neck | Armor |
| HonestyGorget | 0x2B0E |  | Neck | Armor |
| HygieiasAmulet | 0x1088 | necklace | Neck | Jewelry |
| InitiationGorget | 0x13C7 | leather gorget | Neck | Armor |
| JackalsCollar | 0x1413 | platemail gorget | Neck | Armor |
| KnightsPlateGorget | 0x1413 | platemail gorget | Neck | Armor |
| Lavaliere | 0x1088 | necklace | Neck | Jewelry |
| LeafGorget | 0x2FC7 | lt_armor_gorget | Neck | Armor |
| LeatherGorget | 0x13C7 | leather gorget | Neck | Armor |
| LeatherMempo | 0x277A | leather mempo | Neck | Armor |
| LeurociansMempoOfFortune | 0x277A | leather mempo | Neck | Armor |
| LuckyNecklace | 0x1088 | necklace | Neck | Jewelry |
| MyrmidonGorget | 0x13D6 | studded gorget | Neck | Armor |
| Necklace | 0x1085 | necklace | Neck | Jewelry |
| NecklaceOfVillainousEpiphany | 0x4210 | gargish necklace | Neck | Armor |
| NecklaceOfVirtuousEpiphany | 0x4210 | gargish necklace | Neck | Armor |
| NecklaceofDiligence | 0x1F08 | necklace | Neck | Jewelry |
| OctopusNecklace | 0xA349 | octopus_necklace_hum | Neck | Jewelry |
| OldNecklace | 0x1085 | necklace | Neck | Jewelry |
| PaladinGorget | 0x1413 | platemail gorget | Neck | Armor |
| PendantOfKhalAnkur | 0xA1C9 | khal ankur gargish n | Neck | Armor |
| PendantOfTheMagi | 0x1088 | necklace | Neck | Jewelry |
| PhoenixGorget | 0x13D6 | studded gorget | Neck | Armor |
| PlateGorget | 0x1413 | platemail gorget | Neck | Armor |
| PlateMempo | 0x2779 | plate mempo | Neck | Armor |
| RangerGorget | 0x13D6 | studded gorget | Neck | Armor |
| RoyalZooLeatherGorget | 0x13C7 | leather gorget | Neck | Armor |
| RoyalZooPlateGorget | 0x1413 | platemail gorget | Neck | Armor |
| RoyalZooStuddedGorget | 0x13D6 | studded gorget | Neck | Armor |
| ScoutGorget | 0x13D6 | studded gorget | Neck | Armor |
| SilverBeadNecklace | 0x1F05 | bead necklace | Neck | Jewelry |
| SilverNecklace | 0x1F08 | necklace | Neck | Jewelry |
| SlimsShadowVeil | 0x277A | leather mempo | Neck | Armor |
| SorcererGorget | 0x13C7 | leather gorget | Neck | Armor |
| StuddedGorget | 0x13D6 | studded gorget | Neck | Armor |
| StuddedMempo | 0x279D | studded mempo | Neck | Armor |
| TigerPeltCollar | 0x7829 | collar_tiger_h | Neck | Armor |
| TorcOfTheGuardians | 0x1088 | necklace | Neck | Jewelry |
| VesperCollectionNecklace | 0x1088 | necklace | Neck | Jewelry |
| VirtuososCollar | 0x13C7 | leather gorget | Neck | Armor |
| VirtuososNecklace | 0x4210 | gargish necklace | Neck | Armor |
| VoiceOfTheFallenKing | 0x13C7 | leather gorget | Neck | Armor |
| WoodlandGorget | 0x2B69 | elven plate gorget | Neck | Armor |
| BarreraaksRing | 0x108A | ring | Ring | Jewelry |
| BlueDiamondRing | 0x108A | ring | Ring | Jewelry |
| BrassRing | 0x108A | ring | Ring | Jewelry |
| BurningAmber | 0x108A | ring | Ring | Jewelry |
| CompassionsEye | 0x108A | ring | Ring | Jewelry |
| CrystallineRing | 0x108A | ring | Ring | Jewelry |
| DemonBridleRing | 0x108A | ring | Ring | Jewelry |
| DjinnisRing | 0x1F09 | ring | Ring | Jewelry |
| EcruCitrineRing | 0x108A | ring | Ring | Jewelry |
| EssenceOfBattle | 0x108A | ring | Ring | Jewelry |
| EtoileBleue | 0x108A | ring | Ring | Jewelry |
| GargishRing | 0x4212 | gargish ring | Ring | Jewelry |
| GoldRing | 0x108A | ring | Ring | Jewelry |
| KatalkotlsRing | 0x1F09 | ring | Ring | Jewelry |
| LuneRouge | 0x108A | ring | Ring | Jewelry |
| OldRing | 0x108A | ring | Ring | Jewelry |
| PerfectEmeraldRing | 0x108A | ring | Ring | Jewelry |
| PetrifiedMatriarchsTongue | 0x108A | ring | Ring | Jewelry |
| ReginasRing | 0x1F09 | ring | Ring | Jewelry |
| RingOfTheElements | 0x108A | ring | Ring | Jewelry |
| RingOfTheSavant | 0x108A | ring | Ring | Jewelry |
| RingOfTheSoulbinder | 0x1F09 | ring | Ring | Jewelry |
| RingOfTheVile | 0x108A | ring | Ring | Jewelry |
| SilverRing | 0x1F09 | ring | Ring | Jewelry |
| StaffRing | 0x108A | ring | Ring | Jewelry |
| SterlingSilverRing | 0x1F09 | ring | Ring | Jewelry |
| StolenRing | 0x1F09 | ring | Ring | Jewelry |
| TurqouiseRing | 0x108A | ring | Ring | Jewelry |
| VesperCollectionRing | 0x108A | ring | Ring | Jewelry |
| BaseFormTalisman | 0x2F59 | talisman | Talisman | Other |
| BaseTalisman | 0x-001 |  | Talisman | Other |
| BirdsofBritanniaTalisman | 0x2F5A | talisman | Talisman | Other |
| BloodwoodSpirit | 0x2F5A | talisman | Talisman | Other |
| ConjurersTrinket | 0x2F58 | talisman | Talisman | Other |
| CuSidheFormTalisman | 0x2F59 | talisman | Talisman | Other |
| DuelistsEdge | 0x2F58 | talisman | Talisman | Other |
| EnchantressCameo | 0x2F5B | talisman | Talisman | Other |
| FerretFormTalisman | 0x2F59 | talisman | Talisman | Other |
| FriendOfTheLibraryToken | 0x2F58 | talisman | Talisman | Other |
| FrostguardTalisman | 0x2F5B | talisman | Talisman | Other |
| GrammarOfOrchishTalisman | 0x2F59 | talisman | Talisman | Other |
| GuaranteedSpellbookImprovementTalisman | 0x9E28 | talisman_inscription | Talisman | Other |
| JadeSnakeHead | 0x2F59 | talisman | Talisman | Other |
| LuckyCharm | 0x2F5B | talisman | Talisman | Other |
| ManaPhasingOrb | 0x1096 | talisman | Talisman | Other |
| MasterCraftsmanTalisman | 0x-001 |  | Talisman | Other |
| MyBookTalisman | 0x2F5A | talisman | Talisman | Other |
| MysticsMemento | 0x2F5B | talisman | Talisman | Other |
| NecromancersPhylactery | 0x2F5A | talisman | Talisman | Other |
| PrimerOnArmsTalisman | 0x2F59 | talisman | Talisman | Other |
| RandomTalisman | 0x-001 |  | Talisman | Other |
| ReptalonFormTalisman | 0x2F59 | talisman | Talisman | Other |
| Slither | 0x2F5B | talisman | Talisman | Other |
| SmeltersTalisman | 0x2F5B | talisman | Talisman | Other |
| SoldiersMedal | 0x2F5B | talisman | Talisman | Other |
| SquirrelFormTalisman | 0x2F59 | talisman | Talisman | Other |
| TalismanofGoblinSlaying | 0x2F58 | talisman | Talisman | Other |
| TalkingtoWispsTalisman | 0x2F5B | talisman | Talisman | Other |
| TheLifeOfTravelingMinstrelTalisman | 0x2F5B | talisman | Talisman | Other |
| TotemOfTheTribe | 0x2F5A | talisman | Talisman | Other |
| TotemOfVoid | 0x2F5B | talisman | Talisman | Other |
| TreatiseonAlchemyTalisman | 0x2F58 | talisman | Talisman | Other |
| WizardsCurio | 0x2F58 | talisman | Talisman | Other |
| WoodsmansTalisman | 0x2F5A | talisman | Talisman | Other |
| CrimsonDaggerBelt | 0xA40E | belt_dagger | Waist | Clothing |
| CrimsonMaceBelt | 0xA40C | belt_mace | Waist | Clothing |
| CrimsonSwordBelt | 0xA40D | belt_sword | Waist | Clothing |
| DaggerBelt | 0xA40E | belt_dagger | Waist | Clothing |
| FirstAidBelt | 0xA1F6 | first aid belt | Waist | Container |
| GargoyleTailFemale | 0x44C1 | cannonballfire south | Waist | Clothing |
| GargoyleTailMale | 0x450D | gargoyle belt south | Waist | Clothing |
| KhaldunFirstAidBelt | 0xA1F6 | first aid belt | Waist | Container |
| LeatherNinjaBelt | 0x2790 | leather ninja belt | Waist | Clothing |
| LeatherNinjaBeltBearingTheCrestOfBlackthorn1 | 0x2790 | leather ninja belt | Waist | Clothing |
| LeatherNinjaBeltBearingTheCrestOfBlackthorn2 | 0x2790 | leather ninja belt | Waist | Clothing |
| MaceBelt | 0xA40C | belt_mace | Waist | Clothing |
| Obi | 0x27A0 | obi  | Waist | Clothing |
| ObiBearingTheCrestOfBlackthorn1 | 0x27A0 | obi  | Waist | Clothing |
| ObiBearingTheCrestOfBlackthorn2 | 0x27A0 | obi  | Waist | Clothing |
| ObiDiEnse | 0x27A0 | obi  | Waist | Clothing |
| OzymandiasObi | 0x27A0 | obi  | Waist | Clothing |
| SwordBelt | 0xA40D | belt_sword | Waist | Clothing |
| WoodlandBelt | 0x2B68 | elven plate belt | Waist | Clothing |
| WoodlandBeltBearingTheCrestOfBlackthorn1 | 0x2B68 | elven plate belt | Waist | Clothing |
| WoodlandBeltBearingTheCrestOfBlackthorn2 | 0x2B68 | elven plate belt | Waist | Clothing |

**Worn gear drawn only as a stand-in:** 761 kinds on drawn layers, plus 10 waist items with `apron` in the name. They show the nearest Sidekick outfit of the same material class, not their own look. Which specific shapes have no Sidekick equivalent can only be seen in the game, not from files.

### 3.4 Items not worn and not held
3560 kinds have no layer (containers, resources, food, scrolls, potions, tools, gems, decorations, ...): drawn only as the pouch on the ground; UI icons are outside this audit. See the Category table in 3.1.

## 4. Doors, boats, houses, effects and other placeholders

### 4.1 Doors
Door leaves exist: `DoorViews` hangs the Synty `SM_Bld_Base_Door_01` leaf (present in `Assets/Synty/PolygonGeneric`) and `ChunkCollision` adds an invisible swinging box collider to it, so **doors are not boxes**. Door-like statics that are placeholders in the Trammel census: `0x0F6C blue moongate` (2) and `0x1EEA/0x1EE5/0x1EEC magical door` (4). All wooden, barred-metal, iron-gate, secret and generic door tiles render through the wall kit. TODO.md (2026-11-10) says doors still feel wrong to use (pick and reach): that is behaviour, not art.

### 4.2 Boats and multis
`Data/BoatCatalog.asset` has 12 boat entries, all with a prefab (small/medium/large and dragon variants, orcish/gargish/tokuno galleon, rowboat, britannian ship, pumpkin rowboat; four headings each). Whether the shard can create a boat multi outside these 12 types cannot be told from files. TODO.md still says "Houses and boats (multi items): not rendered at all yet", but later entries (multi.mul reader, house detector/generator) say houses are built; I did not check this in the editor, so treat that TODO line as stale.

### 4.3 House pieces (player housing)
`Content/Housing.json` references 18 distinct kit prefabs across its 19 pieces. All 18 exist under `Assets/Synty/PolygonFantasyKingdom/Prefabs/Buildings/House/` and all 18 are in `Assets/UOClient/Game/HouseKit.asset` (checked by GUID). **No housing piece is missing a prefab.** The 5 wall and 3 roof material names were not individually resolved against the kit's material list.

### 4.4 Effects
Particle/VFX use exists in `GrassField`, `HudController`, `SyntyBuildingGenerator`, `WorldView`, `Pickable` and the bake (candle and fire flames). TODO.md says only effect prefabs found by name (candle flames) survive the bake. I found no file that maps `Spells.json` (64 spells) or combat events to effect assets, so **spell, combat and ambient effects cannot be audited from files**; check in the editor (`Docs/VFX_Authoring.md`).

### 4.5 Other placeholders found while auditing
- Corpses use `ActorModel`, so a modelless creature's corpse now also shows as a lying capsule.
- The legacy protocol viewer `Scripts/World/EntityViews.cs` still draws unmatched items as gold cubes and creatures as notoriety-coloured capsules. It is not the game path.
- From TODO.md, not re-verified: floors (wood, carpet, pavers) as flat coloured tiles, unpaved town land, generated-house entry steps and UO furniture inside houses as boxes, 3x3 brick corner towers drawn from UO tiles. The census in section 2 is the authoritative current list for statics.

## 5. How to regenerate
- Creatures: decode `CreatureCatalog.asset` (`Bodies:` is packed little-endian ushort hex, e.g. `e100t` = 225) and compare with `Creatures.json` bodies; for kinds without `Body`, read `ServUO/Scripts/<File>` for `Body = ...;`. Spawn counts come from `Content/Spawns.json`.
- Statics: re-run the world bake (writes `WorldData/<facet>/census-*.csv`), then sort `PLACEHOLDER BOX` / `NOT DRAWN (no shape)` rows by `count`.
- Items: join `Items.json` (Layer, Graphic) with `tiledata.mul` names, apply the `Weapons` name rules from `SidekickWardrobe.asset` and the drawn-layer list in `SidekickDresser.Dress`.
- The Python used is in the session scratchpad (`c2.py`, `c3.py`, `i.py`, `gen.py`); ask Claude to re-run it.

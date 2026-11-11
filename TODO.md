# UnityClient TODO

Work flagged as "not done" along the way. Check items off when they're finished and verified.

## Server rewrite (top priority, started 2026-11-09)
All ServUO-protocol client work is paused. Design: Docs/ServerDesign.md (draft, open decisions at the end).
- [ ] **Base UO, in this order (user, 2026-11-10: "do it, in that order"):**
  1. Pets and taming: Animal Taming, followers and commands, stables, pack animals, mounts
     - [x] Part 1 (2026-11-10): `Pets.json` migrated (`export_servuo_pets.py`: taming, control formula, loyalty, bonding,
       food lists, followers, stables, UO's coaxing lines). Animal Taming (Use on the Skills tab, current target),
       3-4 coaxing ticks, skill check, skill scaling; orders (follow, stay, guard, attack my target, stop, release,
       rename, feed) checked against the control chance; loyalty by the hour, feeding, bonding (bonded pets stay as
       ghosts); followers cap; stables at animal trainers (stable, claim, buy their pets); pets saved with the
       character and back beside them on login. Pets window (P). Players may now attack wild animals, which fight
       back; townsfolk are protected. The creature export now follows forwarding constructors: 54 creatures gained
       their stats (every mount, elementals, blade spirits, energy vortex).
     - [x] Part 2 (2026-11-10): riding (Ride in the Pets window; the mount is carried, drawn under the rider, double
       speed as in UO, thrown off on death, still ridden after logging back in), pack animals' packs (drag items in
       and out like the bank, 1600 stones), dead bonded pets as ghosts, Veterinary (bandaging creatures in 2 s;
       resurrecting bonded pets at 80 Veterinary and 80 Animal Lore), Animal Lore (Use on the Skills tab: a creature's
       stats window, by UO's skill tiers). Fixed: pets' hits being recalculated with the player formula; dead
       targets now stay selected outside a fight (to bandage or resurrect them); no regeneration while dead.
     - [ ] Riding visuals need art: there is no horse model in the project (only in the SIMPLE packs, not imported)
       and no sitting animation, so a rider stands on the mount's model (horses: the placeholder capsule).
       Docs/game-riding-placeholder.png
  2. The rest of Magery: summons, fields/walls, Recall/Mark/Gate and runes, Dispel, Invisibility/Reveal,
     Polymorph/Incognito, Protection/Reflect, item spells
     - [x] 2026-11-10: 59 of 64 Magery spells cast. New: Reactive Armor, Protection, Arch Protection, Magic
       Reflection (resist shifts by element, kept on the combatant; Protection stops blows breaking a cast), Wall of
       Stone, Energy Field, Fire/Poison/Paralyze Field (networked `SpellObject` tiles: walls solid on both sides,
       fields hurt whoever stands in them), Recall, Mark, Gate Travel (recall runes remember a spot and name it from
       the region; casting asks which rune; Recall brings following pets; gates are two-way for 30 s), Summon
       Creature, the elementals, Summon Daemon (controlled, count as followers), Blade Spirits and Energy Vortex
       (fight the nearest monster), Dispel and Mass Dispel (UO's dispel chance), Dispel Field, Invisibility (unseen,
       untargetable; casting or attacking reveals), Reveal, Incognito (a stranger's name and face for a while),
       Polymorph (choose a form; cast again to return; throws you off a mount). Also: the unit frame holds the mana
       and stamina bars and grows to fit them (bigger bar text).
     - [ ] Still waiting: Magic Trap, Remove Trap, Magic Lock, Unlock, Telekinesis (need world containers with
       locks and traps); runebooks; travel rules by region (no recall in/out of dungeons etc.); Reflect/Protection
       and Reactive Armor don't persist through logout; Polymorph and Incognito end on logout; the fields, walls
       and gates are stand-in shapes and colours (need effect art); Protection's Magic Resist penalty and slower
       casting aren't applied
  3. Creature spellcasters; elemental damage and resists
     - [x] 2026-11-11: damage by element, as UO's AOS.Damage: every blow and spell splits its damage over physical,
       fire, cold, poison and energy (creatures' blows from their DamageTypes, e.g. a lich 10/40/50 physical/cold/
       energy; spells and fire fields from Spells.json "Types"), each share cut by that resist. Resists: armour sums
       all five for players (capped 70), creatures roll theirs from Creatures.json (capped 100); Reactive Armor,
       Protection and Magic Reflection shift them; poison ticks aren't resisted. The stats page and Animal Lore show
       all five (Lore also mana). Creatures now have mana (their Int unless set) and regain it by the players' formula.
       Mage creatures cast (230 kinds: AI_Mage/NecroMage/Spellweaving/Mystic with Magery), by ServUO MageAI's plain
       path, kept in Spells.json "CreatureCasting": cure when poisoned, dispel summoned foes, heal when hurt, else
       poison/bless/invisibility/curses/paralyze/mana drain or (most often) a damage spell as strong as their mana
       allows; they stand while casting, say the words, can be disturbed and can fizzle. Tamed casters cast for you.
       Invisibility no longer heals you (it used to reset the fight). Tested in Solo by code (an orcish mage cast
       Paralyze, Fireball, Explosion, Poison, Flame Strike; resist maths and caps checked).
     - [ ] Not done: SmartAI (combos, teleporting, meditating), Necromancy/Spellweaving/Mysticism spells for those
       AIs (they cast Magery for now), creatures fleeing at low health, special abilities (breath, etc.), the cast
       has no effect visuals for other players (only the spoken words)
  4. Active skills (Hiding, Stealth, Meditation, Anatomy, Arms Lore, Item ID, Detect Hidden, Snooping, Stealing,
     Lockpicking, Begging...) and Parry with a shield
     - [x] 2026-11-11: `SkillUseService`, rules in Skills.json "Uses" (ServUO's numbers; our own wording). Hiding
       (not while someone fighting you can see you; house bonus), Stealth (steps = Stealth / 5 tiles, running costs
       double and reveals when they run out, walking tries Stealth again; armour table; mounted or flying reveals),
       Meditation (trance doubles the meditation bonus to mana until you move, act, are hurt or are full; armour
       that doesn't allow it blocks it; hands must be free), Detect Hidden (around you or your target; reveals
       hidden players and creatures), Anatomy and Evaluating Intelligence (your target: tiered words, stamina/mana
       at 65/76), Arms Lore and Item Identification (Use asks which of your things: no cursor), Begging (a
       townsperson within 2 tiles, gold from their pocket), Stealing (both hands free; one random item from the
       creature you target, by UO's weight checks; caught: it turns on you and those near are told). Creatures now
       carry their loot from the first look (what's left goes on the corpse). Parry: shield (Parry/400, +5% at GM,
       less below 80 Dex, gains) or melee weapon (Parry/800); players, human-bodied creatures, GM-wrestling pets;
       a parried blow does nothing. UO's skill delays. Tested in Solo by code (each skill's result lines; parry
       rates 17.6% dagger / 12.0% shield at 21 Dex vs 17.5% / 12.3% expected).
     - [ ] Not done: Snooping (UO only lets you snoop players and pack animals, and players can't harm each
       other here, so there's nothing to snoop yet), Lockpicking and Remove Trap (need locked/trapped containers,
       with the container spells), karma and fame (Begging's bad-karma refusal, fame raising the take; stealing's
       criminal flag), monsters' special stealables, Tracking, Forensics, Spirit Speak, Taste ID, Poisoning, the
       bard skills (Peacemaking, Provocation, Discordance), Inscription; Item ID doesn't identify properties until
       magic items exist; Arms Lore has no durability lines until items wear
  5. Magic item properties on loot; names and text from the player's UO text files (cliloc)
     - [x] 2026-11-11: `Content/Magic.json` migrated by `Tools/Data/export_servuo_magic.py` (165 properties with
       imbuing weights, scales and per item type loot maxima and over-cap ranges, tooltip cliloc numbers, 11 slayer
       groups, jewel skills, loot budgets from Config/Loot.cfg). `MagicLoot` ports Runic Reforging's random item path:
       budget from the creature's fame (+ killer's luck), 1-5 weak or 4-10 strong properties, Scale(), weight-paid
       budget, validation (one hit spell / hit area / eater, two-handed only...), disadvantages (Prized, Antique,
       Brittle, Cursed... buying extra properties), item power (Minor Magic Item .. Legendary Artifact; legendary
       armour hue). Loot rolls it on every weapon, armour, shield, hat and jewel a creature drops or carries.
       Items keep properties (`Item.Props`), shown in tooltips in UO's words from the player's Cliloc.enu; item
       names now come from cliloc too. Worn effects: hit/defence chance (45 cap), damage increase (100), swing
       speed (60), slayers (x2 damage vs the group's creatures), weapon elemental damage, resist bonuses, stat and
       hits/stam/mana bonuses, hit/stam regeneration, mana regeneration (in the formula), spell damage, faster
       casting (2) and recovery, lower mana cost (40), skill bonuses (+N to the skill everywhere), use best weapon
       skill, mage weapon, mage armour (meditation and stealth), hit spells (Magic Arrow, Harm, Fireball,
       Lightning, Dispel), hit areas, life/mana/stamina leech, hit lower attack/defence, reflect physical, luck
       (better loot). Tested in Solo by code (rolled items per creature, worn gear stats, a fight with procs).
     - [ ] Not done: named prefixes/suffixes (Reforged "Vampiric ... of Doom" sets); effects without the systems
       they need: durability (self repair, durability bonus, brittle/antique), lower requirements, lower reagent
       cost and enhance potions (no reagent use from books, no potions), night sight (no darkness), spell
       channeling (casting isn't blocked by weapons yet), SA absorption (eaters, resonance, casting focus),
       reactive paralyze, soul charge, splintering, battle lust, hit curse/fatigue/mana drain, balanced, velocity;
       Blood Drinker excluded (no weapon abilities data); the shard's own ranged/spell properties (60-68) excluded
       with the other custom systems; magic items don't change hue by element; vendors don't price magic
  6. The other schools: Necromancy, Chivalry, Bushido, Ninjitsu, Spellweaving, Mysticism
     - [x] Part 1 (2026-11-11): `Tools/Data/export_servuo_schools.py` adds all six schools to Spells.json (73
       spells and moves: names, words, required skill, mana, tithing, reagents, cast time, scrolls; a Schools table
       with each book, skill, window, recovery and rules). Books per school (a book of Chivalry/Bushido/Ninjitsu
       comes full; Necromancy, Spellweaving and Mysticism books fill from scrolls: use a scroll to write it in).
       School casting: the school's skill from the requirement to +40 (+50 Chivalry), raw mana (Mind Rot scales it),
       Necromancy's reagents and karma cost, Chivalry's tithing, 4 faster casting and 7-quarter recovery and no
       disturbing. Necromancy: Animate Dead (the shard's version: raised dead are controlled, on Guard, max 3, no
       follower slots, heal slowly), Blood Oath, Corpse Skin, Curse Weapon, Evil Omen, Horrific Beast, Lich Form,
       Mind Rot, Pain Spike, Poison Strike, Strangle, Summon Familiar (choose; one at a time), Vampiric Embrace,
       Vengeful Spirit (a revenant hunts the target), Wither, Wraith Form. Chivalry: all 10 (power from karma and
       Chivalry). Fame and karma (kills award them, Necromancy costs karma; stats page), tithing points (Tithe on the
       Spells tab by an ankh; new Station.Ankh tile layer). Tested in Solo by code.
     - [x] Part 2 (2026-11-11): Bushido and Ninjitsu (Spells.json from the same exporter; Server/SpellService.Moves.cs
       hooked into every blow through Combatant.Moves). Moves readied from the book for the next blow (again puts it
       away; Readied on the Spells tab): Honorable Execution (kill: hits back and faster swings 20 s; fail: -40
       resists and no Magic Resist 7 s, a parry ends it), Lightning Strike (+50 hit chance, may ignore armour),
       Momentum Strike (carries into another foe), Focus Attack (more damage, stronger hit properties), Death Strike
       (direct damage after 5 s or 5 steps), Ki Attack (rush within 2 s), Surprise Attack and Backstab (from
       stealth). Stances, one at a time: Confidence (parries heal; regen), Evasion (parry x1.16-1.5, turns aside
       single-target spells, 20 s wait), Counter Attack (strike back after a parry). Animal Form (16 animals by
       Ninjitsu: their bodies, mount speed, +20 hits, regen, stealth/stealing bonus, unicorn cures poison, serpent
       and bull frog poison, cu sidhe bandages, reptalon breath; no other spells, moves or 13 skills in form),
       Mirror Image (a copy of you that a blow at you may land on), Shadowjump (from stealth, 11 tiles ahead,
       still hidden). Parry now uses Bushido as ServUO does (weapon: Parry x Bushido / 48000; shield: Parry -
       Bushido). Choosing a foe no longer reveals you; the first swing does. Tested in Solo by code.
     - [x] Part 3 (2026-11-11): Spellweaving and Mysticism (same exporter; Server/SpellService.Weaving.cs). Spellweaving:
       the arcane focus (Arcane Circle on a pentagram/arcane circle tile, new Station.ArcaneCircle layer, with another
       spellweaver beside you; strength up to 5 for hours, +that much strength) powers every spell; Gift of Renewal,
       Immolating Weapon, Attune Weapon, Thunderstorm, Nature's Fury, Summon Fey and Fiend (1 + focus), Reaper Form,
       Wildfire, Essence of Wind, Dryad Allure, Ethereal Voyage (creatures ignore you), Word of Death, Gift of Life
       (back to life after dying), Arcane Empowerment. Mysticism by circle like Magery (Boost = the better of Focus
       and Imbuing): Nether Bolt, Healing Stone (a stone you use), Purge, Enchant (choose a hit spell), Sleep and Mass
       Sleep, Eagle Strike, Animated Weapon and Rising Colossus (stats by level), Stone Form, Spell Trigger (a stone
       that casts its spell at once), Cleansing Winds, Bombard, Spell Plague, Hail Storm, Nether Cyclone. Chaos
       damage picks an element each time. Tested in Solo by code.
     - [ ] Not done in part 3: the epic arcanist quest that unlocks Spellweaving (quests, roadmap #8); Arcane Circle's
       bonus spots (strength 6) and circles placed as decorations (only map tiles count: 2 near Britain); Nature's
       Fury's growing damage; Dryad Allure doesn't empty the creature's pack; Arcane Empowerment's summon and dispel
       bonuses; walk-speed limits of Reaper and Stone Form; Spell Plague goes off on any harm, not only spells;
       Wildfire has no flames to see; form bodies 285/705/770 and the summons may lack models
     - [ ] Not done in part 2: Stalking bonus (Tracking) for Backstab, Surprise Attack and Death Strike; animal form
       hues; Lightning Strike's SA skill gain above 87.5; a clone's look doesn't change when you re-dress
     - [x] Bug found 2026-11-11: a player whose network object id is 0 had masterless pets. Fixed 2026-11-11:
       NpcMobile.MasterId holds the id + 1 (IsPetOf), and a target request uses -1 for nobody (protocol 25).
     - [ ] Not done in part 1: Exorcism (needs champion spawns); familiars' own abilities (Horde Minion looting, Vampire
       Bat drain...); Dispel Evil doesn't make evil creatures flee; Vampiric Embrace's garlic and 80-120 window;
       form bodies 746/747/748/749 have no models yet; fame/karma titles in names; karma from other deeds
  7. World: day and night, starting city choice, swimming and fall damage, boats
     - [x] Part 1 (2026-11-11): Content/World.json (WorldRules) with UO's clock (Clocks.cs: a day is 2 real hours,
       later eastward) and light levels (LightCycle: night 0-4, dawn to 6, dusk from 22; dungeons 26 from
       Regions.json DungeonRegion); the client's DayNight moves the sun, dims light, sky, ambient and haze, a moon
       at night; Night Sight now lights you as day for 15-25 min (PlayerIdentity.NightSight). Starting cities
       (Creation.json StartCities, the nine ServUO inns; "Begin in" on character creation). Swimming: WaterMap holds
       each loaded tile's water surface (from the chunk water meshes); deeper than 1.25 m you float head-out and move
       at 3.5/2 m/s. Fall damage: beyond 5 m, 4.5% of your most hits a metre (none into water or flying). Tested in
       Solo by code (screenshots at noon, dawn and 2 AM; swimming at 1488,626; a 15 m fall took 41 of 104; a new
       character began in Moonglow).
     - [x] User TODO (2026-11-11): a real night-time skybox and darkness. Done 2026-11-11 as a small sky-and-weather
       system of our own (user: "something simpler like Enviro 3"): Shaders/Sky.shader (day gradient, amber mornings
       and evenings, sun, stars that twinkle, a galaxy band, the moon in UO's eight-day phases, drifting clouds,
       lightning), DayNight.cs (a real sun path from World.json Sun: rises 6:00 in the east, crosses the south, sets
       20:00 in the west, then the moon; ambient light from the sky's colours so nights are dark; fog; rain and snow
       particles round the camera; lightning flashes) and World.json Weather (Clear, Cloudy, Overcast, Fog, Rain, Storm,
       Snow; each ten-minute period's weather picked from the clock alone, so everyone sees the same with nothing sent;
       Force holds one, a live change). Assets: Resources/Sky (Tools/Game/MakeSky.eval.cs). Seen in Solo by
       screenshots: noon, sunrise and sunset, midnight with stars and the moon, cloudy, storm with rain, fog, snow.
     - [ ] User TODO (2026-11-11): dynamic cloud cover like Enviro's: volumetric-looking clouds that build up, thin
       out and move with the weather (today's clouds are one flat noise layer in the sky shader whose cover just
       fades between weather kinds).
     - [x] Hard shine on roofs and trees (user, 2026-11-11, also in shadows): shiny surfaces reflected the bright
       daytime sky the scene was made with, day and night. DayNight now re-makes the reflection from the sky as it
       changes and scales it with daylight (0.5 by day, 0.06 at night, less underground and under cloud). Night
       screenshots before and after.
     - [x] World lights (2026-11-11): WorldLights lights the baked world's street lamps, wall torches, candles,
       braziers, lanterns and campfires at night (World.json Lights: which props by name, range, strength, flicker),
       as each chunk loads; only the 24 nearest the camera shine at once, all fading out by day. Britain: 168 lamps
       found, the nearest 24 lit (screenshot by a street lamp at night).
     - [x] Carried lights (2026-11-11): a held torch, lantern or candle (or a lit helm: Items.json "Light" CircleN, now
       ItemDef.LightRadius) lights its bearer's surroundings for every client from the synced look: a warm point light
       at the off hand, 5/7.5/10 m, flickering for flames, strong in the dark and faint by day (DayNight.Daylight).
       Also fixed: a host's own character now redraws when its gear changes. Tested in Solo (night screenshots with and
       without a torch; a lantern on and off).
     - [ ] Not done in part 1: a swimming pose; creatures walk the sea bed instead of swimming; dungeons still show
       the outdoor sky; the held torch itself isn't drawn, and UO's lighting by double-click and burning out aren't
       in; lamps placed by players (items) don't light yet; no clock on screen; some foliage shaders
       ignore the darkness (orange trees stay bright); Royal City (Ter Mur isn't in the world)
     - [x] Part 2 (2026-11-11): boats, a basic first version. Content/Boats.json (`Tools/Data/export_servuo_boats.py`:
       11 ships, deeds and docked items, footprints from the UO multi data, BaseBoat speeds, a Sailing block). A deed or
       docked ship from the backpack shows a ghost hull on the water under the mouse (BuildMode.Ships), green where it
       fits (all water, dry land within 6 tiles, within 30 tiles of you; Boat.CheckPlace, checked again by the server)
       and red where not; Rotate turns it, a click places it. The owner takes the helm from the ship window and steers
       freely with the movement keys (forward speeds up to 5 m/s, back slows and goes astern, left/right turn at any
       angle; smaller ships turn faster); the course (position, yaw, speed, turn rate, tick) is synced and both sides
       place the hull from it as an arc. Those on deck ride it as if parented (PlayerMotor moves and turns them by the
       deck's move each tick) and can still walk about; their legs animate only for their own steps. The hull floats
       with its inside floor just above the water. Ships stop short of land and other ships; anyone may board or go
       ashore; the owner can drop anchor or dry-dock. PlayerIdentity.CharacterId is now synced. Tested in Solo by code
       (ghost screenshots; placing, boarding, accelerating, cruising, turning, stopping with the passenger holding
       their spot on deck; going ashore; dry docking; the ship stayed across a restart).
     - [ ] User TODO (2026-11-11): Valheim-type sailing (sails and wind, rudder, momentum and drift, waves and
       buoyancy, ships rocking).
     - [x] User TODO (2026-11-11): poses aboard. Done 2026-11-11: PlayerIdentity.Pose (standing, sitting, steering).
       Taking the helm puts you at the stern (found by walking the deck back to the end of the hull), facing the bow,
       legs held still; "Sit down" / "Stand up" in the ship window for anyone aboard, and a movement key stands you up;
       the seated pose is our own humanoid clip from muscle values (Resources/Poses/Sit, Tools/Game/MakePoses.eval.cs)
       played over the animator; standing passengers on a moving deck no longer start walking from small drift. Tested
       in Solo: helm at 4.25 m aft, held there with the walk at 0 through a full turn at 5 m/s; seated pose (screenshot);
       W stood the player up.
     - [ ] Poses still to do: a sitting spot per ship (benches), poses per ship type (a big ship's wheel), the helmsman's
       hands on the tiller (an arm pose over the idle), sitting on land (chairs, benches, the ground).
     - [ ] Not done in part 2: the hold, tillerman, planks and keys (anyone may board; only the owner steers); a
       passenger standing on deck while the ship speeds up may still be corrected a little over the network (watch
       for it with a real second client);
       co-owners and friends; renaming; galleons' cannons and damage; ship decay; NPC and pirate ships; the ship
       window not yet seen by hand; creatures at sea
  8. Quests and bulk orders
     - [x] Part 1 (2026-11-11): bulk orders. Content/BulkOrders.json (`Tools/Data/export_servuo_bulkorders.py`: all
       eight crafts, ServUO's newest points system: item lists and large sets, amounts, material and exceptional
       chances, points, fame and gold formulas and tables, 179 rewards resolved to their items, the vendors). Talk to a
       craftsman of the trade: "Ask for a bulk order" (an offer to accept or decline; two waiting, one more every six
       hours), use the deed to see it and "Add items from backpack" (the right kind, exceptional and material when
       asked; a large order also takes filled small ones), "Turn in bulk orders" (gold into the bank, fame, then spend
       the points on a reward now or bank a share of them), "Bulk order rewards". Crafted items now remember being
       exceptional and their material (AccountStore.Item.Exceptional, Resource). Tested in Solo by code (offer, fill,
       turn in for 130 gold and a smith's hammer; exceptional shadow-iron matching; a large order from three small
       ones paying 4,182 gold and +36 fame, its 310 points banked as 62).
     - [ ] Not done in part 1: rewards are plain items (runic hammers, talismans, powder of temperament, colored anvils
       and the rest don't do their special things yet); bulk order books; offers when selling to a vendor (20%);
       vendor bribes; the exceptional crafted bonuses (AOS) that make exceptional gear better
     - [x] Part 2 (2026-11-11): quests. Content/Quests.json (`Tools/Data/export_servuo_quests.py`: ServUO's Mondain's
       Legacy quests, 408 quests and 211 quest-givers with their texts as cliloc numbers, objectives, rewards, chains,
       restart rules, offer conditions, looks and where they stand). A quest-giver's "Quests" option: an offer (one of
       its quests you may take, at random) to accept or refuse, how far you are, or the quest done with its rewards and
       the chain's next quest offered. Objectives: slay (your or your pets' kills, inside the region when it says),
       obtain (taken from your backpack when you return), deliver (given to you at the start, handed to the named
       quest-giver) and the New Haven apprentice training (reach the skill). Rewards: items and gold. A Quests tab on
       the character window lists your quests with progress and Resign. Tested in Solo by code (Marauders: kills
       outside Sanctuary didn't count, ten inside did, its reward and the next quest; A Broken Vase: progress at 6,
       done at 12 taking 10; Aemaeth: the basin given, delivered to Szandor, the chain's second quest offered).
     - [ ] Not done in part 2: escort, question-and-answer and the 33 custom objectives (their quests aren't offered:
       ~65 objectives); text, flag (e.g. Spellweaving unlock), mastery and recipe rewards do nothing; 24 quest-givers
       ServUO places that our spawns don't (Eodon, High Seas, Town Cryer); "any blood creature" slay targets; the old
       story quests (Haven, Solen, ninja/samurai, Dark Tides); quest items given by OnAccept code; allegiance checks
  9. Admin tools (as the user specified: an API/MCP for AI agents; as few restarts as possible for changes an AI
     makes; Admin its own window from the skill book, hotbar-draggable; admin accounts are player accounts that switch
     to admin mode: invisible, untargetable, unkillable). Docs/AdminTools.md.
     - [x] 2026-11-11: AdminService. Accounts gain `Admin` (granted only through the API). Admin mode
       (Combatant.AdminMode): not drawn for players (admins see each other, you see yourself as a ghost), never
       picked, fought or harmed (no damage, poison, paralysis or Kill); entering it drops every fight with you. The
       skill book's Admin section (Admin tools, Admin mode), both draggable onto the hotbar. The Admin window: admin
       mode, players online (go to, bring, kick), go to coordinates / a starting city / a player (ground found, or
       waited for while it loads, landing without fall damage), heal or resurrect / kill / remove your target,
       search and spawn creatures, search and create items, announce, save. Every admin action logged.
     - [x] The admin API (AdminService.Api.cs): 127.0.0.1:7771, one JSON line each way, token in
       ServerData/admin-token.txt; status, players, accounts, account.admin, broadcast, kick, teleport, give, spawn,
       kill, heal, skill, save, shutdown (warnings, save, exit), content.list/get/set/put/revert.
     - [x] Live content (Content/LiveContent.cs): Creatures, Magic, Shops, Harvest, Crafting, Spells, Pets, Boats,
       BulkOrders, Quests and World change with no restart and no client update: loaded at once (a breaking change is
       refused), kept in ServerData/Content, sent gzipped in parts to every client and to clients as they log in.
       Creatures update in place (kind numbers kept, new ones appended).
     - [x] The MCP server Tools/GameMcp/game-mcp.mjs (no dependencies; ssh relay to phoenix-server or local): 24
       tools, including game_restart (warn, save, stop, start) and game_content_pull (a live change into the
       project). The deploy now asks the server to save and stop before replacing it.
     - [x] Tested in Solo by code through the MCP server (local): status, accounts, admin granted (persisted across a
       restart), admin mode (orcs ignored the admin; turned off, they attacked; on again, they let go and hits came
       back), spawn, live Orc hits 58-72 -> 500-520 (a new orc had 508, old ones kept theirs) and revert, a broken
       change refused, a Quests patch round trip (4 parts, 183 KB), teleport to Moonglow, give, skill, broadcast,
       shutdown planned and called off, shutdown now (saved and stopped), the hotbar's Admin mode both ways, the
       window's layout (screenshots).
     - [ ] Not done: Items, Spawns, Names, Outfits, Creation, Skills, LootPacks, Housing and Regions can't change live
       yet; hot code changes (new code still needs a build and a restart); in-game account management (granting admin
       is API-only); an admin "invisible" look for other admins beyond the normal model; jail, mute and ban; editing a
       creature's stats or an item from the window (the API's content tools do it).
- [ ] **1.0 is a general release (user, 2026-11-11): Base UO plus SeriousFun's players, no shard custom systems.**
- [~] Playtest (2026-11-11, Docs/Playtest.md): patch host on phoenix-server (IIS, port 7780, C:\GameServers\Patches);
  playtest client 0.1.0 (PlaytestBuilder: the Game scene as a player, IL2CPP, LZ4HC, Trammel's world; art NOT
  encrypted, friends only) published through the updater and installed end to end over HTTP (signed, verified, 52 s
  on the LAN); SeriousFun-Playtest.zip (GameUpdater + its channel) on the host. The client now finds the player's UO
  install itself and connects to seriousfun.tplinkdns.com by default.
  - [ ] User: firewall rule for TCP 7780 and the Deco forwards (TCP 7780, UDP 7770); then the playtest itself
  - [ ] Before any public release: finish secure art (Addressables, encrypted bundles) and stop shipping playtest
    builds
- [~] User TODO (2026-11-11): NavMesh agents and updated spawn points.
  - [x] 2026-11-11: Server/NavMeshService builds one navmesh at run time from the loaded chunks' collision
    (BakedWorldView.ChunkReady/ChunkDropped; async, only changed tiles rebuilt; a person-sized walker, 0.6 m steps,
    45 degree slopes, 0.2 m voxels). Every creature gets a NavMeshAgent that plans (and avoids other creatures) while
    its character controller moves it; straight-line walking stays as the fallback off the navmesh. Spawns now land on
    the surface nearest the spawn point's own height (not roofs or treetops) and onto the navmesh. Tested in Solo:
    Britain's 58 creatures all on the navmesh with agents; an orc set on the player round an obstacle went round it
    (a route 1.4x the straight line) and reached and fought them in about 4 s.
  - [x] Spawn points in the 3D world (user, 2026-11-11: NPCs on roofs, blacksmiths stuck on the wall outside their
    shops; "they should be in their damn shops"): each point is pinned once to the walkable floor nearest its own spot
    and height on the navmesh, and remembers if that's indoors; creatures appear only where they can walk to from it
    within the point's range (never the far side of a wall, a roof or a rock), under the roof when the point is
    indoors; strolls go only where a short path reaches (a shopkeeper stays in the shop). Points with no navmesh (sea,
    piers) fall back after a few tries. Tested in Britain: 56 creatures, none on a roof or wall top; 29 of 36
    shopkeepers indoors, the other 7 the docks' fishers and a beggar.
  - [x] Doors and open ground (2026-11-11): the navmesh now includes each chunk's terrain (it sits beside the chunk's
    root, so bare ground, streets and fields had no navmesh: only paving and floors did); door leaves are left out and
    each doorway gets a navmesh link from the floor on one side to the other, made once the navmesh round it is built;
    a creature walking the link opens the door (WorldServices.CreatureOpens, as a player's use: everyone sees it, it
    shuts after 4 s; never a player house's door). The walker is 0.3 m wide (0.15 m voxels). Tested in Britain: 109 of
    127 doors linked, 12 of 13 doorways walked straight through, all 59 creatures on the navmesh; an orc outside a
    closed shop door opened it, came in and fought the player; builds 0.6 s over 51 chunks.
  - [ ] Still to do: one agent size for every creature (a dragon plans like a person); creatures
    standing off the navmesh (a harbour master on a pier) walk straight; the spawn points themselves are still
    ServUO's (where, how many, which creatures): revising them for the 3D world is open (ask the user what they want).
- [~] Move SeriousFun's players over (Docs/ServUOImport.md)
  - [x] 2026-11-11: ServUO `[ExportForUnity` (ServUO/Scripts/Custom/ExportForUnity.cs) and the importer
    (Server/ServUOImport.cs, admin API `import.servuo`, MCP `game_import_servuo`, dry run by default,
    repeatable). Old passwords work (ServUO hashes checked once, then ours). First export from a copy of the live
    save: 6 accounts, 19 characters, 1,297 items, 16 pets, 1 house. Imported in Solo, checked, and the local
    accounts put back.
  - [x] Imported on the live game server, 2026-11-11 1:13 PM (accounts backed up first)
  - [x] General for any ServUO shard (user: anyone who downloads this imports their own server's data): the exporter
    ships in Tools/ServUOExport, the import reads a config (custom classes, base-type fallback, properties, skills,
    materials, maps, admin levels, houses, gold, pets, name clashes; import-config.example.json). Tested with a
    made-up custom shard.
  - [ ] At the cutover: a fresh export and `replace: true`, then SeriousFun closes (Phase 5)
  - [ ] Not carried: weapon elemental damage splits (our weapons don't split damage), negative item tags, a feed
    trough addon, loose house items, house friends
- [x] Settle the open decisions in ServerDesign.md (2026-11-09: import players, bake all facets, players import from their UO install, rewrite not port, jump/swim/fall damage)
- [ ] Step 1: bake the generated world to chunk files; client renders from the files
  - [x] Baker, chunk format, palette, verifier, census, baked-world viewer (`Scripts/World/Bake`, 2026-11-09). Britain
        test (56 chunks, Trammel): 89,562 objects round-trip with 0 differences.
  - [x] Determinism: same files whatever the bake order. Fixed: stair flights owned by the first-built block,
        house tile sets in scan order, deferred collider removal, seat facing remembering map tables. Britain 56
        chunks byte-identical in order vs shuffled (2026-11-09)
  - [x] Houses from footprint only: one cell per tile, every UO wall/window/door/roof/floor/stair tile of a house
        claimed, UO doorways become kit door modules with working BuildingDoors (2026-11-09; Britain: 79 doors)
  - [ ] Interiors are open plan (NoPartitions): decide whether the generator should add its own rooms
  - [ ] Clean up dead per-tile house code: RoofPlanner.FitWall gable steepening for house walls, DoorViews
        AddDoorways/leaf binding, RoofWings.cs, NearestEntryHalf
  - [x] Scale: 1 tile = 2.5 m (`WorldSpace.TileSize`, `TileScale` = 2 for constants tuned at 1.25 m); no
        squashing in houses or the custom-house plinth; roof pitch, rocky-slope angles and door reach carried over;
        stair flights take a 35 degree pitch against their top step (2026-11-09)
  - [ ] At 2.5 m tiles: ground blending looks smeared (orange blotches), ground cover and trees are sparser per m2,
        props sized "per tile" (tables stretched over runs, Block fits) need a look
  - [x] UO "wooden signpost" (0x0B98) is a shop's wall bracket, not a ground post: new `PropFit.Bracket` mounts the
        Synty sign bracket on the wall beside its tile, arm out, just under the wall top (was standing in the ground
        at 1515,1552) (2026-11-09). The sign hanging from it is a decoration item: comes with the decoration bake
  - [ ] Black hole in the terrain at the bottom left of the Britain overview (Docs/bake-view.png): a void tile or a
        missing chunk
  - [ ] Bake ServUO decorations (`ServUO/Data/Decoration`) into the map as static objects with stable IDs (doors and
        other stateful ones get state the server can change)
  - [ ] Bake every facet (0-5) and write the census for each
  - [ ] Static catalog: go through EVERY graphic the census reports as placeholder box or not drawn, identify what
        it is, and decide: model it, paint it into the floor, or (rarely, on purpose) keep it as an explicit
        invisible blocker. Britain alone: 539 graphics, 21,245 boxes, about 780 not drawn (biggest groups: raised
        pavers and cobblestones, carpets, invisible "no draw" blockers 0x21A4, nameless carpet parts 0x0AD2-0x0AFA)
  - [x] Look at the baked world (Tools/Bake/ViewBaked.eval.cs + Snap.eval.cs): Britain loads and draws, 9 chunks,
        doors open with E (Docs/bake-view.png, bake-door.png) (2026-11-09)
  - [x] E opens doors: the door nearest the line under the cursor within 4 m opens, and shuts itself after 4 s
        (stands in for the server's open/close triggers) (2026-11-09)
  - [ ] Particle effects: only effect prefabs found by name are kept (candle flames); check others in a full bake
  - [ ] Collision for the server: vegetation and props lose their prefab colliders today (client-only camera use);
        the server needs trunks, rocks and furniture to collide
- [x] Full bake of every facet (2026-11-09): 18,271 chunks, 10M objects, about 1 GB in WorldData/, censuses
      per facet (880k placeholder boxes, 72k statics not drawn, 1,000-3,400 problem graphics per facet)
- [x] FishNet 4.7.4 installed (release package; the git UPM path ships Pro-only code that doesn't compile)
- [~] Phase 1, first playtest (Docs/ServerRoadmap.md):
  - [x] Map collision on chunk load, same on server and client (`ChunkCollision`): merged solid mesh per
        chunk, tree trunk capsules, plants and tiny clutter skipped, door leaves get swinging boxes; palette models
        made Read/Write (`Tools/Bake/MakePaletteReadable.eval.cs`)
  - [x] Game scene (`Scenes/Game.unity`, built by `Tools/Game/MakeGameScene.eval.cs`): FishNet network manager
        (Tugboat, port 7770), player spawner at Britain, baked world following every player, orbit camera, menu
  - [x] `UOGame` assembly: `PlayerMotor` (predicted, server-authoritative free movement: run default, Shift walk,
        Space jump, gravity), `PlayerAppearance` (Synty model, walk/run animation from real speed), `GameCamera`,
        `GameWorld`, `GameBoot` (Solo / Host / Join; `-server [-port N]` headless)
  - [x] Playtested solo by the user (2026-11-10): "It works". They found a few issues, deferred and not listed yet
  - [x] Handshake (protocol version + content hash) and accounts (`GameAuthenticator`, `AccountStore`: salted
        PBKDF2, auto-created on first login, ServerData/accounts.json); character create/select (`CharacterService`),
        spawn where the character last stood, saved on leave and every minute; names over heads (2026-11-10)
  - [x] Doors through the server (`WorldServices`): use by stable ID, server checks reach, opens its own copy
        (collision swings) and tells clients within 120 m, shuts 4 s later; chat (say, 40 m earshot) with bubbles and
        a chat log (`GameHud`); tested end to end in solo by code (2026-11-10)
  - [x] Headless server on phoenix-server (2026-11-10): `Tools/Game/BuildServer.eval.cs` (Windows Server
        subtarget; the release guard lets server builds through), installed to `C:\Games\UOGameServer` with
        WorldData/1, scheduled task `UOGameServer` (at startup, SYSTEM, restarts on failure), UDP 7770, log
        `C:\Games\UOGameServer\server.log`. ServUO keeps TCP 2593/2594
  - [x] Inbound UDP 7770 firewall rule on phoenix-server (added by the user, 2026-11-10)
  - [x] First remote join to the headless server from the user's PC (2026-11-10): account created, character
        created and entered the world (server log)
  - [ ] Headless server: menu/HUD panels still start and log a font exception (no shaders in server builds); skip
        UI in batch mode
  - [ ] Client builds of the Game scene: the release pipeline only protects WorldView's art; WorldContent,
        WorldPalette and the player prefab need the same content-pack protection before a client ships
  - [x] Scale back to 1 tile = 1.25 m, houses from TRUE-SIZE pieces (2026-11-10): plans on a 1.25 m unit grid
        (`SyntyKit.Unit`), walls = 2.5 m pieces + one 1.25 m half at a corner, greedy floor slabs, 2x2-unit stair voids,
        half roof bays, kit doors centred on UO doorways; no stretch or squash anywhere (Docs/HalfModulePlan.md,
        before/after pictures in Docs/HalfModule/). Gables span the SHORT side (user: "the large gables look terrible");
        odd spans use a centred 0.625 m eave overhang. Verified 0 differences and order-independent bakes
  - [ ] Half wall pieces per set: Plaster/Brick `Half_01` shows a short top-rail stub; PlasterBeams' half is a small
        window; Wooden's half is a plaster mesh (looks OK). Single UO doors get a 2.5 m kit door 0.625 m off-centre
  - [ ] (old) Scale, user (2026-11-10): "I'm not sure I like the doubling of the map." Go back to 1 tile = 1.25 m (1x),
        and keep each house's total size in those units, but build walls from properly sized pieces that add up to it,
        never squashed halves. For example: a wall run's length / 2.5 m, rounded, gives the number of whole Synty
        modules, each stretched evenly by a few percent to fit exactly; door and window modules the same way; floors,
        plinths and roofs to match.
        - Footprint-only houses stay.
        - Undo the 2.5 m compensations: `WorldSpace.TileScale` (roof pitch, rocky slopes, door reach), stair pitch.
        - Re-bake every facet afterwards.
  - [ ] Doors, user feedback (2026-11-10): "Doors are weird, but work". The user often had to press E several times
        before a door opened.
        - Show when a door is targeted: highlight it and/or an "E: Open" prompt while it's the one E would use.
        - Likely causes of the misses: both client pick (`GameHud.UseDoor`) and server reach check
          (`WorldServices.Distance`) measure from the FIRST renderer's bounds (the wall panel, not the leaf); 4 m
          client reach vs 4.5 m server reach measured from different points; 1.6 m off-ray tolerance. Measure both from
          the leaf's middle and give the server a little slack.
        - The user describes it only as "they work weird"; recheck how doors look and feel after the pick/reach fix.
  - [x] Doors: one measuring point (`BuildingDoor.OpeningCentre`) for the client pick and the server reach check
        (server allows 1 m more), "E  Open" prompt on the door E would use (2026-11-10)
  - [x] Content import, phase 2 part 1 (2026-11-10): `Tools/Data/export_servuo_creatures.py` (1,324 creature/NPC
        types incl. 412 vendors with shop lists), `export_servuo_spawns.py` (6,788 XmlSpawner points, 96% of
        entries resolve to creatures; the rest are camps/chests/quest props), names.xml -> Content/Names.json
  - [ ] Content import still to do: items (weapons, armour, properties), loot packs, vendor shop lists (SBInfo),
        quests; UO client data (tiledata names, cliloc) through the player's import
  - [x] Mobiles (2026-11-10): `SpawnService` wakes spawn points within 100 m of a player, fills them, refills on their
        delay, sleeps them after 60 s unvisited; `NpcMobile` (server AI: wander, hostile aggro/chase/leash),
        `NpcAppearance` (creature catalog by body, Synty human for humans), nameplates for NPCs (red hostile, blue
        vendor). Britain: 127 mobiles with models
  - [ ] Bodies the creature catalog doesn't cover show as the Synty human; spawn-point camps and treasure chests
  - [x] Interest management (2026-11-10): FishNet ObserverManager with a 100 m distance condition (`Assets/UOClient/Game/ActorsInRange.asset`, hidden past 110 m, rechecked within 1 s); `Tools/Game/AddInterest.eval.cs`
  - [x] Death, UO style (2026-11-10): creatures leave a corpse (3 min) and are removed; a player who dies leaves a
    corpse (10 min) and walks on as a translucent ghost (monsters ignore ghosts, ghosts can't fight, HUD says to find a
    healer); any BaseHealer NPC within 5 m resurrects them (`DeathService`, `Corpse`, `Tools/Game/AddDeath.eval.cs`).
    Healer types marked from the data's `Base` chain. Checked in Solo by code.
  - [ ] Death animations: none in the project, so corpses tip over from the idle pose (humans lie with arms out)
  - [ ] Ankh shrines resurrect too; healers' own lines from the content (cliloc) once text is imported
  - [x] Items, first slice (2026-11-10): `Items.json` (5,097 kinds) and `LootPacks.json` migrated; backpack (B),
    paperdoll character window (C, the old window's layout re-pointed: live doll, slots, drag to wear/take off,
    Stats tab), corpse loot (E, Take all), starting kit, weapon sets damage/speed/reach, armour physical resist,
    creature resists. Items on the wire by kind number.
  - [x] Looks (2026-11-10): characters dressed with the Sidekick dresser from what they wear. NPCs sync only kind +
    seed (name, body, sex, outfit derived from our content on both sides); players sync a `Look` (sex, skin, hair,
    beard, gear by item number); corpses dressed the same; data-file hash in the handshake.
  - [x] NPC outfits migrated (2026-11-10): 564 of 565 human kinds have an `Outfit` (inline or a template in
    `Outfits.json`, which also holds the town guards, hue rules and hair styles); 40 kinds keep `OutfitUnparsed` lines
  - [ ] Town guards (WarriorGuard, ArcherGuard) aren't creature kinds yet: their outfits wait in `Outfits.json`
  - [ ] Birds and other kinds whose body field didn't resolve fall back to the human model
  - [ ] Before deploying a server build, run it locally first (a MonoBehaviour declared in a file of another name
    saves as a missing script and the build's scene won't load: GameLook did this on 2026-11-10)
  - [x] Character creation (2026-11-10): `Creation.json` + `Skills.json` migrated (58 skills, 8 professions, 46
    skill item sets). Creation screen with live preview: sex, skin, hair and beard with colours, a profession or
    Advanced (stats to 90, up to 4 skills at most 50 each totalling 100 or 120). Server checks the same rules and
    gives the kit in UO's order. Stats and skills feed combat: hits = Str/2 + 50, the weapon's skill and Tactics.
    Older characters got the Warrior profession's stats and skills.
  - [x] Skills, first slice (2026-11-10): UO's gain rules re-implemented (`SkillService`): gain chance from caps
    and difficulty x gain factor x 2.5 player rate, 0.1-0.4 below 10, 100 cap each / 700 total, Down-locked skills
    give way; stat gains (10%, primary/secondary, 125 each / 225 total, Down-locked stats give way). Fighting trains
    the weapon's skill, tactics and the defender's skill; bandages (double-click) heal by healing + anatomy with
    slips. Skills tab with locks; stats with locks on the Stats tab. Gains announced at the top of the screen.
  - [x] Decided (2026-11-10): out-of-combat regen slowed to 1.5% of max hits a second (about a minute to full) so bandages matter; none in combat
  - [x] Gathering and crafting (2026-11-10): `Harvest.json` (mining ore/sand, lumberjacking, fishing) and
    `Crafting.json` (11 systems, 1,198 recipes) migrated; decorations copied to `WorldSource/Decoration` (out of
    git). `WorldData/<facet>/layers.bin` (`Tools/Bake/MakeTileLayers.eval.cs`): per-tile harvest and station layers
    from the UO map + decorations (Trammel: 1.58M ore, 229k trees, 75 anvils, 150 forges, 81 ovens, 325 water).
    E in front of a harvestable tile with a tool gathers until the bank runs dry; banks, veins, fallback, bonus finds.
    Double-click a tool opens its crafting window (cliloc names, chances, materials); server checks tool, skills,
    material skill, resources (with equivalents), stations; success/exceptional by UO's formulas; failure loses the
    resources. Smelting ore at a forge.
  - [ ] Ship `layers.bin` to clients with the world (updater) — clients use it for the E prompt
  - [ ] Crafted items' quality (exceptional) and maker's mark aren't stored on items yet; tools don't wear
  - [ ] Sand (needs learned Sand Mining), Glassblowing and Masonry (learned), recipe scrolls, inscription (mana)
  - [ ] Fishing's special catches (mutations, SOS, special fish); lumberjacking's kindling with blades; stone/gem mining
  - [ ] Decorations themselves aren't in the baked world yet (only their stations are in the layers)
  - [ ] Active skill uses (Hiding, Meditation, Anatomy, Arms Lore...)
  - [x] Magic, first slice (2026-11-10): `Spells.json` migrated (64 Magery spells, pools/regen, cast timing, skill
    ranges, poison levels, effects as data with formulas). `Formula` evaluates the data's expressions. Mana/stamina
    pools (Int/Dex), mana regen by UO's formula; spellbooks hold spells (item Data); 32 spells castable: damage,
    chain lightning, heals, cures, poison, buffs/curses, mass curse, paralyze, mana drain/vampire, night sight (no
    darkness yet), create food, resurrection, teleport (ahead, no cursor). Harmful spells to your target, beneficial
    to your friendly target or you; casting time, words spoken, disturbed by blows, Magery check (fizzle keeps mana),
    shard rule: book casting costs no reagents and 1.25x mana. Spells tab, 10-slot hotbar (1-0), cast bar, mana and
    stamina bars. Poison, paralysis and stat buffs/curses as statuses on combatants.
  - [ ] Spells waiting on systems: summons, fields and walls, recall/mark/gate (travel and runes), dispel,
    invisibility/reveal, incognito/polymorph, protection, reactive armour/magic reflection, item spells (trap, lock,
    telekinesis), the shard's 12 extra Magery spells (ids 1000+), Necromancy/Chivalry/Bushido/Ninjitsu...
  - [x] NPC spellcasters and elemental damage and resists (2026-11-11; see Base UO item 3)
  - [ ] Active Meditation (doubles mana regen), Focus; reagents when casting from scrolls
  - [x] Housing, first slice (2026-11-10): `Housing.json` (rules, 4 plot tiers priced from ServUO's foundations,
    19 kit pieces, wall/roof styles, classic houses kept as blueprints) and `Regions.json` (460 regions; 308 towns,
    guarded zones, dungeons and code-only areas where you can't build) migrated by `export_servuo_housing.py`. Plot
    deeds sold by architects; the deed shows the plot ahead of you, checked against regions, roads, impassable tiles
    (new `Road`/`Blocked` tile layers), other plots and the ground's rise; one per account. Build mode (H): palette,
    ghost, levels, turning, pay materials per piece (logs pay for boards), refund on removal; plot boundary rails.
    Pieces are kit prefabs with their own colliders on server and clients; house doors open through the door
    service, private houses only for owner and friends. Trees and plants on a plot are cleared. Saved in
    ServerData/houses.json; decay after 30 days unvisited. `HousePlot` objects seen from 300 m.
    Also: walls block melee, aggro and spells (one line-of-sight check); nothing spawns or wanders within 8 tiles
    of a plot.
  - [ ] Housing next: furniture and decor placed freely, lock-downs and secure storage (tier Lockdowns/Storage),
    co-owners, blueprints (stamp a classic house), roof generated from the walls, more pieces (half floors,
    corner/hip roofs, castle stone), stone pieces should cost granite once stone mining exists
  - [ ] Generated vegetation rocks on a plot claimed after its chunk loaded keep their collision until the chunk
    reloads (the merged collision mesh isn't rebuilt)
  - [ ] Creatures already standing near a new plot stay until their spawn sleeps
  - [ ] Ship `Road`/`Blocked` in layers.bin to clients with the updater (clients use it for the plot preview)
  - [ ] Parry with a shield, Focus, and the other passive combat checks
  - [ ] Starting city choice (UO lets you pick; everyone starts in Britain for now)
  - [ ] The RedBook's author/title (book items have no properties yet); Young status and its bank ticket
  - [ ] Per-part dye channels on gear (cloth / metal / trim) for Sidekick's colour regions
  - [x] Decided (2026-11-10): dying keeps your belongings; nothing drops onto your corpse
  - [ ] Loot magic properties (intensity/max props in the packs) as our own rules
  - [ ] Item names from cliloc once the text import exists (potions show tiledata names)
  - [x] Vendors, bank, ground, splitting (2026-11-10): `Shops.json` migrated (104 shops; per-vendor shop lists with
    conditions in its `Vendors` table: map checks false on Britannia, random armour sets rolled from the vendor's
    seed). E on a friendly NPC: Buy / Sell / Open bank box; "bank" said near a banker opens it too. Pay from backpack
    then bank; hourly restock. Bank box saved with the character. Drag out of every window drops on the ground (a
    pouch, E to pick up, 20 min decay); hold Shift (SplitStack action) to choose how many.
  - [ ] Vendor economy rules from Shops.json Notes (price drift, town tax, quality, 1.9x resale) as our own rules
  - [ ] Pets from the animal trainer (Shops "Creature" entries) once there's a stable / followers
  - [ ] Conditional stock entries (Shops "If") left out; cliloc names for preset maps etc. need the text import
  - [ ] Re-running export_servuo_creatures.py undoes 3 hand fixes to "Shops" (namespace-qualified and nested SB names)
  - [ ] Client UO data folder: default still points inside the old ServUO folder; should be the player's UO install
  - [ ] Corpses hold loot (creatures) and the dead player's belongings (needs items)
  - [x] Full re-bake at 1x with true-size houses, all facets (2026-11-10): 9.2M objects, about 55 min; server rebuilt
        and redeployed to phoenix-server with the new Trammel world and content (accounts kept; characters saved at
        the old scale go back to the start point)
  - [x] Combat, WoW style (2026-11-10; tested by code in solo: a rat aggroed, swung and missed, took 8 and died,
        was removed, and combat ended): `Combatant` (hits, in-combat, dead,
        UO-style hit chance/damage/swing speed), aggro puts you in combat until the attacker dies/leashes/you die,
        creatures leash home at 35 m and heal, players respawn at the start point after 5 s; `CombatService`
        (target/attack requests, swing broadcasts); `CombatHud` (click/Tab targeting, R attack, unit frames, floating
        numbers)
  - [ ] Swimming and falling damage (needs water surfaces and health)
  - [ ] Delete the UO-protocol client code (Net, WorldState packets, Walker) once nothing uses it

## Art
- [ ] Art mapping: replace the placeholder colored boxes for statics with Fantasy Kingdom props and Pure Nature vegetation and rocks (in progress)
- [x] Wall orientation from UO art (South/East edge, corner, post, full block) and Fantasy Kingdom walls from `Data/WallKit.asset`: plaster, timber, stone (stacked) and window half-walls, timber/stone posts, castle blocks; merged into one mesh per material per block (2026-11-09)
- [x] Roofs: UO roof tiles become Fantasy Kingdom clay-tile or thatch half-slopes facing downhill, ridges get two half-slopes and a peak cap, pitch steepened 2.2x from each roof's eave (`RoofPlanner`), rows overlap so there are no gaps (2026-11-09)
- [ ] BUG (reported 2026-11-09): walls poke up through the roof along its edges (eaves): the tops of the wall pieces stick out above the roof tiles. Likely the steepened roof starting below the wall tops at the eave, or wall caps (short h3 plaster pieces) sitting above the eave line
- [ ] Roof gables (gable ends can be open under the roof): the stepped UO gable walls stay at UO height while roofs are steepened, so gable tops fall short of the roofline; steepen gable walls the same way (or replace with peak pieces). Seen at Britain x=1599, y=1585-1598: a triangle of short plaster walls (0x014B, z 60-78) whose stack shows through the roof where the steepened slope dips below it; the steepened roof and the gable must use the same heights
- [ ] Roof corners and hips: tiles where two slopes meet use one slope only; use Fantasy Kingdom corner/hip pieces and check L-shaped and hipped roofs
- [ ] Roof colours: slate and stone roofs use the same clay tiles; find grey slate pieces or tint
- [x] Furniture and clutter: `Data/PropCatalog.asset` (26 entries, editable in the Inspector) maps UO tile names to Fantasy Kingdom props for both map statics and server items: tables, counters and display cases (one stretched prop per row of tiles), beds, benches, chairs, stools, shelves, wardrobes, cabinets, chests, crates, barrels, street lamps, signs, banners, anvils, forges, planters, candles, books, ink wells, wells. Props keep their real-world size, shrink only to fit, and wall furniture turns its back to the wall (2026-11-09)
- [x] Remaining boxes: the catalog (48 entries) adds a Block fit (stretched to the tile and its UO height) for raised stone/wood platforms, stoops and stairs, posts, piers and pillars; railings along their edge for banisters; the fountain, statues, fireplaces, cauldrons, buckets, baskets, sacks, carts, and small items (bottles, mugs, plates, bowls, bread, jugs, scrolls, hammers). Exact-name fragments (`=stone`) keep generic names from catching more specific ones (2026-11-09)
- [x] Stoops, decks and stairs: short walls (10 Z or less: planter fronts, garden walls) are slim stone courses along their edge (`PropFit.LowWall`, `MaxHeight`) instead of the house wall kit's blocks; stone step slabs and outdoor boards are thin stone/plank slabs; wooden support blocks are posts capped at the deck above them; UO stair tiles are Synty stair flights scaled to one tile's rise and turned toward the next step or landing (`PropFit.Stair`) (2026-11-09)
- [x] Stairs, round 2: Synty flights climb toward their local -x (was assumed +x, so every stair faced backwards); a run of UO stair tiles, traced across block edges, is now one flight from the lowest step to the top, climbing the way the steps rise; steps side by side at one height become one wide flight, and a step's climb ignores its same-height twin and wants low ground at its foot (2026-11-09)
- [ ] Stone stair flights (`SM_Bld_Stairs_Straight_01`) read light/beige next to grey stone stoops; tint or swap for a greyer stone flight
- [x] Banners hang flat on the wall beside their tile (`PropFit.Hang`), from just under the top of the wall, at least 1.2 m off the ground; generated walls keep windows out of modules with a UO hanging in front (`HouseLayout.Hangings`, `SyntyBuilding.BlankPoints`) (2026-11-09)
- [x] Door leaves are a solid rectangle (`SM_Bld_Base_Door_01`) stretched to the 1.25 x 2.5 m opening; the arched castle leaf left the corners see-through (2026-11-09)
- [x] Half-module wall slots (beside single UO doors) use the set's plain panel squashed to half width (`SyntyBuildingGenerator.SquashPlainHalves`) instead of the kit's mismatched half pieces (2026-11-09)
- [x] Low walls are grounded (down to the surface below); a wall UO floats over a planter (more than 2 Z up) stops 2 Z above its own base, so planter fronts like 1673-1679,1601 stand about 1.25 m instead of 2.25 m (2026-11-09)
- [ ] Door leaf style: `SM_Bld_Base_Door_01` is a plain generic door; find or make a planked rectangular leaf that matches the Fantasy Kingdom houses
- [ ] Still boxes (few): rope barriers, cannons and anchors, paintings and portraits, decorative weapons and shields, the virtue shrine plaques, gems, loose armour, Town_Cryer; mushrooms and rushes should go to the vegetation catalog
- [x] Distant doorways and furniture no longer empty out: items the server drops beyond ~18 tiles stay as ghosts (doors shut) until the player is back near them and the server doesn't re-send them; generated houses hang shut leaves in their doorways before the server sends the doors, and the door item takes its leaf over (2026-11-09)
- [ ] Prop facing: wall furniture assumes Synty props face +Z; spot-check beds, chairs and signs around town and fix any that face the wall
- [ ] Multi-tile UO furniture made of different-named parts (some beds, large tables) may place several props; group by item art, not just name
- [ ] Gables and low wall pieces: short UO wall tiles (h3, h5) are squashed half-walls, which reads as stripes on stepped gables; use Fantasy Kingdom wall peaks and caps
- [ ] Doors: door statics and door items (opening/closing) are not mapped yet
- [x] Walk-to-open doors: `Walker` uses (0x06) a closed door item it walks into, then walks on once the server opens it. `DoorTracker` reads open/closed from the door graphic (runs of 16 from each door base, closed on even steps); `DoorViews` binds each door item (and its double-door partner) to the nearest generated `BuildingDoor` and swings it as the server opens and auto-closes it. Generated doors sit on the half-module nearest UO's doorway, centred on double doors (2026-11-09)
- [x] Interior doors: generated houses build no partitions (`SyntyBuilding.NoPartitions`); UO's own interior walls stay (kit walls) and any door item with no generated door gets a kit leaf (`SM_Bld_Castle_Door_Single_01`) on the wall edge its art stands on, swinging with the server's state. Seen in place at 1655,1604 (2026-11-09); swing not yet checked by walking into it
- [ ] Interior walls are the old per-tile kit walls, not the generated house's wall set; they can differ in style from the outer walls and may meet them a little off at the junctions
- [x] Door alignment: the kit's house doors are a 1 m leaf in a 2.5 m panel and never lined up with UO's 1.25 m door tiles. Generated walls now leave an open gap exactly over UO's doorway (`SyntyBuilding.EntryWidths`, one half-module per door tile), and each UO door item hangs its own kit leaf in it, so single and double doors line up tile for tile (2026-11-09)
- [x] Double doors: the first leaf of a pair (its partner on the next tile along the wall) is mirrored to hinge at its outer end, and both swing inward to the same side, so the pair opens from the middle like UO's linked doors (2026-11-09)
- [ ] Doors outside generated houses (gates, dungeon and castle doors) now get the same kit leaf; gates and double castle doors deserve their own prefabs
- [x] Rubber-banding after the world was seeded: the walker only knew map statics, so it walked into server items (lamp posts, furniture, fences) and the server snapped the player back. `SurfaceQuery.DynamicTiles` now feeds the walker the items on each tile; walking confirmed in play (2026-11-09)
- [ ] Floors: wooden floors, carpets and pavers (statics and town land) are still flat colored tiles
- [ ] Wall variety: pick panels by UO graphic family (e.g. different timber patterns) instead of one panel per style
- [ ] Wooden walls: UO wooden and log walls currently render as half-timbered plaster; they need solid wood (plank or log) pieces. Fantasy Kingdom's `SM_Bld_Castle_Hoarding_Wood_Wall_01/02` is a plank panel; a Vikings or Western pack may have better log walls
- [ ] Plain vs half-timbered plaster: UO names both "plaster wall". `StaticArt` now measures each graphic's share of dark pixels (`ArtMask.DarkShare`): Britain's plaster walls range 0.16-0.57, so a threshold around 0.3 should split plain from half-timbered
- [x] Port Soulbound's building generator into the client (`Scripts/Buildings`, namespaces `UOClient.Buildings(.Synty)`): asset lookups go through `SyntyAssets` (registry asset for player builds, AssetDatabase fallback in the editor), Soulbound's interaction/lockpicking hooks removed, editor-only fire VFX compiled out. A test cottage builds in 0.6 s with no door failures (2026-11-09)
- [ ] Synty asset registry: create and fill `SyntyAssetRegistry` so generated buildings work in a player build (the editor falls back to AssetDatabase)
- [x] Roof colours and wall atlases: Soulbound's five roof-colour materials and the generated brick/grey brick/wood-board wall atlases copied to `Materials/Roofs` and `Materials/SyntyWalls` (2026-11-09)
- [x] Generate whole buildings: `HouseDetector` reads each house (footprint, storeys, doors, windows, wall style, thatch) from the statics; `HouseSpecBuilder` turns it into a Synty spec on 2.5 m cells (roof ranges split by storey height, cross-gables where a wing butts into a wider range, 30-40° pitch by span, doors where UO's door gaps are); `HouseIndex` + `MapRenderer` build them as blocks stream in and hide the UO walls/roof/floors they replace. Britain: 45 houses detected, ~19 built around the player with no errors (2026-11-09)
- [x] Generated houses: the long house east of 1655,1598 kept its UO roof and chimney box because it wasn't detected (its roof stops a tile short of the west wall, and the overhang peel ate the house from that strip); walled-in floor is now added before the peel, and it builds as one L with its neighbour (2026-11-09)
- [x] Generated houses: missed houses in Britain (`NetTest houses ... misses` lists unclaimed roof clusters, `HOUSETRACE=1` says why a flood was rejected): split-level floors now join one house, open verandas and lean-to sheds become open-sided porch rooms under the house roof, the cell grid picks its best one-tile alignment, and near-rectangular plans get one gable. Britain: 46 houses, only unattached roofs left (2026-11-09)
- [x] Unattached roofs: the "stall" strip at 1508,1540 is a stable with an earth floor; bare ground under a roof now counts as floor, so stables and barns build (thatched wooden barn). Floods that leak along a paved overhang into neighbours are split back into one house each, porches must be open to the sky and at least 2 tiles deep, and a wall blocks whenever it rises above the higher floor. Britain: 48 houses (2026-11-09)
- [ ] The 3x3 brick corner towers (e.g. 1446,1558 and 1462,1566, flat stone roofs at z 70) still draw from UO tiles; build them as towers (Synty castle tower pieces)
- [x] Floating fragments in the haze: the kit's transparent glass and Pure Nature's tree impostors (`BK/Impostor`) don't fog. Generated houses swap `Glass_01` for a see-through URP Lit `Materials/UOWindowGlass.mat` (transparent, fogs); houses (`Buildings` layer) and map vegetation (`Vegetation` layer) are culled where the fog is total, so nothing is cut off mid-silhouette. The camera's obstacle filter includes `Buildings` (2026-11-09)
- [ ] Fog: a dark winding line still shows far out when looking down from high above (not from the normal camera); likely the far terrain or vegetation range not fogging; check if the camera is ever allowed that high
- [ ] Generated houses: split-level floors build at the main floor height; raised parts (a dais 10 Z up) keep their UO floor tiles as placeholders above the generated floor
- [ ] Generated houses: UO entry steps/stoops at doors are still placeholder boxes; drop them or use the kit's stoop
- [x] House foundations: a house standing above the ground around it (17 in Britain, 0.5-2.9 m) gets stone plinth courses under its floor down to the lowest ground just outside (`SyntyBuilding.FoundationDepth`, `HouseLayout.GroundZ`). The terrain where UO raised the land under a house is flattened to the outside ground at the walls and kept just under the floor inside (`HouseIndex.TerrainCornerZ`), so the ground meets the foundation instead of ramping up in a mound (2026-11-09)
- [x] Town terraces: UO's land steps a full storey between rows and the heightmap (one height per tile corner) can only show that as a 45-degree bank. Steep tiles (8+ Z across) within 2 tiles of town paving now paint a new `LandKind.Terrace` layer (`Materials/UOTerraceStone.terrainlayer`, Synty `Generic_Rock`; the castle brick read as brick, not rock) so they read as stone retaining walls; open-country hillsides keep earth and grass (2026-11-09)
- [ ] Terraces, further: true vertical retaining walls would need a finer heightmap (2+ samples per tile) or meshes that cut the bank; walking height on a step tile is UO's (the tile's average), so any wall must leave that intact
- [ ] Rock and mountains: Pure Nature has no stone terrain layer (the Rock slot holds Gravel1); make one from `BK/PureNature/Models/Rocks/Cliffs/Textures/Cliff01_a.png` for mountains and cliffs
- [ ] Generated houses: half-cell refinement (footprints are rounded to whole 2.5 m cells, so odd-width houses shift by a tile)
- [ ] Generated houses: a big house takes ~0.3-0.4 s to generate on the main thread (one per frame); cache built houses or pool them if it hitches
- [ ] Generated houses: merge meshes per house (currently every kit piece is its own GameObject)
- [ ] Generated houses: interiors use the generator's partitions, not UO's interior walls; UO furniture stays as placeholder boxes
- [ ] Player-built (customizable) houses: the server sends their layout as a house-design packet (0xD8) and players can draw almost any shape. Generate the house from that layout, then generate a roof where the footprint allows one (gable wings over rectangles); when the layout is too irregular for a proper roof, fall back gracefully (the player's own roof tiles, or a flat roof) rather than forcing a broken one. Design the building generator so both map houses and player houses go through the same path
- [ ] Make the building algorithm look better (current result is "OK, not great"): one consistent style per building, merge straight runs into full 2.5 m walls (with real doors/windows), proper corner pieces, trim beams lining up between storeys, gables from peak pieces instead of stepped stripes
- [ ] Meadow Forest pack: paused (2026-11-09) in favour of Pure Nature; not in the project, and its .unitypackage is still in Downloads
- [x] Terrain texturing and blending: Unity Terrain chunks (64x64 tiles) from UO land heights, Pure Nature terrain layers per land kind, noise-roughened splat blending, height blending, holes for void/NoName, Pure Nature water on water land and water statics (2026-11-09)
- [ ] Mountains that look like mountains in 3D (UO mountains are land Z plus rock statics; they need real sculpted shapes)
- [ ] Proper biome transitions (grass to sand, forest to swamp, snow line, and so on). Basic blending between neighbouring kinds works; still to do: use UO's own transition tiles, wider blends between big biomes, per-biome vegetation
- [x] Pure Nature vegetation: UO trees, bushes, grass, flowers, ferns, reeds, mushrooms, rocks, stumps and lily pads become Pure Nature prefabs chosen per biome from the land under them (`Data/VegetationCatalog.asset`, 62 entries, editable in the Inspector); UO leaf/needle statics are skipped (2026-11-09)
- [ ] Vegetation tuning: check sizes per biome in play (targets are guesses, e.g. trees 7-10 m), and visit jungle, swamp, snow, desert and volcanic areas to check each biome's picks
- [x] Grass, phase 1 (base fill): `GroundCover` paints GPU-instanced terrain detail (the catalog's grass, flowers and ferns per biome) by land kind, in noise patches, skipping tiles covered by statics (2026-11-09)
- [ ] Grass covers some walkable tiles (paths, paving, floors that aren't statics); keep it off anything a mobile walks on as a road, and thin it near paths
- [ ] Grass floats past ledges: at drops (e.g. street edges) detail cells and noise spread push grass beyond the tile edge into the air; clamp cells to their tile and drop cells on steep slopes
- [ ] Grass, phase 2 (VFX Graph): generate grass with the VFX Graph so it reacts to things moving through it (players, NPCs, creatures bending it aside); replaces or layers over the phase 1 fill
- [ ] Vegetation performance: each plant is a GameObject (about 1,300 around Britain, ~118 fps in the editor); move to GPU instancing if dense forests get slow
- [x] Town paving: cobblestone, stone/flagstone/marble and brick town land is painted into the terrain (`LandKind.Cobblestones/Flagstones/BrickPaving`, `LandClassifier.Paving`, layers `Materials/UOCobblestones|UOFlagstones|UOBrickPaving.terrainlayer` from the Fantasy Kingdom castle stone textures), so roads follow the ground and blend into the verges instead of sitting on it as flat squares; no overlay tile is drawn for paved land (2026-11-09)
- [x] Flat paving statics laid on the ground (stone steps, pavers, flagstones, cobbles; Surface, at most 1 high, within 4 Z of the land) are painted into the terrain's paving layers like paved land instead of drawn as square slabs (`LandClassifier.PavingOfStatic`), e.g. in front of the banner house at 1672-1679,1601 (2026-11-09)
- [ ] Town land the terrain doesn't pave (wooden floor, plank, tile outside generated houses) is still drawn as flat coloured overlay tiles
- [ ] Pure Nature scripts were patched for Unity 6.6 (`GetInstanceID` in BK_FogVolume.cs and BK_Lava.cs, marked "UOClient patch"); re-apply if Pure Nature updates
- [ ] Houses and boats (multi items): not rendered at all yet
- [x] Creatures: 76 UO body groups drawn with POLYGON models (Data/CreatureCatalog.asset, built by Tools/Creatures/CreatureCatalog.eval.cs); bodies not in the catalog stay capsules
- [ ] Creature gaps (no owned model, still capsules): sea serpents, birds/eagles, big cats, ratmen, harpies, spiders, rats, snakes/serpents, dolphins, mongbats, gazers, corpsers, slimes, scorpions, beetles, frogs, drakes/wyverns/dragons, farm and forest animals (SIMPLE packs skipped), horses/mounts; see Docs/CreatureMapping.md
- [ ] Creature stand-ins to revisit: lizardmen (Kaiju lizard), ice fiends and molochs (Kaiju), cyclopes/titans (Dark Fortress Behemoth/Colossus), imps (scaled-down demon), elementals (one tinted golem model per element/ore)
- [ ] Humanoid creatures share AC_UOHumanoid (walk/run/idle only); wolves and dogs use DogLocomotion; no attack/hit/death clips are wired for any creature yet
- [ ] Equipment: show what mobiles wear (layers) on the Sidekick models instead of a fixed Starter outfit

## Camera
- [x] Camera collision: Cinemachine Third Person Follow obstacle avoidance, with mesh colliders on merged walls/roofs/placeholders and terrain colliders; trees stay collider-free so the camera can pass through foliage (2026-11-09)
- [ ] Collider cost: every map block bakes mesh colliders when it streams in; if it causes hitches, bake them on a background thread (Physics.BakeMesh) or use simpler box colliders

## Animation
- [ ] Female bodies: use the Base Locomotion Feminine clip set (an override controller for AC_UOHumanoid)
- [ ] Tune walk/run playback rates (currently 1.6x / 1.25x) so feet don't slide at UO speeds
- [ ] Combat animations (ANIMATION - Sword Combat / Bow Combat packs in SyntyPass)
- [ ] Creature clips from AnimGen (`com.phoenixforgegames.animgen`, repo G:\Git\Personal\PhoenixForgeGames\Unity.AnimGen): generate per creature model with its UO body as reference and wire them into the creature views; drive `animator.speed` from WalkNaturalSpeed/RunNaturalSpeed in the `<Model>_AnimGen.json` report
- [ ] `Packages/manifest.json` references AnimGen by absolute local path (`file:G:/Git/...`), which breaks other clones; switch to a git URL once the package repo is pushed
- [ ] AnimGen: apply `AnimationSequence.uop` group replacements so UOP-only bodies don't miss action groups in reference fitting

## Testing
- [ ] Log in to the live shard (192.168.68.91) as Kalor and check the world with real players and NPCs

## Housekeeping
- [ ] `Assets/DownloadCache` (35 MB, two .unitypackage files) appeared when Sidekick was imported; decide whether to keep it in the project

## Round of fixes, 2026-11-09 (from play-testing)
- [x] Stacking: props and items are recorded per tile and dropped onto the real top of whatever UO stacks under them (`PropPlacer.Stack`); UO heights give the order only, so things sit on Synty tables/counters however tall those are. Grouped furniture (tables, counters) fills the whole UO footprint, both ways
- [x] Ground objects (outside houses) sit on the terrain surface under their middle; inside generated houses they stay on the floor (the terrain is lowered there)
- [x] Props keep their prefab's own root rotation (a holder carries the fit), fixing turned presets like planters
- [x] Grass stays off tiles beside steep drops; steep town ground is rock all over (no paving smeared up banks); new terrain layers use a matte mask (`Materials/UOMatteMask.png`)
- [x] UO walls are hidden only where the generated house has a wall on the same edge (`HouseSpecBuilder.WalledEdges`); walls the cell rounding moved stay visible instead of blocking invisibly
- [x] Cutlery, trays, food, gems, paintings, shields, weapon racks, curtains, ladders, hay, rope, cannons, anchors added to the catalog (72 entries); placeholder cubes are sized to the item and stand on their surface
- [ ] Not yet verified by walking: the invisible blocker from the first screenshot (path beside a planter) and the blockers in the building at 1657,1580; walkable map in front of the banner house showed only hedges/planters blocking
- [x] Invisible bed at 1657,1579: generated houses hid every flat walkable static as "floor", including impassable furniture flagged as a surface (beds, low tables); those are now kept and drawn (2026-11-09)
- [x] Rubber-banding: the walker ignored anything that is both a surface and impassable (beds, tables, counters), so it predicted steps the server refused; impassable now blocks unless it's a stair/ramp (Bridge). Refused tiles are avoided for 20 s, and every refusal logs a console warning listing what is on that tile; confirmed in play, no more snapping (2026-11-09)
- [x] Decks: UO's stacked "wood" support blocks are solid plank blocks filling their tiles (capped at the deck boards), so a deck is one solid landing from stair to door instead of boards on thin posts (2026-11-09)
- [x] Sausages are a lying salami (the Synty sausage string hangs upright); chairs and stools face a neighbouring table/counter (`PropCatalog.Entry.FaceTable`), planters put their back to the wall (2026-11-09)
- [ ] The solid deck block's sides are a plain plank face; the wooden stair flight still shows its small legs and the open space under its top step
- [x] Stair flights spanning a map-block edge are placed by whichever block loads first (once), so they no longer go missing until the other block streams in; UO support blocks under stair treads are skipped, since the flight covers them (2026-11-09)

## Dressed humanoids (Sidekick), 2026-11-09
- [x] Humanoids are built from Sidekick parts by `SidekickDresser` from their UO equipment (`Data/SidekickWardrobe.asset`, 26 Knights/Villagers outfits tagged by material, head type, long hips, cape). Each worn item picks an outfit of the same stuff per graphic; skin, hair, beard and dyes recolour a per-character copy of the colour map from hues.mul (`Core/Data/Hues.cs`). Re-dresses on equip/unequip
- [x] Part meshes are shared copies with the female body shape baked in (Synty's copy recipe; a copy that sets submeshes separately skins with holes). The Animator is added last, or CullUpdateTransforms leaves the rig in T-pose
- [x] Robes: SIDEKICK Fantasy Sorcerers imported; its robes (with wizard hats and hoods) dress UO robes, and the villager long dress counts as a dress, not a robe (2026-11-09)
- [x] Elves: SIDEKICK Elven Warriors imported; elf bodies (605-608) get the elf body and pointed-ear faces and prefer elven outfits, which humans don't wear (2026-11-09)
- [x] Weapons, shields and tools in hand (layers 1/2), matched by UO item name to Fantasy Kingdom models (`SidekickWardrobe.Weapons`). Grips run along the fist channel of the prop bones, checked in close-up renders: swords/maces rest point-down, edge on, wrap in the fist; axes edge-forward; staffs, polearms and bows level (bow string up to the armpit); shields on the left forearm. `HandCurl` closes the holding hand each frame (2026-11-09)
- [x] Sword and shield checked in play walking and running: blades rest 20° down (35° dug into the ground mid-stride), shields turned to face out to the left, parallel to the walk (2026-11-09)
- [ ] Daggers, maces, hammers and tools use the sword grip, not checked up close; per-model handle offsets may need tuning (`Held.Offset` / `Held.Turn`)
- [x] Hued metal armour (dull copper ... valorite) recolours the colour map's metal cells from the chest/arms/legs/helm/gloves piece, on the brighter part of the ore's ramp (2026-11-09)
- [x] Dyed metal armour tints the metal and the near-grey outfit colours under it (dark plating, the tabard's dark side panels); the trousers behind the shin guards are cloth; coloured cloth (the tabard's red, sashes, sleeves) keeps its own colours (UO would paint the whole piece) (2026-11-09)
- [ ] Boots are their own UO item (shoes layer), so plate wearers' boots stay leather-coloured
- [ ] Copper and gold read a little olive under the town lighting; the metal cells are shared, so mixed ore sets take the chest piece's colour
- [x] Cloth worn with metal armour dyes the armour's cloth: a doublet/tunic/shirt the upper body (sleeves, sash, tabard top), a kilt/skirt/pants the lower (tabard), else the chest cloth's dye, else default. A doublet or kilt over metal doesn't hide the plate; a robe still covers everything with a wizard robe (2026-11-09)
- [ ] Later, with custom server code: let players tint the cloth of armour separately (a second hue per item)
- [ ] Female bone movement (narrower shoulders) isn't applied
- [ ] Performance: ~25 skinned renderers per character. Measured at the Britain bank on the local server (2026-11-09): 21 dressed characters, 501 skinned renderers, frame time p50 6.6 ms / p95 8.1 ms in the editor, so merging isn't needed yet; re-measure on the live shard with real players
- [x] Actor frame rate (2026-11-10): Britain's 82 Sidekick NPCs took the editor from 76 to 22 fps. The merged
  Starter_01 mesh kept every part's skeleton (3,176 bone slots on 96 transforms); `SkinCompactor` collapses them to
  the 85 its vertices use, once per mesh, shared by all instances: back to 72 fps. `ActorCulling` on every actor
  model: no animation or skinning off-screen, quarter-rate animation past 35 m, not drawn past 90 m.
- [ ] Actor occlusion: hide actors behind walls/buildings too (needs a cheap visibility test or portal data per
  house; baked Unity occlusion doesn't suit a streamed world)
- [x] Character parts use a whole-figure bounds box, so animated limbs no longer get culled when only they are on screen (2026-11-09)
- [x] Removed the stray Sidekick-preview "Combined Character" from World.unity (2026-11-09)

## Terrain, water and stone buildings, 2026-11-09
- [x] Slopes blend to rock by their real angle (32° to 45°, like MicroSplat), whatever the ground; town terraces stay stone
- [x] Water: the bed under water slopes down into a basin (shallow at the shore, deepest mid-channel), the surface runs one tile in under the banks so the shoreline is where the ground rises out of it, dry gaps at water level beside water count as water, one surface per tile, one bed texture under all water; ground cover stays off water and bank tiles
- [ ] Water still shows a light strip along banks (shallow over the rising bank), following UO's square water layout; rock on near-vertical banks is stretched (needs a triplanar terrain shader)
- [x] Flat-roofed buildings (no roof tiles; a walkable deck a storey up) are detected as houses and built whole with the generator's flat roof and parapet; a storey needs tall walls on it, so a roof deck behind knee-high parapets isn't a storey
- [x] Castle-style buildings (stone walls, flat roof: banks, keeps) are built two storeys per UO storey (`HouseSpecBuilder.IsCastle`)
- [x] Parapet corners get a half-height timber post (`WoodBeam_05`), like the walls' corner posts (2026-11-09)
- [ ] The flat roof deck is green; players walking on a doubled building's UO roof (Z 20) would be inside its upper storey
- [x] False flat-roofed houses: a flat roof with no door and no window under it (passages under the city wall's walkway, vaults) is rejected; Britain's box went from 94 to 88 houses, leaving 18 flat-roofed ones (bank, guard posts, gate towers, keeps) (2026-11-09)
- [ ] Not yet looked at in play: the gate towers joined by wall (1490,1636) build as one H-shaped building; Castle British's interior (doorless) stays on the tile-by-tile path
- [x] Plants follow the terrain: plant statics are seated on the ground exactly as the terrain draws it (house flattening and the dip under water included), at their own scattered spot, whenever they're within 16 Z of it; ground cover (grass, flowers, ferns, mushrooms, reeds) is drawn as five plants at 40% size spread over the tile and tilted to the slope under each; trees and bushes stay upright (2026-11-09)

## VFX Graph grass, 2026-11-09
- [x] VFX Graph 17.6 installed; `Assets/UOClient/VFX/Grass.vfx` (built from code, `Tools/VFX/GrassVfx.eval.cs`) grows grass on each terrain chunk around the player (`GrassField`, radius 1 chunk). Each chunk bakes its normals/heights and a ground colour + grassiness mask from the splatmap; blades sit on the drawn ground, lean with its normal, take its colour (browner on dry ground, darker in forest), thin out as the texture blends away, and keep a fixed facing (crossed quads, never turning to the camera). Replaces the terrain detail grass and UO grass statics; flowers/plants stay as UO statics, seated by their mesh footprint and lowest point
- [x] Measured: ~60k blades over 9 chunks, 5.6 ms median frame in the editor (better than with detail grass); no far grass beyond ~1 chunk, the fog covers it
- [x] Grass is the BK grass meshes (low clumps Grass1/Grass2, meadow tufts GrassMeadows1/3) in two VFX systems, standing straight up, short (clumps 0.35-0.55, tufts 0.22-0.35 of BK size), fixed facing, swaying in the wind; lit through generated URP Lit VFX Shader Graphs (`GrassClump`, `GrassMeadow`: BK texture x ground colour), casting and receiving shadows. ~54k blades, 6.0 ms median frame (2026-11-09)
- [x] Blades, not clumps: four generated blade-cluster styles (fine, broad, wispy, seed stalks; `Tools/VFX/GrassBladeMeshes.eval.cs`), fine most common, per-blade shade variation; bases sunk 6 cm into the ground; 0.55-0.8 scale; 220k clusters per chunk capacity (2026-11-09)
- [x] Grass grows only on grass textures (Grass01, Grass02/jungle, GrassPine/forest, Moss/swamp), never on Mud01 or any dirt/sand/rock/paving; density follows the grass texture's share of the splat blend, and colour comes from the grassy layers alone (2026-11-09)
- [x] Density doubled (440k clusters per chunk capacity, ~490k simulated over 9 chunks) with a distance falloff: full density within 14 m of the camera, thinning to none by 60 m, each blade fixed in or out by its own random number (no flicker; `GrassViewer` passes the camera per chunk). 6.0 ms median / 9.2 ms p95 frame (2026-11-09)
- [ ] Grass particle buffers are sized by capacity (~50 bytes x 440k per chunk, ~200 MB for 9 chunks); trim capacity or the grass radius if GPU memory gets tight
- [x] Characters push grass aside: every MobileView is a `GrassPusher` (0.7 m); `GrassPushMap` paints them into a 64x64 camera-centred map (32 m) each frame, and each blade bends away by the push strength. Verified with a test pusher from above (2026-11-09)
- [x] Pushed grass springs back: the push map remembers pushes and lets them fade (95% over `RecoverSeconds` = 1.2 s), so walking leaves a trail that stands back up; the map is world-anchored (centre snaps to 0.5 m cells, contents shift as the camera moves) so trails stay put. Verified frame by frame (2026-11-09)
- [ ] Tune per biome (forest/jungle/swamp not yet seen), wind strength

## Gameplay phase 1: moving and talking, 2026-11-09 (Docs/GameplayDesign.md)

- [x] Input through Input/UOControls.inputactions (generated UOControls wrapper, GameInput holder); WASD and camera moved off device polling
- [x] Cliloc.enu (BWT-compressed) and speech.mul keywords loaded; system messages translated; NPC keywords sent with speech
- [x] HUD (UI/Hud.uxml): player frame, target frame, chat log and input, name plates in notoriety colours, health bars, speech bubbles, damage numbers
- [x] Targeting: click select (shows name), double-click use, Tab / Shift+Tab hostiles, Esc clear, E use nearest, R attack (war mode on), ` war mode; checked on the local server (tab, attack, combatant, damage, kill)
- [ ] Not verified: typing into chat with a real keyboard (synthetic key events reach the Input System but not UI Toolkit text entry from the CLI); Enter opens and focuses the line
- [ ] Not verified: click-select and double-click on the live screen with a mouse; NPC keyword speech ("bank", "vendor buy") at a banker or vendor
- [ ] Name plates for ~30 mobiles crowd the screen; consider a shorter range, fade with distance, or names only for hostiles / target / hovered
- [ ] Hues: chat and bubbles use colours by message type; UO message hues (hues.mul) are ignored
- [ ] Right-click context menus (0xBF 0x14), target cursor (0x6C), paperdoll (0x88) and containers come in phase 2; double-clicking an NPC asks for its paperdoll, which isn't shown yet
- [ ] Rebinding UI and gamepad pass (actions have gamepad bindings, untested)

## Gameplay phase 2: windows, 2026-11-09 (Docs/GameplayDesign.md, phase 2 plan)

- [x] Window framework (UI/Windows): movable windows, Esc order (context menu → newest window → target → main menu), B backpack, C character
- [x] Backpack: sorted grid from the server's container packets, names and tooltips from item property lists (0xD6), bags as tabs; checked in play (11 items, weight, gold)
- [x] Character window paperdoll: live render of the player (PaperdollStage, layer 31) with equipment slots; checked in play
- [ ] Not verified in play: drag and drop (grid ↔ bags ↔ paperdoll slots ↔ ground), right-click item context menus, Stats / Skills / Spells tabs, main menu settings and log out, NPC dialogue (E), auto-open corpses and auto-loot
- [x] Weapon abilities in the Spells tab, from a table generated from the shard's weapon scripts (Tools/Data/make_weapon_abilities.py, 116 weapons); checked: readying Armor Ignore reaches the server
- [x] Vendor shop window (ShopWindow from a plain ShopOffer): buying checked on the local server ("vendor buy" → 4 wares, bought a commodity deed, gold 1000 → 995)
- [ ] Not verified: selling (0x9E / 0x9F) at a vendor who buys your items
- [x] No targeting cursors: TargetResolver answers 0x6C itself; checked: bandage → self, Anatomy → selected NPC, scissors → menu of cuttable items in the pack (worn excluded); own unit frame selects yourself
- [x] Lumberjacking checked: using a hatchet from the pack puts the held weapon away, takes the axe up (paced, the server refuses item moves < 0.5 s apart) and chops the tree in front
- [x] Mining answers with the ground in front ("You can't mine there" on a town hill); smelting looks for a forge in front
- [ ] Not verified: mining a real rock/cave, smelting at a forge, harmful spells (current target, else declined with a hint)
- [ ] Tool menus list only usable items: scissors are filtered; other tools (dyes, keys, tinker tools...) still list the whole pack until each gets its own filter
- [ ] Party member unit frames (click to target) once parties exist
- [ ] No server gumps (decided): purpose-built windows for crafting, quests, house management, help. Done: the client closes server gumps (button 0) and notes the gump ID in chat
- [ ] Item icons: tiles are text for now (no UO art); render icons from the matching 3D props
- [x] Settings: view distance (near / medium / far / very far) and grass (off / near / far) apply live; Controls page rebinds keyboard and mouse keys (saved, reset to defaults)
- [ ] Not verified by hand: the Controls page rebinding, view distance / grass changes; render scale and shadow distance apply only in builds; gamepad bindings aren't on the Controls page
- [ ] Stacks: dragging always moves the whole stack (no amount picker yet)

## Hotbar and buffs, 2026-11-09

- [x] Hotbar: 10 slots on keys 1-0, saved per character (settings.json); spells, skills, weapon abilities, items (first carried of that kind, with count); checked: keys use bandages and ready the primary ability, slots survive a restart
- [ ] Not verified by hand: dragging spells / skills / abilities / items onto slots, swapping slots, right-click to clear
- [ ] Buff bar (0xDF) built, not seen yet (Tester has no buffs)
- [ ] Hotbar extras: cooldown display, more bars, rebinding the keys
- [x] Server gumps are parsed (0xB0 / 0xDD, zlib) and kept until the HUD exists; unknown ones are noted once in chat and their layout / text logged
- [x] Gump bridge: the shard's death window → client "You have died" window (respawn / stay a ghost); checked: respawned at Britain
- [ ] Not verified: healer / ankh resurrection (ResurrectGump, free and paid) → "Return to life?" window
- [x] Publish news (0xAD7C9C2) suppressed; the shard's "Commands" button gump (0x98EB1F26) silenced (paused: its features get client pathways one by one with the user)
- [x] What's new window (shard's WhatsNew: "What's changed since you left" on login, [news, and Main menu → What's new); checked in play
- [x] Ghosts: same character all in white (hair, robe kept); visible to their own player always, to others only in war mode (model, name plate, click collider, Tab); checked: own ghost in white
- [ ] Not verified: another player's ghost hidden out of war mode / shown in war mode (needs a second client)
- [ ] (Back burner, user 2026-11-10: "get to a Base UO game state before deciding what custom systems to implement") Shard custom systems from the old client need real client pathways (not gumps, not add-on scripts): Book of Commands features, Guide, To-do list, quest markers, house sorting, pet status, etc. — go through them with the user

## Minimap, 2026-11-09

- [x] Minimap (M toggles): top-down render of our own world (no UO art), mobile dots, town / dungeon / place names from Resources/Places.json (Tools/Data/make_places.py from the shard's Regions.xml, 147 places); checked in play near Britain
- [ ] Only shows ground already streamed in (view distance); a full world map would need its own pre-rendered tiles
- [ ] Renders the scene again every 0.2 s; measure the cost and lower the rate or resolution if it shows

## Multis (part A of Docs/BuildingDesign.md), 2026-11-09

- [x] multi.mul reader (Core/Data/MultiData); server house multis laid over the map (MapFacet overlays) and rebuilt by the house detector + generator (HouseIndex.Forget, MapRenderer.Refresh); checked: small brick house placed from a deed builds like a map house
- [x] House deed placement (0x99) answered with the ground under the mouse (no cursor)
- [x] Boats: ship models (Data/BoatCatalog.asset, Tools/Boats/BoatCatalog.eval.cs, Synty Pirate ships) scaled and turned; deck tiles walkable; checked: gargish galleon in Felucca
- [x] Custom (player-designed) houses: 0xD8 design packet + 0xBF 0x1D / 0x1E parsed and drawn; checked: foundation placed via the house placement tool, walls + windows added through the server designer
- [x] Bank: "bank" opens the bank box as a container window; checked: deposited a check
- [ ] Gump bridge rule: never answer a gump with a button it doesn't have (ServUO disconnects: "Invalid gump response")
- [ ] Boat sailing (0xF6), boat fittings as models, deck height per ship
- [x] Custom houses stand on the generated houses' plinth (BuildPlinth at tile resolution), the foundation's own floor / edge tiles hidden; designs that arrived at login are requested again when the world view starts; checked in play
- [x] Custom house foundation floor kept as invisible walkable surfaces (plinth draws it); verified by the user: walked up the steps into the house
- [x] Plinth per tile (squashed full course pieces, odd-sized plots), covers the whole foundation; posts / poles over lower ground stretch down (GroundedPole)

## Gargoyles, 2026-11-10 (design: monsters are player gargoyles with a preset outfit, same size)

- [x] Wing rig: Synty Dark Fantasy `SM_Chr_Attach_Wing_02` rigged in Blender (12 bones, soft weights, UVs pointed at the Sidekick skin cells (0,1)/(1,3) so the character's skin hue tints the wing), six generated clips (Fold, Spread, Open, Close, Flap, Glide), in `Assets/UOClient/Models/Gargoyle/` with `GargoyleWings.controller`; script `wingrig.py` is in the session notes folder, not the repo (2026-11-10)
- [x] Dresser: `Look.Gargoyle` (bodies 666/667) builds the Sidekick human plus two wings (right, and its mirror) on `spine_03`, in the character's own material; `GargoyleWings` component exposes `Spread` and `Flap` (2026-11-10)
- [x] Monsters: kinds whose only bodies are 4, 67, 130 (gargoyle, stone, fire, assassin, shade, effete) use the gargoyle look with the Outfits.json `Gargoyle` template (bald, kilt, a cutlass/scimitar/club/mace; stone greys, fire red); same height as players. The old catalog entries for those bodies are unused now (2026-11-10)
- [ ] Verify in play: wings follow the spine while walking and fighting, monsters look right, corpses, the second player seeing the same (NeedsVerified.md)
- [x] Player gargoyle race: Race row in character creation (Human / Gargoyle: stone-grey skin range `GargoyleSkin` 957-989, no hair or beard, same professions and kits), `Character.Gargoyle` saved, `Look.Gargoyle` synced, server checks it (2026-11-10). Still to do: gargish starting clothes, race-specific stats/skill caps if wanted
- [ ] Race rework: Fly is a placeholder racial ability; the user wants to rework it later
- [x] Flying (reworked 2026-11-10): "Fly" (racial ability id 1000, Spells tab, hotbar) toggles hover mode: the gargoyle hovers 0.6 m over the ground and moves as if walking (9 m/s, 4 walking). Space climbs off the ground, Ctrl (`Descend` input) descends; once climbed, coming back to the ground ends flight and the wings fold. Ceiling 40 m above the ground (`PlayerMotor.FlightCeiling`). Wings: climbing beats fast, moving forward beats slowly, straight down beats to brake, descending at an angle glides, hovering beats normally (`PlayerAppearance.FlyingLook`). Folded wings lean onto the back and narrow (`GargoyleWings.Mount`); the wing mesh reaches toward -x, so the unmirrored wing stands on the left (they were crossed over before); mounts 0.12 m either side, 0.17 when open; open wings lie flat (horizontal) and the flap bows them like a bird: wrist and fingers trail the arm, the wing folds in at the elbow on the upstroke (rig script wingrig4.py in the session notes)
- [ ] Flying: over water there is no ground, so flight holds altitude instead of landing (open water has no collision at all: walking or falling in drops out of the world, see below); no stamina cost, no fall damage when landing, no flying pose beyond a lean, no flight in the paperdoll, not blocked indoors/under roofs
- [x] Gargoyles cannot mount: `PetService.Ride` refuses them; flying also ends if mounted (2026-11-10)
- [ ] Horns: Sidekick has no horn part; needs a small attachment (the monster's horns are part of its head mesh)
- [ ] Gargoyle monsters whose body is not 4/67/130 (Gargoyle Destroyer/Enforcer/Guardian, pets...) still use their own bodies
- [ ] Kilt reads as short trousers on Sidekick: find or make a real loincloth part

## View distance and far LODs, 2026-11-10

- [x] Game scene draws further: `GameWorld` sets chunk radius from the View distance setting (Near/Medium/Far/Very far = 3/4/5/6 chunks of 80 m; was a fixed 2) and linear fog that is total just inside the loaded edge
- [x] `FarLod` (World/Bake): each 10 m block of a loaded chunk gets a merged, vertex-clustered (0.8 m cells) copy per material, in a LOD group (real pieces until ~0.10 screen height, merged mesh after, culled at 0.008). Built a few ms per frame as chunks load; no re-bake. About 25x fewer triangles measured at 81 chunks
- [ ] FarLod does not follow later changes to a block (house plots that clear trees, doors): the far copy still shows the cleared trees; rebuild the block's far copy on those events
- [ ] FarLod far meshes are built on the main thread from readable meshes; check frame time on a slow machine and in a build (palette meshes must stay readable)
- [ ] Terrain far from the player is full chunk terrain (no terrain LOD yet); fog hides the edge
- [ ] Open water has no collider: a character that falls in a river (or lands flying) drops out of the world (seen at about world tile 1475,1502)

## Gargoyle flight body animation, 2026-11-10

- [x] `GargoyleFlightPose` poses the Sidekick bones over the animator while flying: crouch wind-up and spring at take-off (motor holds movement 0.30 s, hover rises over the next 0.22 s, speed builds over 0.8 s), landing flare (Fly cast in the air now lands under control: `PlayerIdentity.Landing`, slows, descends 4 m/s max, flares, touchdown crouch, wings fold 0.3 s after), forward lean 0-48 degrees with speed, legs trailing with soft knees, arms close, chest rocking with the beat, 7 cm bob on the downstroke, hover sway, air braking (wings turned leading-edge-up, legs reach down)
- [x] Wings: lie flat when open and counter the body's lean (82 percent), pronate with the stroke; flap clip has a 34/52 degree arc with wrist and finger bowing (wingrig5.py in the session notes)
- [ ] Not looked at yet: landing sequence and braking in motion (only take-off crouch and spring were captured), acceleration surge, cruise glide gaps, clothing/hair trailing (secondary motion not added), banking when turning, one-leg-up superhero pose was dropped for the streamlined legs in the spec
- [ ] Bone axes were measured by experiment (local Z bends joints; right arm mirrored); if the rig changes, redo
- [x] Hover reworked from the Winged_Humanoid_Animation_Directives doc (2026-11-10): open wings stay parallel to the body (no flat-roll, no counter-lean), body leans 30 degrees in hover (30 + 2.5 per m/s, max 52), separate `Wing_Hover` clip (0.53 s, arc 66 down / 64 up, tucks 50 percent on the upstroke, sweeps forward on the downstroke) in a new `Hovering` state with a `Hover` bool; forward flight keeps `Wing_Flap` (0.8 s, 46/52). Tips reach about 0.3 m above the ground when the character hovers at 0.6 m. Rig script wingrig6.py in the session notes folder
- [ ] From the same doc and not built yet: turns/banking, strafing, barrel roll and evasive dodge, dive with swept-back wings, glide losing altitude, hair/cloth/feather trailing

## Secure art in Addressables, 2026-11-11 (Docs/SecureArt.md)
- [x] Design: paid art only in encrypted Protected_* Addressables groups, loaded by address, shipped through the
  updater; the repo ignores Assets/Secure, the Addressables settings and the built bundles.
- [x] `SecureArt` loading API + `SecureArtAddresses` (UOClient), Addressables backend, `EncryptedAssetBundleProvider`
  and `PatchPaths` (assembly gated on the package), `SecureArtBundles` build/encrypt/verify menu.
- [x] Call sites resolve by address when their field is empty: NpcAppearance, PlayerAppearance, Corpse, GameLook,
  GroundItem, BoatService, HousingService, GameAuthenticator, BakedWorldView; ActorModel falls back to the capsule.
- [x] `Assets/Secure/` added to ReleaseBuilder.ProtectedRoots (BK moved there from Assets/BK, so it had dropped out).
- [ ] Install Addressables (Package Manager; a download, user's OK), check the gated assemblies compile.
- [ ] Run Tools/SecureArt/SetupSecureArt.eval.cs; build content (UO Client > Secure Art); play-test with
  Use Existing Build.
- [ ] Run Tools/SecureArt/ClearDirectArtRefs.eval.cs (dry run, then for real) with the Game scene open.
- [ ] Release: the updater's PatchBuild calls SecureArtBundles.EncryptProtectedBundles; change its build path
  from ServerData to AddressableContent (ServerData holds our server saves); retire content.pak with the old World scene.
- [ ] Old World scene: ContentPack.WorldViewFields now lists _grass, _creatures and _boatCatalog, but ContentPack
  has no such fields, so a release build clears them from the scene without carrying them in the pack (creatures,
  boats and VFX grass would be missing in a release of the World scene).

## UI icons, 2026-11-11 (data: Content/Icons.json; art in Assets/Secure/Icons, loaded through SecureArt)
- [x] Skill, spell, racial and weapon-ability icons chosen by eye from the 6000 Fantasy Icons pack (229 icons, every
  Skills.json skill, every Spells.json spell, Fly, all 33 weapon abilities; opaque-background art only, none used
  twice). Synty's interface icons are all transparent glyphs, so none fit the "no transparent backgrounds" rule.
  Tools/Icons/place_icons.py copies them to Assets/Secure/Icons/<category>/<slug>.png (address icon/<category>/<slug>).
- [x] Icons on the Skills and Spells tabs (beside the names) and on the hotbar (instead of the name; the name is the
  tooltip, and shows only when a build has no icon). Same rounded frame as the hotbar and bag slots.
- [x] Equipment pictures rendered live from the gear (UI/Icons/ItemIcons.cs): the item dressed on a mannequin by
  the same SidekickDresser as worn gear (outfit parts, colour map, hue, metal-only armour tint), the bare body hidden,
  filmed on black. Cached per item kind + hue + wearer; one render a frame. Backpack, bank, corpse, pack animal and
  paperdoll slots; stack counts bottom centre with an outline.
- [x] Control prompts (UI/Icons/ControlPrompts.cs): "{Use} Open" shows the player's bound key as an icon, follows
  rebinding. In use on the door/loot/talk prompts, notices (server notices too), build-mode banners and help line.
- [ ] Run in the editor: Tools/Icons/IconImport.eval.cs, then Tools/SecureArt/SetupSecureArt.eval.cs (needs the
  Addressables package) so the icons are in Protected_Icons.
- [ ] Non-equipment items (reagents, potions, gold, scrolls, tools) still show their name: no 3D model to render.
  The pack's loot/resource icons could cover them (would need choosing like the spells).
- [ ] Weapon abilities have icons and addresses (GameIcons.Ability) but no UI shows weapon abilities yet.
- [ ] Prompt icons are keyboard and mouse only; a gamepad binding falls back to the keyboard one.

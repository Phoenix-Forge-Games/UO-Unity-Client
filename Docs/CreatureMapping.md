# Creature models: UO bodies → Synty models (2026-11-09)

**Status:** in the client. The catalog (`Assets/UOClient/Data/CreatureCatalog.asset`) is built by `Tools/Creatures/CreatureCatalog.eval.cs`, which is the source of truth for the exact body → model, height and tint choices. The POLYGON packs were brought in with `Tools/Creatures/unitypackage.py extract`: only the creature prefabs and the files they reference, with no scenes or environment sets and without overwriting shared Synty files already in the project. SIMPLE packs were skipped. Every POLYGON creature except the Dog Pack is humanoid-rigged and uses the humanoid locomotion controller.

Census: `Docs/CreatureCensus.md` (about 260 bodies, the top 60 cover 81% of spawns). The pack audit read every candidate pack's Asset Store file list and its Synty store description.

## Owned packs with rigged creatures (SyntyPass)

| Pack | Useful creatures | Animations |
|---|---|---|
| POLYGON Dog Pack | Wolf, Fox, Coyote, Hellhound, zombie dogs, 10 dog breeds; saddle/armour attachments | yes, 77 clips |
| POLYGON Fantasy Rivals | Troll, Big Ork, Barbarian Giant, Red Demon, Spirit Demon, Evil God, Medusa, Elemental/Fort/Mechanical Golem, Forest Guardian | no (Mecanim) |
| POLYGON Dark Fantasy | Demon x2, Gargoyle, 5 skeletons | no |
| POLYGON Dungeon Pack | Ghost x2, Tormented Soul, Rock Golem, skeletons x4, goblins x6 | no |
| POLYGON Dungeon Realms | Big Demon, Demon Skeleton x3, Undead Knight | no |
| POLYGON Dark Fortress | Behemoth, Colossus, Undead x2, Corpse, Wraith, Necromancer, Plague (Lord) | no |
| POLYGON Boss Zombies | Brute, Blobber, Slobber, Wretch | no |
| POLYGON Werewolf | Werewolf, Undead Werewolf | no |
| POLYGON Goblin War Camp | goblin camp + Troll | no |
| POLYGON Pirate Pack | Shark, Fish (swim anims), skeletons x3 | swim only |
| POLYGON Ancient Egypt | Mummy x3, animal-headed gods x5 | no |
| POLYGON Kaiju | Gorilla, Lizard, Crab, Alien (giant, humanoid rig) | no |
| SIMPLE Farm Animals | Pig, Sheep, Cow, Bull, Horse x5, Chicken, Rooster, Duck, Goat | yes |
| SIMPLE Forest Animals | Fox, Wolf, Rabbit, Boar, Bear, Moose, Doe, Stag, Skunk, Raccoon | yes |
| SIMPLE Cats / SIMPLE Fantasy | cats; Horse, Undead Horse, Stag, Boar | yes |

SIMPLE is a plainer art style than POLYGON (flatter, rounder). It's the only owned source of farm animals, deer, bears, rabbits, cats and horses.

## Proposed mapping (top bodies)

| UO body (share) | Model |
|---|---|
| orc 17, ogre 1 | Fantasy Rivals Big Ork; Dungeon goblins for small orcs |
| ettin 18 | Fantasy Rivals Barbarian Giant |
| troll 53 | Fantasy Rivals Troll |
| goat 209, sheep 207, pig 203, cow/bull 0xD8/0xE8 | SIMPLE Farm |
| boar 290 | SIMPLE Forest/Fantasy Boar |
| wolves 225/25/23/34, dog 217, hellhound 98 | Dog Pack Wolf/Hellhound/breeds |
| bears 211/167/212/213 | SIMPLE Forest Bear (tinted) |
| hind 237, great hart 234 | SIMPLE Doe, Stag |
| rabbit 205 | SIMPLE Rabbit |
| cat 201 | SIMPLE Cats |
| chicken 208 | SIMPLE Chicken |
| skeleton 50, skeletal knight 147, bone knight 57, skeletal mage 148 | Dark Fantasy / Dungeon skeletons |
| zombie 3, ghoul 153 | Boss Zombies, Dark Fortress Undead/Corpse |
| spectre/wraith 26 | Dark Fortress Wraith, Dungeon Ghost |
| reaper 47 | Fantasy Rivals Forest Guardian |
| gargoyle 4 | Dark Fantasy Gargoyle |
| daemon 9, balron 40 | Fantasy Rivals Red Demon, Dungeon Realms Big Demon |
| earth elemental 14, stone 1 | Dungeon Rock Golem, Fantasy Rivals Golems |
| water/air/fire elementals 16/13/15 | Fantasy Rivals Elemental Golem, tinted + VFX |
| lich 24 | Dark Fortress Necromancer |
| mummy 154 | Ancient Egypt Mummy |
| gorilla 29 | Kaiju Gorilla (scaled down) |
| horses (mounts) 200/204/226/228 | SIMPLE Horse (no POLYGON horse exists) |

## Gaps (no owned model)

Sea serpent 150 (5.6% of spawns), birds/eagles 6/5, panther/cougar/snow leopard 214/63/64, lizardman 35, corpser 8, ratman 42, harpy 30, giant spider 28, rats 238/215, snakes/serpents 52/21/92, dolphin 151, mongbat 39, gazer 22, llama 220, alligator 202, slime 51, scorpion 48, drake/wyvern/dragon 60/62/12, imp 74, beetles 242, frogs/toads 80/81, and others further down the list.

Possible sources: Asset Store packs matching the POLYGON style (paid), or placeholder models (tinted capsules or blobs) until then.

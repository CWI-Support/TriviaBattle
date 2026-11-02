# 3D models: where they come from

Placeholders for the 3D battle scene (`js/battle3d`) until the real art exists. All are CC0 (public
domain): free for commercial use, no credit required. Credit is still nice, so here it is.

| Files | Pack | Author | Source |
|---|---|---|---|
| `characters/knight.glb` | KayKit Adventurers Character Pack 1.0 (Knight) | Kay Lousberg | https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0 |
| `characters/skeleton-minion.glb` | KayKit Skeletons Character Pack 1.0 (Skeleton Minion) | Kay Lousberg | https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0 |
| `room/*.glb` | KayKit Dungeon Remastered 1.0 | Kay Lousberg | https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0 |
| `characters/slime.glb` | Slime | Quaternius | https://poly.pizza/m/LyjSUKHKnh |
| `characters/rat.glb` | Rat | Quaternius | https://poly.pizza/m/iltq5bVNaV |
| `characters/bat.glb` | Bat | Quaternius | https://poly.pizza/m/hNO9XvjlKa |
| `characters/goblin.glb` | Goblin | Quaternius | https://poly.pizza/m/OdCOFSmEhl |
| `characters/orc.glb` (stands in for the Goblin Quizmaster) | Orc | Quaternius | https://poly.pizza/m/5vO2YJsPEf |

The knight has all its gear as separate parts (shields, swords, helmet, cape); `js/battle3d/models.js`
picks which show. To swap in new art, put the .glb here and point its entry in `models.js` at it, with
the names of its idle, attack, hurt and death clips.

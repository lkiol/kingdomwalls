# Kingdom Stone Walls — 0.14.22

For the installed WorldBox 0.51.2 and NeoModLoader.

## Blocked combat targets in 0.14.22

Monster crowds near a gate could still chase units or buildings behind adjacent wall tiles. The ten-attacker limit did not cover those chases, so the fighting task could repeatedly search for routes through the wall. Target selection now checks the actual blocking wall and gate line, redirects blocked chases to a nearby gate with room, and clears them when no gate is available. Monsters beside a full gate pause target searches for one second while stationary; moving or losing the obstruction allows another search. The gate still admits ten simultaneous attackers. Restart WorldBox to load this version; existing saves need no editing.

## Gate attacker limit in 0.14.21

Each closed gate admits up to ten simultaneous attackers. The limit is applied when units select the gate and before they ask for a route, so the remaining crowd does not repeatedly pathfind to the same entrance. Fighters who see an enemy behind a full gate drop that blocked chase and choose another action. A dead or redirected attacker frees a slot; destroying the gate clears its roster. This should reduce large-siege stutter, but a gate may take longer to break. Restart WorldBox to load the update; existing saves need no editing.

## Gate siege route lag in 0.14.20

The “lag” save has hundreds of cold ones crowding the damaged east elven gate. When one could not reach a gate approach, the fighting task kept asking for a route; wall construction elsewhere could also clear the old two-second pause. Failed routes to an intact enemy gate now retain the original gate target, limit each A* search, and retry after 2, 4, 8, then 16 seconds while the unit stays on the same tile. Moving the unit or destroying the gate permits a fresh route immediately. Other route behavior remains unchanged. Restart WorldBox to load the update; existing saves need no editing.

## Gate chase lag in 0.14.19

When a unit chases an enemy behind a closed gate, it now switches its attack to that gate if the gate crosses the chase line. The fighting task keeps the gate target while it remains attackable. Failed routes near walls or gates pause for two seconds before another path search, and retry immediately when the unit moves or a wall changes. Restart WorldBox to load the update; existing saves need no editing.

## Hostile creatures at gates in 0.14.18

Skeletons and other hostile non-animal mobs can now target and attack closed enemy gates even when their native creature asset does not attack ordinary buildings. Gate searches examine nearby gate chunks instead of every gate in the world, reducing repeated work during large attacks. Civilization wars retain their existing gate rules. Restart WorldBox to load the update; existing saves need no editing.

## Vegetation beside walls in 0.14.17

Trees and other plants no longer close the narrow passages beside finished walls. WorldBox's native actor collision treats terrain as solid but does not treat vegetation buildings as walls; the mod's former tree anchor could therefore trap a unit next to a wall until the tree was removed. Mountain and map-edge gap seals remain. Existing saves need no editing; restart WorldBox to load this version.

## Resource targets beside walls in 0.14.16

Gatherers now skip mineral deposits, fruit, hives and other harvestable objects when a finished wall, closed gate or sealed gap leaves no route to the resource. The existing tree check uses the same filter. When a target is skipped, WorldBox can choose another resource or task instead of retrying the blocked one indefinitely. Existing saves need no editing; restart WorldBox to load this version.

## Woodcutters targeting trees across walls in 0.14.15

WorldBox's woodcutting task picked an unclaimed tree in a city zone using only the map island check. A worker could repeatedly choose a tree across a completed wall even though the wall pathfinding hook could not route there. The tree picker now skips trees without a wall-safe route, then continues through the remaining trees and zones. Trees with a clear direct approach avoid the extra path search. Existing saves need no editing; restart WorldBox to load this version.

## Units stuck in walls in 0.14.14

In the “suck on walls” save, one living soldier is saved directly inside a completed orc wall tile. WorldBox can register a moving unit in its next tile before its physical position arrives, so the old completion check could miss someone still standing on a wall site. Wall completion now checks actual unit positions as well as the tile's unit list. When an existing save loads, units already inside finished walls move to the nearest available ground tile and resume their AI. The saved map itself does not need editing; restart WorldBox to load this version.

## Builder reach in 0.14.13

Wall and gate builders can now work from a reachable tile up to two tiles from a scaffold, twice the previous one-tile reach. Builders still prefer the closest reachable work tile, and existing wall plans and saved construction progress are unchanged. Restart WorldBox to load the update.

## Gate sprite error fix in 0.14.12

World generation could reuse a registered gate `Building` instance for a tree or flower before the gate registry was cleared. The sprite hook then looked up the vegetation asset in the gate sprite dictionary and raised `KeyNotFoundException`, interrupting map generation. Gate rendering now verifies the current asset and falls back to WorldBox for non-gates. Reassigned objects and stale ownership entries are also removed from the gate registry. Existing saves need no migration; restart WorldBox to load the update.

## Builder route fix in 0.14.11

The latest “Death and Blood” save contains seven unfinished orc wall sites. Several older sites have no progress even while newer neighbors are being built. Builders now reserve only a scaffold side that WorldBox's pathfinder can reach around finished walls and gates. A site with no reachable worker waits 15 game seconds before another attempt, letting crews try other jobs. A worker whose distance to its assigned side stops improving for 30 game seconds is reassigned. Existing saves need no migration; restart WorldBox to load this version.

## Builder work lanes in 0.14.10

On the latest “God’s Isles” save, the dwarven and human capitals have a few paid wall scaffolds left near otherwise closed outer walls. A mason outside a ring was repeatedly sent to the scaffold’s center-facing side, which it could not reach. Masons now approach a wall job from the side nearest their position when assigned, and keep that lane until the job ends or is reassigned. This changes no wall plans, costs, progress, gates, or movement rules. Restart WorldBox to load the update; the save needs no migration.

## Large-save lag reduction in 0.14.9

Long actor routes now consult the wall/gate spatial index along the route instead of scanning every chunk in the rectangle between the route endpoints. The existing supercover, diagonal-corner, closed-gate, and narrow-gap checks still run where a wall or gate can affect movement. One-step A* neighbor checks retain their original index fast path. Building render and city hooks now identify registered gates without reading the asset of every ordinary building and plant through reflection.

The latest manual “God’s Isles” save has 888 actors, 437 finished wall tiles, and 20,327 buildings. In a read-only wall-index model of 2,997 actor-to-actor routes from that save, the spatial check skipped 1,038,249 of 1,131,592 candidate tile collision evaluations (91.8%). The model excludes gate footprints and is not an in-game frame-rate measurement. The mod compiles against the installed game assemblies, and native movement/path recovery checks pass. Restart WorldBox to load the update; existing saves need no migration.

## Actor path error fix in 0.14.8

The latest WorldBox log repeated 298 null reference errors while actors requested paths after city walls appeared. The movement hook now checks the destination's tile type and the game's pathfinding services before calling the native route method. If a stale route still triggers a null reference, the actor receives a normal failed path, its partial route is cleared, and the same request pauses for two seconds before a retry. The first three recovered errors are logged with tile coordinates for diagnosis. Restart WorldBox to load the fix. Existing saves need no reset. The source compiles and the guard/recovery tests pass against the installed game assemblies; live gameplay still needs an in-game check.

## Mine stone yield in 0.14.7

Stone deposits created by any civilization's mine now give four times their normal stone when a worker gathers them. The miner trait bonus is included in the multiplier. Naturally occurring deposits and other mine resources retain their usual yields. Already spawned deposits keep their previous yield; new deposits are marked when generated. Restart WorldBox to load the patch. Existing saves need no reset.

## Pathfinding performance in 0.14.6

Wall collision checks now traverse tiles without allocating a new callback for every pathfinding step. This preserves the same diagonal corner, gate and narrow-gap rules while reducing garbage collection pressure in worlds with many units. Restart WorldBox to load the update; existing saves need no reset.

The `[PT] Not found quit_game` log line comes from WorldBox's own PowerTracker when the quit dialog lacks a localized title component. PowerTracker still records the window through its fallback path. It appears during exit and is unrelated to simulation lag.

## Narrow wall gap pathing in 0.14.5

Units can no longer use one or two tile gaps between a completed wall and the map boundary or a mountain. Version 0.14.5 also sealed gaps by trees; 0.14.17 removes tree anchors because growing vegetation could trap units. The remaining seal applies to normal route searches, straight movement, diagonal corners, and movement already in progress. Wider openings and functioning gates remain passable. The fix takes effect on existing saves after restarting WorldBox; it does not change terrain or remove vegetation.

## Wartime scheduler fix in 0.14.4

Cities in a kingdom at war now stop wall crews and supply runs before scanning planned wall rings and gate footprints. Existing construction sites keep their progress and resume when peace returns. The direct building-work hook also refuses wall and gate progress during war. This avoids repeating the scheduling work across the 30-city “Final” manual save. Restart WorldBox to load the update; the save needs no changes.

## Gate health in 0.14.3

All gate types have twice their previous health. Wooden town, human capital and elven capital gates now have 5,000 HP. Human and elven stone gates have 10,000 HP. Dwarven capital wooden gates have 10,000 HP and dwarven stone gates have 20,000 HP. Wooden gates remain at half the health of their stone versions. On first loading an older save, existing gates' current HP is doubled once so their damage percentage is preserved; newly built and upgraded gates receive their new full HP.

## Wall and orc gate fixes in 0.14.2

Elf and dwarf style refreshes now repaint only walls that are already stone. Wooden walls retain their material until a paid stone upgrade job finishes. Orc capitals schedule no gate construction or supply, and gates already in an orc capital are removed after loading or a change of ownership. Unfinished gate jobs refund their materials. The existing world save is not modified by installation.

## Orc fortifications in 0.14.1

Version 0.14.1 had orc capitals build wooden log walls and gates. It did not schedule stone upgrades for orcs. If an older save had stone wall tiles or gates owned by an orc capital, the mod changed them back to wood on the same saved footprint. Version 0.14.2 removes the orc capital gates.

## Capital fortification progression in 0.14.0

Capitals now build their existing wall plans in wood first, using one wood per wall section. After every planned section is enclosed, non-orc capitals upgrade each wooden wall tile to its race's stone style for one stone. The original terrain snapshot, saved wall coordinates, ring order, gate openings, and reserved building lanes stay in place. Existing stone walls count as already upgraded.

In version 0.14.0, capitals built wooden gates for two wood each. Human and elven gates upgrade in place for two common metals after their wall ring is stone; dwarven gates use five gems. Orc gates stayed wooden until the 0.14.2 removal. Wooden gates have half the health of their stone versions. Capital towers wait until the stone walls are complete. Non-capital towns retain their wooden-only walls and gates.

Saved plans need no reset. Restart WorldBox to load the new mod version. The source compiles against the installed game assemblies; live construction and save/load behavior still need an in-game check.

## Town wall reservations in 0.13.4

New non-capital towns reserve their future wooden wall and both gate openings as soon as the town is founded. Wall construction still waits until the population exceeds 200. The reservation is a five-tile-wide square lane at radius 16, including the exact two-tile building clearance needed by the wall planner and gate foundations; the interior remains available for buildings. The reserved center is saved so the lane does not move as the town grows. Existing town plans keep their center and previously planned segments. On loading a save, omitted town segments are added only where the wall tile and its clearance have since become safe.

Read-only inspection of the "Moon and Lemons" save found Valocenid with 36 of 110 planned wooden wall segments, neither gate, and six ordinary house centers on planned wall tiles. Iba has 96 of 110 segments and both gates, with no ordinary building center on a planned wall tile. The fix prevents new buildings from taking future wall space; it does not move or remove the six existing houses. If those houses are removed in the game, reopening the save lets the planner retry the freed sections. Restart WorldBox to load the update. No save reset is needed.

## Path recovery in 0.13.3

AI can briefly target a tile whose region or island is not indexed. WorldBox's movement routine reads those references and can throw repeatedly instead of reporting a failed path. The mod now returns an ordinary path failure for that destination and keeps its path repair step from retrying it. Restart WorldBox to load the update; saved worlds need no migration.

## Four human gatehouses in 0.13.2

Human capitals now build one outer stone wall and four short gatehouse corridors, one on each side. Each gatehouse has an inner and outer gate, for eight gates total. The old continuous inner wall is removed from existing human saves while the gatehouse side walls and outer wall are planned. Unfinished inner-wall jobs refund their materials. Towns keep their wooden wall layout and two gates. Restart WorldBox to load the update.

## Two town gates in 0.13.1

Town walls now leave only north and south gate openings. The east and west sides are continuous wooden walls. New town plans build two gates; a saved 0.13.0 town plan removes its east and west gates and plans wall sections across those gaps when the world loads. Unfinished removed gates refund their wood. Restart WorldBox to load the update.

## Wooden town fortifications in 0.13.0

Non-capital cities with **more than 200 people** can now build a compact wooden wall, using the capital wall planning and citizen construction system. The town plan has one square ring at radius 16, smaller than the human capital's outer perimeter at radius 32. It leaves two nine-tile openings for wooden gates. Each town gate has 2,500 health, half the regular capital gate's 5,000, and costs 2 wood. Wall sections cost 1 wood each. All four race types use the same town plan.

Town workers pause if population falls to 200 or below and resume if it rises again. Finished walls and gates stay in the world. Existing saves require no reset. The source compiles against the installed game assemblies; live gameplay still needs an in-game check after restarting WorldBox.

## Wall-building races in 0.12.7

Open the mod's options in the in-game NeoModLoader menu. **All races build walls** is on by default. Turn it off to allow only humans, orcs, elves and dwarves to start or continue capital and town wall plans. The switch is saved by NeoModLoader and takes effect during play. Existing completed walls remain in the world; the option controls future building work. Turn it back on to resume wall planning for other races.

## Gate scheduler fix in 0.12.6

The latest log reported an `IndexOutOfRangeException` in `Gates.WorkTiles` while checking for gate rebuilds. After a city's race changes, its active wall plan can still contain one ring while the current race's gate layout requests two. Gate scheduling, supply requests, and footprint reservations now require a matching active ring layout. Wall construction can continue without pausing the mod. Restart WorldBox to load the fix; saved worlds are unchanged.

## Reliability and performance sweep in 0.12.5

New gates leave the movement index while they are under construction and enter it when complete. Couriers withdraw from the selected donor stockpile, keep transfers within its available stock and the destination's capacity, and limit trade to the seller's gold storage room. Missing terrain assets now leave wall, road, and gate checks safely unavailable instead of causing null reference errors. Elven and dwarven footprint reservations skip distant locations before checking the layout. Existing saves need no migration; restart WorldBox to load the update.

## Courier stockpile fix in 0.12.4

The game puts windmills and other storage buildings in a city's general `storages` list. Supply couriers previously selected from that list, so a gem shipment could go to a dwarven windmill instead of the stockpile needed for the gate. Couriers now choose source, destination, and trade-payment buildings from the city's `stockpiles` list and reject a delivery destination outside that list. The saved game is unchanged; restart WorldBox to load the fix.

## Gate trade reliability in 0.12.3

The “test 7” save has all 239 dwarven wall tiles complete, no dwarven gate, zero gems in the dwarven capital stockpile, and 510 stored gold. Elven stockpiles contain surplus gems, including 58 in the closest eligible seller and 172 in the elven capital. Their land connects to the dwarves. Gate supply is now requested when its completed wall and terrain permit construction even while citizens temporarily occupy the gate footprint. A supply runner now times out only after 90 world seconds without moving closer to its current stop, allowing the long trip to elven territory. The gate still waits for walls to finish and for its footprint to clear before placement. The save is not modified; restart WorldBox to load the update.

## Single-ring supply error fix in 0.12.2

The new “test 7” log reports repeated `IndexOutOfRangeException` errors in `Supply.Needed`. Its two-ring tower supply check accessed ring 2 for elven and dwarven capitals, which each have one ring. The check now runs only for two-ring plans. Wall and gate supply runs for single-ring capitals continue as before. Restart WorldBox to load the fix; the save does not need changes.

## Supply runner render error fix in 0.12.1

The latest “test 7” log repeatedly reports a null reference in `ActorManager.precalculateRenderDataParallel`. Version 0.12.0 assigned supply runners a null forced hand tool. WorldBox compares that field with an empty string, so it attempted to render a nonexistent tool and dereferenced its missing asset. Supply runners now use the empty string to indicate no forced tool. Existing saves do not need changes; restart WorldBox to load this fix.

## Supply and performance update in 0.12.0

When a capital runs short of wall stone or wood, gate iron or gems, or tower materials, it assigns a citizen to bring supplies home. The courier first takes surplus from another city in its kingdom, then gathers a matching resource from outside the capital's border. If neither is available, it can buy surplus from a peaceful foreign city using the native resource's gold trade price. Citizens carry the materials in their inventories and deliver them to a stockpile with room. Walking trips currently require the source and capital to share an island; foreign purchases need sufficient stored gold and a seller with storage space for it.

The construction scheduler checks three times as often, while gate ownership refresh, tower cleanup and race migration checks run less often. Movement checks use a small spatial index, so actors traveling away from any wall or gate avoid repeated line tracing. The index is rebuilt on world load and updated as walls and gates change. These changes target the mod's frequent work in crowded saves; the actual frame rate in “test 7” still needs an in-game measurement after restart.

## Wildlife gate targeting fix in 0.11.6

The “test 7” save has a rabbit with the `savage` trait beside a damaged human east gate: rabbit (173,437), gate (172,438), gate health 730/5000. The gate attack hook allowed any actor into native building combat checks, and the target search considered every live gate for each actor. Wildlife can be hostile to units, but it should not select or attack a city's gate. A blocked animal could also repeatedly retry a route through that gate.

Only civilization actors can now select, retarget to, or attack gates. Gate target searches skip noncivilization actors before scanning gates and check distance before invoking native attack logic. When an actor cannot siege a closed gate, its blocked movement task ends so its AI can choose another action without the costly fallback route search. Enemy civilization units still siege; friendly units still pass. The save is unchanged.

The current game log loaded 0.11.4 and has no repeated exception during the “test 7” session; the previous log's repeated `ActorMove.goTo` error is separately addressed by 0.11.5. The new source compiles against the installed game, and a focused regression reproducing a hostile noncivilization rabbit passes. Restart WorldBox to load 0.11.6; live frame time still needs to be checked in game.

## AI movement error fix in 0.11.5

The latest WorldBox log repeatedly reported a null reference in the patched `ActorMove.goTo` path during `BehGoToTileTarget`. The movement prefix now returns a failed route to the AI when the actor, destination, current tile, asset, or path list is missing. Gate checks also ignore an incomplete building with no saved data. This prevents those invalid states from entering native pathfinding, where they could throw on every AI update. Existing gate approach and valid route behavior remain the same.

The source compiles against the installed game, the saved army route regression passes, and the Harmony method bindings validate. A live game session is still needed to confirm that the repeated log exception has stopped.

## Tower cleanup, peacetime rebuilding and capital borders in 0.11.4

Destroyed elven and dwarven towers now lose their sprite immediately and are removed from the building registry. Saved ruined towers are cleaned up after loading. Missing gates and towers can be rebuilt once their planned walls are complete and the owning kingdom is at peace. Gate work started as a rebuild pauses if war begins. Gate material costs stay at 2 iron or 5 gems; replacement towers consume their native race tower construction cost. Towers wait until all planned rings finish, preserving wall materials during construction.

The current capital reserves and claims the tile zones needed by its planned walls, gate openings and tower foundations. Unclaimed zones are taken first; same-kingdom village zones can be transferred through the game's city ownership method, which updates the village center as needed. A village's last zone is preserved. Other kingdoms' zones are never transferred. This addresses the unclaimed border zones visible in the saved “test 6” plan without changing the save file.

Restart WorldBox and reload “test 6”; no wall reset is needed. Native assembly compilation and save inspection pass. The in-game result still needs a live check after restart.

## Army movement recovery in 0.11.3

The native army movement shortcut can accept a direct route through a completed wall. The previous collision guard stopped the unit and cleared its route and destination, leaving its movement task waiting. Routes now receive a wall check after native planning. Blocked routes use tile pathfinding without the native region restriction so armies can reach an open passage. If a wall finishes during an accepted march, the unit keeps its destination and resumes on a safe route. An unreachable destination returns failure or releases the waiting task so the AI can choose another action. Physical position is used for collision checks because the game can advance `current_tile` before arrival.

Recovery uses the existing native terrain rules, wall collision and gate permissions, with a 2,048-node frontier limit for its fallback search. Ordinary clear routes keep the native movement flow. The one-resource wall price and current-capital construction rule remain in effect.

Validation: reproduced the old stopped-route behavior using the original 0.11.2 guard. Extracted production checks pass with terrain and elf army positions copied from the saved “test 6” world, including 36 blocked routes through the saved orc wall passages. Mid-march recovery, unreachable destinations, gate permissions, construction and all race layout regressions pass. Compiled against the installed game; native method bindings and AStar guard insertion pass. The save is unchanged. Restart WorldBox and reload “test 6”; the battle itself still needs a live gameplay check.

## Cheaper walls and current capitals only in 0.11.2

New wall sections cost **1 stone** for humans, elves and dwarves, or **1 wood** for orcs: twice the sections for the same materials. The 20-resource reserve and citizen hammer work remain unchanged. Gates keep their existing separate prices (2 iron, or 5 gems for the dwarven gate); towers remain free.

Fortification planning, wall and gate work, layout migrations, tower placement and building reservations now apply only to each kingdom's **current capital**. The old saved city selection cannot override that rule. If the capital moves or a city is captured into a kingdom where it is not the capital, its workers stop, its planned foundations are released, and the current capital receives its own plan. Ordinary towns keep their native tower rules. Existing completed walls and gates remain; paid unfinished sites keep their progress and can resume if that city becomes a capital again. Old paid wall sites refund their original 2 resources; new sites record and refund 1.

Restart WorldBox and load the existing save; no reset is needed. Validation is described with the release delivery; live gameplay after restart remains to be checked.

## Nighttime orc scaffold fix in 0.11.1

The latest September 27 log loaded 0.11.0 successfully, then recorded 580 identical `NullReferenceException` errors in `DynamicSprites.getBuildingLight`. The orc scaffold renderer returned custom sprites without setting `Building.last_main_sprite`; the native nighttime window-light lookup hashes that field directly. The renderer now updates the cache with the current construction or finished sprite, matching the other custom race renderers. Existing saves, construction progress, costs and artwork need no reset or changes. Restart WorldBox to load the fix.

Validation: reproduced the exception using the original production orc renderer and the installed game's light lookup. The repaired production renderer passes finished/scaffold/finished transitions and leaves ordinary buildings untouched. Native lighting checks also pass for elf/dwarf sites and towers and custom gates; shared race construction/layout/combat regression checks pass. Live gameplay after restart has not yet been checked. The separate NeoModLoader compiler-generated listener warnings and Discord SDK startup error originate outside this mod.

## Dwarven square walls in 0.11.0

Dwarven capitals build one square wall with radius 32 tiles, four corner towers and a single big gate on the south side. The gate spans 17 tiles, has 10,000 maximum health, costs 5 gems and requires the existing 10 citizen hammer progress. It uses no iron or stone. Paid jobs preserve their progress through interruptions and loading; refunds return the original payment resource even after capture. Finished gates allow their kingdom through, block outsiders across the complete footprint and retain saved damage. Destroyed gates stay destroyed.

Wall sections keep the stone construction rules: 2 stone each and a 20-stone reserve. The four towers sit immediately inside the corners, clear of the wall work lane. They inherit the native dwarven tower combat behavior and fire the existing boat `cannonball` projectile instead of arrows (one shot every three seconds). Towers are placed complete and free after the planned square wall sections finish. Units, houses, foreign land and unsuitable ground delay placement; eligible vegetation is cleared. Native city construction cannot add extra dwarven towers.

Custom slate stone walls with gold runes, steel and gold cannon towers, matching scaffolds and a reinforced gemstone gate are generated at startup. All 12 exact runtime sprite exports are included as `art/dwarf-*.png`. Human, orc and elf artwork is unchanged. Gate renderers now also populate the native nighttime lighting cache with the current finished or construction sprite.

Restart WorldBox and load your existing save; no manual reset is needed. Prior dwarven double-square plans convert automatically. The old inner square, unwanted gates and old city towers are cleared one change per frame; unfinished removed jobs refund their original material. Shared outer-wall sections are kept and receive dwarven artwork. The new square keeps the saved center and reserves the gate and corner foundations.

Validation: compiled against the installed game and NeoModLoader. Extracted production checks passed for square geometry and diagonal sealing, the full 17-tile gate collision, 10,000 health, saved damage/destruction, gem payment/shortages/resume/refunds, four corner foundations, safe placement retries, migration and sprite scale. Shared human/orc/elf construction, gate and layout regressions passed. Actual native asset fields, dwarf architecture, resource APIs, projectile routing, Harmony bindings and nighttime lighting lookup were verified headlessly. The exported sprites were visually reviewed; live gameplay remains to be checked after restart.

## Elven nighttime renderer fix in 0.10.1

The September 27 crash log repeatedly reported a NullReferenceException in DynamicSprites.getBuildingLight. The custom elven renderer bypassed native sprite selection without populating Building.last_main_sprite, which the nighttime window-light renderer hashes. It now updates that cache with the selected finished or construction sprite on every render. Existing saves and construction progress need no reset.

The latest test save (save1, stored world name "Land and Misery") contains 120 of 284 elven wall sections and only 1 stone in the elven city's buildings. Its saved plan includes every section; the log reports 164 remaining owned, buildable sections and a failed stone-reserve check. Construction correctly waits for stone under the existing human rules (2 stone per section and a 20-stone reserve). The native infinite-resources setting bypasses that resource check; enabling it exposed the separate rendering fault. Costs, layout, tower placement and artwork are unchanged.

Validation: reproduced the exact null exception by calling the installed game's light lookup after the original production render prefix. The repaired production prefix passes finished/scaffold/finished transitions for elven towers and sites, and the installed light lookup IL succeeds in a headless check using managed sprite identity instead of Unity's native identity. Ordinary building caches stay unchanged. Compiled against the installed game; existing layout, construction, gate and migration checks passed. Live gameplay still needs verification after restarting WorldBox.

## Elven circular walls in 0.10.0

Elven capitals build one circular stone wall with a radius of 40 tiles (80 tiles across), replacing the two square rings. Four nine-tile gates sit at the cardinal directions. Sixteen towers are spaced evenly around the outside, with their entire foundations beyond the wall and its work clearance. Native city construction cannot add extra elven towers, and existing towers belonging to the planned elven city are removed from inside the circle.

Elves keep the human construction rules: each wall section costs 2 stone, retains a 20-stone reserve, and requires 10 citizen hammer progress. Gates cost 2 iron and have 5,000 health, allowing their owning kingdom through and blocking outsiders. Orcs still cannot use them. Towers inherit native elven combat behavior and are added complete and free once the circle's planned wall sections are finished. Occupied, foreign, unclaimed or unsuitable foundations wait; houses are preserved, while vegetation on an eligible tower footprint is cleared.

Custom pale stone walls with green leaf details, matching scaffolds and gates, and slim towers with pointed green roofs are generated at startup. The exact production sprite buffers are exported to art/elf-*.png. Human and orc artwork and behavior are preserved; dwarves await phase four.

Restart WorldBox and load the existing save. Saved elven square layouts convert automatically: the old squares and gates are cleared one change per frame, unfinished paid sites refund their original material, and a new circular plan keeps the saved city center. Interior towers are removed. Existing stone sections that coincide with the new circle acquire elven artwork. This migration intentionally replaces the old elven layout; no manual reset is needed.

Validation: compilation against the installed game passed. Production-code checks passed for a single circular perimeter, diagonal sealing, all four gate passages, sixteen exterior towers, reservations, native tower bans, placement retries, saved-layout migration, original-material refunds and sprite dimensions/transparency. Existing human/orc scheduler, gate, movement and migration regressions passed. Native Harmony bindings, city/AStar guards and the native elf architecture key were verified. Actual gameplay appearance and timing still need an in-game check.

## Orc log walls in 0.9.0

Orc cities build timber palisades with four bark variations and matching timber scaffolds. Each new section costs 2 wood, uses no stone, and retains the existing 20-resource reserve and 10-progress citizen hammer work. Existing ring sizes, wall collision, work clearance, scheduling and save progress remain the same. The exact procedural sprite pixels are exported in art/orc-log-wall*.png and art/orc-log-scaffold*.png.

Orcs do not build or pass through gates, including gates owned by their kingdom. Their existing nine-tile entrances stay open. Orc cities receive no gate towers, reserve no tower foundations, and native city construction cannot build towers. Existing towers belonging to a planned orc city are removed when construction runs. Existing gates belonging to orc cities are removed; unfinished gates refund their paid iron.

Existing owned stone sections in an orc wall plan convert to logs, one section per frame, without another payment or resetting the layout. Paid wall jobs resume their existing progress; refunds use the material originally paid even after capture. Shift+F8 also restores the original terrain beneath logs. Restart WorldBox and load the existing save; no wall reset is needed.

Human wall artwork and behavior remain unchanged from 0.8.2. Dwarves retain their previous behavior until their own phase; the subsequent elven phase is described above.

Validation: compiled against the installed game and NeoModLoader; production-code regression checks passed for wood accounting, job recovery, gate and tower restrictions, migration, terrain restoration and existing human construction/gates. Human sprite buffers match 0.8.2 byte for byte. Installed native Harmony bindings and tower-placement IL guard were verified. Live gameplay still needs verification.

## Gate scaffold scale and completion display in 0.8.2

Read-only inspection of the latest "wall mod test 5" save found four gate buildings with 5,000 health and no under-construction flag or pending progress. Citizen construction had completed, but the old render hook updated the main-sprite cache key prematurely, retaining the scaffold image. Native recoloring also rebuilt the sprites at one pixel per world unit, discarding the attempted size correction in 0.8.1.

Gate rendering now bypasses native recoloring in both the legacy and parallel/cached renderers. Scaffold and finished sprites use four pixels per world unit: the long axis spans the nine-tile opening, one quarter of the previously displayed width and height. Each render selects the current construction state directly, so hammer completion immediately displays the finished gate. Other building sprites retain native behavior.

Restart WorldBox and load your existing save. The four internally finished gates will display their finished artwork. Pending paid gate jobs still require citizen hammer work and preserve their progress; gates still cost 2 iron, use no stone, have 5,000 maximum health, allow their owning kingdom through, and block other entities until destroyed. Saved health and destruction are preserved. No wall reset or additional iron payment is needed for existing finished gates.

Validation: compiled against the installed game; extracted production regressions passed for both sprite orientations and construction states, cache isolation, ordinary-building fallback, citizen work, iron accounting, collision, combat, save recovery and placement safety. Verified Harmony bindings and the installed game's legacy and parallel/cached renderer paths. Live gameplay appearance still needs verification.

## Smaller, citizen-built gates in 0.8.1

Version 0.8.1 attempted to shrink gate sprites by changing their pixels-per-unit. Native recoloring discarded that scale; 0.8.2 corrects the rendering path. The entire nine-tile opening remains protected when a gate is finished.

Gates now join the existing mason crew instead of appearing finished and free. Each gate costs 2 iron (the installed game's construction resource ID is common_metals), no stone, and requires 10 native construction progress. Citizens travel to the gate, equip their hammer and contribute work using the same task as walls. Gates become eligible after their own ring is complete; inner gates can be built while the outer ring continues. An iron shortage does not stop stone wall construction. Gates under construction show scaffolding and remain passable until finished.

Iron is charged once when a paid gate job starts. Interrupted and saved jobs retain their progress and resume without another payment. Construction pauses with F8, danger, capture, fire, or unavailable citizens. House construction keeps its separate native job slot. Completed gates retain 5,000 health, owning-kingdom passage, enemy attacks, saved damage and saved destruction. Destroyed gates do not automatically respawn.

Replace the mod folder and restart WorldBox, then load your existing save. No wall reset is needed. Existing finished gates remain built, retain their current health and use the smaller sprites; the construction cost applies to new jobs. Shift+F8 refunds iron for unfinished gates as it clears the layout.

Compilation against the installed game passed. Extracted production checks passed for iron-only payment, real worker progress, pauses, retries, resumed jobs, house-job independence, ring ordering, placement safety, gate collision, combat, kingdom changes, saved damage and destruction. Native construction methods, resource mapping, Harmony parameter bindings and AStar insertion were verified against the installed assembly. This update has not been tested in live gameplay.

## Kingdom gates in 0.8.0

Each finished wall ring receives four iron-bound timber gates with stone pillars: north, south, east and west. One attackable native building spans all nine tiles of each opening, with 5,000 maximum health. Gate artwork has horizontal and vertical variants with crisp pixel filtering; matching PNGs are included in art/.

Any creature or citizen currently belonging to the owning kingdom passes through. Other kingdoms, allies and unaffiliated creatures cannot cross an intact gate. Hostile units can target and damage gates using native combat, including melee from any edge of the wide footprint. Gate collision also checks diagonal movement, direct movement, pathfinding and knockback. Ownership follows the capital city's current kingdom after capture, including while F8 has paused construction.

Gates appear when their ring is complete and the entire opening is owned, suitable and clear of units or protected buildings. They are added complete and free, following the existing tower placement workflow. At zero health the whole gate and collision footprint are removed. Health and destruction are saved; destroyed gates stay open instead of automatically respawning. Shift+F8 clears walls and gates and forgets the layout; F8 resumes planning. Existing native watch towers remain.

Replace the mod folder and restart WorldBox, then load your existing save. No wall reset is required. Gates populate eligible openings of existing completed rings. Compilation against installed WorldBox 0.51.2 and extracted production regression checks passed for membership, collision, combat approach/range, captures, saved health/destruction, placement safety and sprite buffers. Native Harmony parameter bindings and AStar insertion were checked against the installed assembly. Live gameplay still needs verification.

## Gate towers in 0.7.9

Two existing native watch towers flank the inside of each of the four inner-ring gate openings (8 total). No tower sites are reserved beside the outer ring. Foundations sit immediately beside the passage and at the closest inward position allowed by wall work clearance, minimizing space used inside the city. Tower foundations stay outside the nine-tile passage and the two-tile masonry work clearance. The native asset matches the capital's existing architecture. Each pair becomes eligible when its ring is complete; occupied, unclaimed or unsuitable ground waits without deleting buildings. Existing native towers at the exact sites prevent duplicates after loading. Towers are added complete and free, and remain native buildings when Shift+F8 removes the walls. Restart with this update and load the existing world; no reset is needed. Compilation and placement geometry checks passed; live gameplay has not been verified.

## Smaller scaffold in 0.7.7

Scaffolds use four pixels per world unit, reducing their width and height to one quarter of version 0.7.6. Finished wall sprites retain their 0.7.6 size. Construction and save handling are unchanged. Compilation passed; appearance still needs an in-game check. Replace the mod and restart without resetting the save.

## Sprite scaling in 0.7.6

Wall and scaffold sprites now use one pixel per world unit instead of sixteen, enlarging the artwork 16 times to match the pixel scale of the small houses. Point filtering preserves sharp pixel art. Construction, collision and saved plans are unchanged. Replace the mod and restart; existing saves require no reset. Compilation verified; visual scale still needs an in-game check.

## Completion retry fix in 0.7.5

Inspection of "wall mod test 5" found city 2 at 151 of 156 inner sections, with three recently created scaffolds in its remaining gap. The completion handler could delete and refund finished work when its final safety check failed, allowing the same section to be purchased and built repeatedly.

Completed, paid scaffolds now remain in place while conversion is blocked, and retry without another material charge. Finished neighboring walls are explicitly accepted by the clearance check. Ownership, building clearance and occupied-tile checks remain enforced. Stale or unfinished queue entries cannot convert a scaffold prematurely. Periodic diagnostics report the number of completed scaffolds waiting and the first waiting tile's blocker category.

Load the existing save after replacing the mod and restarting. No reset is needed. Compilation and extracted production regressions passed, including 100 blocked completion retries with no further placement or cost, followed by successful conversion when clear. Live gameplay has not been verified; the exact runtime blocker at the saved corner has not been confirmed.

## Vegetation and saved-plan fix in 0.7.4

The read-only inspection of walls mod test 3 found zero walls or scaffolds, a capital plan with only three outer segments, and another plan with no segments. The log reported plenty of available citizens and stone, but zero buildable owned sites. Trees and small plants are native Building objects; treating them like houses erased forested segments during planning and let later vegetation growth block construction.

Trees and small plants now permit planning, wall clearance and worker approach. Starting an owned, safe, funded wall job removes only vegetation on that wall tile before placing its scaffold. Houses, minerals, roads on the target tile, water and mountain clearance remain protected. Unclaimed and foreign-city tiles still receive no construction attempts or material charges.

On first load, pre-0.7.4 saved masks are expanded to include currently safe segments while preserving their existing segments and center. The usual connectivity check runs before accepting the expansion; if it would isolate land, the original mask is retained. Existing paid scaffolds and wall progress remain intact. No reset or manual save editing is required.

Validation: compiled against the installed game and NeoModLoader assemblies. Extracted production safety and scheduler checks cover forested jobs, sparse/all-zero mask repair, repeat-load stability, house clearance, native vegetation flags, ownership and paid scaffold recovery. Live gameplay has not been verified.

## Construction scheduling fix in 0.7.3

Citizens searching for a house job (`try_build_building`) or working on roads (`build_road`) can now join the wall crew. Active house builders, existing masons, hungry/sleeping/fighting citizens and children remain excluded. The old blanket rejection of any task containing `build` could leave a city without eligible masons.

Roads beside a wall segment are now valid clearance and approach tiles. Roads built after planning could previously make every remaining segment fail the safety check indefinitely. Walls still cannot replace a road, building, water or mountain tile. Building clearance remains two tiles.

The 0.7.2 ownership checks remain: new scaffolds and wall completion require the planned city to own the tile. Unclaimed segments wait for expansion without placement attempts or stone charges. Existing centers, masks and construction progress are preserved. No reset is required.

Diagnostic reports now include buildable owned sites, available citizens and whether the stone reserve is met, so a legitimate wait can be distinguished from a stalled scheduler.

Validation: compiled against the installed WorldBox/NeoModLoader assemblies; scheduler regressions cover house-search and road workers, active house protection, ring order, paid scaffold recovery, border expansion/loss and material accounting. Extracted production safety tests cover adjacent roads, target roads, house/terrain clearance and unclaimed tiles. The ownership helper was also exercised against the installed game types. Live gameplay has not been verified for this release.

## Border scheduling fix in 0.7.2

The scheduler previously allowed unclaimed land and land belonging to another city in the same kingdom. Those sections could repeatedly consume placement attempts while valid sections farther along the ring waited.

New wall jobs now require the tile to belong to the capital that owns the wall plan. Unclaimed sections remain reserved in the saved plan, but receive no construction attempts, workers or material charges until that city claims them. The scheduler skips those sections and continues with valid sites elsewhere on the active ring. Border expansion makes the saved sections available automatically.

If a border moves away from a paid scaffold, its mason is released and its construction progress is preserved. Reclaiming the tile resumes that same scaffold without charging materials again. Completed scaffolds also wait for ownership before becoming solid walls. Every planned inner-ring section must still become solid before the outer ring starts, so an unclaimed inner section holds the next ring until the border expands.

Close WorldBox before replacing the mod, then restart and load the existing test world. No wall reset or save editing is needed.

## Error fix in 0.7.1

Player.log showed repeated NullReferenceException errors in ActorManager.precalculateRenderDataParallel immediately after three wall masons received jobs. The installed game library's clone function keeps the task's "hammer" flag but skips its nonserialized cached hand-tool asset. Rendering therefore attempted to read the color settings of a missing hammer asset.

The wall task now initializes that asset from the native hand-tool library. Joining or leaving the wall crew also refreshes the citizen's item sprite cache. This fixes the missing renderer asset without changing the game's renderer or ordinary construction tasks. The dedicated crews, material costs, reserved lanes and inner-ring-first construction from 0.7 remain intact.

Close WorldBox before replacing the mod, then restart and load the existing test world. No wall reset or save editing is needed.

## Construction fix

Wall construction now has a dedicated crew of three to six adult citizens, independent of the city's normal house-building slot. The crew starts at 40 people, grows with population, and is capped at six. Houses can continue building while masons work on the active wall ring. Full housing and a long-running house job no longer prevent wall work.

The read-only inspection of the save named "walls mod test 2" found six completed wall tiles, no wall scaffolds, 269 stored stone in its capital, and an unfinished native tent. The previous version required spare housing and an idle native construction slot, which could indefinitely starve wall construction despite available stone. Version 0.7 removes both gates. The original save was not modified.

Masons use a separate copy of the game's actual house-construction task: they walk to a site, equip the hammer, animate construction, and contribute native building progress. The scheduler checks every three simulation seconds without restarting active tasks. Each tile still costs two stone and needs 10 construction progress; the city retains a 20-stone reserve. Timers never complete walls without citizen work.

Interrupted scaffolds retain their paid materials and construction progress. Another citizen can resume them without another charge. A worker who makes no progress or movement for 30 simulation seconds is released and temporarily excluded from reassignment. A worker who is still walking keeps the assignment. Smaller populations release excess crew members. Hunger, combat, danger, capture and fire take priority; children and citizens already building houses are not recruited.

Citizens hammer from an adjacent work tile. Terrain conversion waits only for occupants of the actual wall tile to leave, allowing nearby masons to continue working. This replaces the former two-tile unit exclusion buffer. Buildings still retain the two-tile clearance around the wall lanes.

## Layout, reservations and ring order

The capital receives two fixed square rings with radii of 24 and 32 tiles and four aligned nine-tile-wide gates. At most 376 wall tiles are planned. The inner ring encloses a 47 by 47 area; the reservations leave a central 43 by 43 area available for building foundations. Walls fortify the capital rather than tracing the changing kingdom border.

House lanes are reserved before the city reaches the 40-person construction threshold. New civilian building foundations and upgrades cannot overlap the two wall lanes, their work clearance, or connecting gate corridors. Existing buildings are left intact. Terrain, roads, existing buildings and foreign territory can leave additional gaps when a new plan is assessed.

The saved center and exact segment mask are preserved when loading older worlds. Work proceeds around the perimeter, and every planned inner-ring tile must become solid wall terrain before outer-ring construction starts. Finished scaffolds waiting for conversion still block the next ring. Older outer scaffolds remain paused until their ring becomes active. Destroyed inner segments regain priority.

Planning and legacy recovery are bounded and spread across frames. Only a small crew receives new jobs, and at most one finished wall tile converts to terrain per frame.

## Sprites and collision

Scaffolds display low masonry with timber supports. Finished walls display stone battlements. Matching previews are included in art/; the mod generates its sprites at startup. Natural building and mountain graphics remain unchanged.

Finished walls use separate mountain-derived terrain with explicit collision flags and no contact damage. Pathfinding, straight movement, diagonal corners, velocity and existing movement paths are checked against completed walls. Gate openings and unfinished scaffolds remain passable. Roads or biome overlays cannot conceal the wall type. Teleportation, powers and direct terrain editing are outside these movement checks.

## Install with 7-Zip

1. Close WorldBox fully.
2. Extract the KingdomStoneWalls folder from the ZIP into:
   C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\mods
3. Replace the previous mod files. Main.cs and mod.json must sit directly inside the installed KingdomStoneWalls folder, without another nested folder of the same name.
4. Restart with NeoModLoader and experimental mode enabled. Player.log should contain "Kingdom Stone Walls 0.13.4 loaded". NeoModLoader compiles the mod's C# sources; a separate DLL is not required.
5. Load your existing test world directly. A reset is not needed for this update.

The log reports the active ring, solid tile count, assigned masons and sections waiting for borders every 120 simulation seconds. For example, "masons 3/3" confirms the dedicated crew is assigned; it does not by itself confirm successful hammering. "waiting for borders 12" means 12 unfinished sections of the active ring are outside their owning city's territory and receive no construction attempts. If no unfinished section is owned, the crew waits for border expansion.

F8 pauses or resumes wall work while preserving reservations. Shift+F8 removes this mod's sites and restores wall terrain in batches, preserving ordinary houses. Wait for removal to finish, then press F8 to start a new plan. Before uninstalling, remove the walls this way and save after removal completes. Existing houses in old wall lanes are not automatically moved or demolished.

## Validation and limits

Compilation passed against the installed game, Unity, Harmony and NeoModLoader assemblies. A regression check reproduced the missing cache through the installed native clone function, then verified that the production wall-task factory restores the same hammer asset and that its renderer color lookup succeeds. The native house task and all four construction behaviours remain intact. Checks against native game IL passed for independent wall-site scheduling, city placement and pathfinding. Native sprite-preloader, building-foundation reservation and CityData serialization checks also passed.

The production ownership helper was checked against the installed WorldTile and TileZone classes. It rejects unclaimed land and other cities, accepts owned tiles, and observes border loss and reclaim immediately. Scheduler regressions passed for skipping unclaimed/other-city cells at the front of a ring while assigning three later owned sites with zero rejected placements; 101 ticks with only unclaimed inner sections causing zero placement attempts or costs; automatic starts after border expansion; and preserving paid scaffold progress across border loss, reclaim and deferred solid conversion. These are headless checks, not an in-game border-expansion replay.

Tests using the production scheduler with game API doubles passed for construction alongside full housing and an unfinished house, uninterrupted travel, hunger interruptions, stalled-worker replacement, resource reserves, population changes, strict ring order and pausing. Item sprite caches refresh on crew assignment and release, while uninterrupted tasks do not trigger repeated refreshes. A test using the actual saved center, mask and six walls from "walls mod test 2" preserved its 50 inner and 79 outer planned tiles and immediately assigned three inner-ring jobs despite the occupied house slot. This is a scheduler regression test, not an in-game replay or timing benchmark.

Geometry checks passed for translated layouts, all 376 unique segments, gate connectivity, 4,000 building-footprint cases, strict ring sequencing, 2,000 movement intersection cases and distinct wall/scaffold artwork.

Version 0.7.2 has not been run in-game here. The ownership, renderer-cache and scheduling regressions pass, but actual completion time still needs a gameplay retest. Territory expansion, stone availability, travel, danger and occupied wall tiles can delay work. Saved terrain/building gaps remain unchanged. Terrain restoration after reload may lose the original top terrain and height because those details are remembered only during the session; vegetation recovery is not guaranteed.





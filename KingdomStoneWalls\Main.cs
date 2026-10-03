using System;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.api;
using UnityEngine;
using HarmonyLib;
using System.Reflection.Emit;
using ai.behaviours;

namespace KingdomStoneWalls {
 public class Main : BasicMod<Main> {
  public const string RaceSettingGroup="Wall builders", RaceSettingId="All races build walls";
  public static bool AllRacesBuildWalls=true;
  protected override void OnModLoad() {
   AllRacesBuildWalls=GetConfig()[RaceSettingGroup][RaceSettingId].BoolVal;
   Walls.Register();
   Construction.Register();
   OrcWalls.Register();
   Gates.Register();
   ElfWalls.Register();
   DwarfWalls.Register();
   TownWalls.Register();
   CapitalBorders.Register();
   TowerLifecycle.Register();
   WallMovement.Register();
   Supply.Register();
   Mining.Register();
   new GameObject("KingdomStoneWallsController").AddComponent<Controller>();
   Debug.Log("Kingdom Stone Walls 0.14.22 loaded: blocked combat targets pause before routing. F8 toggles construction; Shift+F8 removes walls and gates.");
  }
 }
 public static class Construction {
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"), Data=AccessTools.Field(typeof(Building),"data"), Active=AccessTools.Field(typeof(City),"under_construction_building"), KingdomField=AccessTools.Field(typeof(City),"kingdom");
  static readonly System.Reflection.MethodInfo HasResources=AccessTools.Method(typeof(City),"hasEnoughResourcesFor"), Spend=AccessTools.Method(typeof(City),"spendResourcesForBuildingAsset"), Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal"), SetKingdom=AccessTools.Method(typeof(Building),"setKingdomCiv"), Add=AccessTools.Method(typeof(BuildingManager),"addBuilding",new Type[]{typeof(BuildingAsset),typeof(WorldTile),typeof(bool),typeof(bool),typeof(BuildPlacingType)});
  public const string SiteId="ksw_construction_site";
  public const int WallCost=1, MaterialReserve=20;
  public static BuildingAsset Site;
  static BuildingSprites graphics;
  static Sprite segment, scaffold;
  static readonly System.Reflection.FieldInfo LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
  public static readonly Dictionary<WorldTile,City> Plans=new Dictionary<WorldTile,City>();
  public static readonly Dictionary<City,Fortification> CityPlans=new Dictionary<City,Fortification>();
  public static readonly Dictionary<City,KeyValuePair<int,int>> TownReservations=new Dictionary<City,KeyValuePair<int,int>>();
  public const string TownCenterKey="ksw_town_reserve_center";
  public static readonly Queue<Building> Finished=new Queue<Building>();
  static readonly HashSet<Building> queued=new HashSet<Building>();
  public static bool Enabled=true, Faulted;
  public static Kingdom Owner(City c) { return (Kingdom)KingdomField.GetValue(c); }
  public static bool IsCapital(City city) {
   if(city==null || !city.isAlive()) return false;
   var kingdom=Owner(city);
   return kingdom!=null && kingdom.isCiv() && kingdom.capital==city && RaceAllowed(city);
  }
  public static bool IsFortifiedCity(City city) {
   return IsCapital(city) || city!=null && city.isAlive() && RaceAllowed(city) &&
    Owner(city)!=null && Owner(city).isCiv() && city.getPopulationPeople()>200;
  }
  public static bool ReservationEligible(City city) {
   return city!=null && city.isAlive() && RaceAllowed(city) &&
    Owner(city)!=null && Owner(city).isCiv();
  }
  public static bool RaceAllowed(City city) {
   if(Main.AllRacesBuildWalls) return true;
   var race=city?.getActorAsset();
   if(race==null) return false;
   var id=string.IsNullOrEmpty(race.base_asset_id)?race.id:race.base_asset_id;
   return id=="human" || id=="orc" || id=="elf" || id=="dwarf";
  }
  public static bool AtPeace(City city) {
   var kingdom=city==null?null:Owner(city);
   return kingdom!=null && MapBox.instance!=null && !MapBox.instance.wars.hasWars(kingdom);
  }
  public static int CostFor(Building site) {
   int paid=0; ((BuildingData)Data.GetValue(site)).get("ksw_wall_cost",out paid,0);
   // Sites saved before 0.11.2 were paid at the original two-resource price.
   return paid>0?paid:2;
  }
  public static void Reset() { Plans.Clear(); CityPlans.Clear(); TownReservations.Clear(); Finished.Clear(); queued.Clear(); CapitalBorders.Reset(); Supply.Reset(); Faulted=false; }
  public static void Register() {
   var template=AssetManager.buildings.list.First(b=>b.city_building && b.has_sprite_construction && b.can_units_live_here);
   Site=AssetManager.buildings.clone(SiteId,template.id);
   Site.base_stats=(BaseStats)template.base_stats.Clone(); Site.atlas_asset=template.atlas_asset;
   Site.fundament=new BuildingFundament(0,0,0,0);
   Site.scale_base=new Vector3(1,1,1);
   Site.type="type_ksw_site"; Site.cost=new ConstructionCost {stone=WallCost};
   Site.construction_progress_needed=10;
   Site.can_units_live_here=false; Site.can_be_living_house=false; Site.can_be_living_plant=false;
   Site.housing_slots=0; Site.housing_happiness=0; Site.max_houses=0; Site.book_slots=0;
   Site.storage=false; Site.is_stockpile=false; Site.spawn_rats=false; Site.smoke=false;
   Site.can_be_upgraded=false; Site.upgrade_to=null; Site.build_road_to=false;
   Site.has_kingdom_color=false; Site.can_be_abandoned=false; Site.shadow=false;
   Site.has_ruins_graphics=false; Site.has_special_animation_state=false;
   Site.has_sprites_spawn=false; Site.has_sprites_main=true; Site.has_sprites_main_disabled=true;
   Site.has_sprites_ruin=false; Site.has_sprites_special=false; Site.has_sprite_construction=true;
   Site.get_override_sprite_main=null; Site.get_override_sprites_main=null;
   // clone() resets sprite state; loading by the new ID previously produced zero frames.
   // Supply an independent, complete frame container and prevent the loader overwriting it.
   segment=Walls.MakeSprite(false); scaffold=Walls.MakeSprite(true);
   var frames=new BuildingAnimationData {main=new[]{segment},main_disabled=new[]{segment},spawn=new[]{segment},ruins=new[]{segment},special=new[]{segment}};
   graphics=new BuildingSprites {construction=scaffold,map_icon=new BuildingMapIcon(segment)};
   graphics.animation_data.Add(frames);
   ProtectGraphics(Site);
   PreloadHelpers.all_preloaded_sprites_buildings.Add(segment);
   PreloadHelpers.all_preloaded_sprites_buildings.Add(scaffold);
   var harmony=new Harmony("custom.kingdom_stone_walls.construction");
   // The startup preloader calls loadBuildingSprites directly, ignoring the initiated flag.
   harmony.Patch(AccessTools.Method(typeof(BuildingAsset),"loadBuildingSprites"),prefix:new HarmonyMethod(typeof(Construction),"ProtectGraphics"));
   harmony.Patch(AccessTools.Method(typeof(BuildingAsset),"checkSpritesAreLoaded"),prefix:new HarmonyMethod(typeof(Construction),"ProtectGraphics"));
   harmony.Patch(AccessTools.Method(typeof(Building),"calculateMainSprite"),prefix:new HarmonyMethod(typeof(Construction),"SiteSprite"));
   harmony.Patch(AccessTools.Method("ai.behaviours.CityBehBuild:buildTick"),prefix:new HarmonyMethod(typeof(Construction),"BeforeCityBuild"),transpiler:new HarmonyMethod(typeof(Construction),"IndependentSites"));
   harmony.Patch(AccessTools.Method(typeof(BuildingManager),"canBuildFrom"),prefix:new HarmonyMethod(typeof(Construction),"ReserveFootprint"));
   harmony.Patch(AccessTools.Method("ai.behaviours.CityBehBuild:tryToBuild"),transpiler:new HarmonyMethod(typeof(Construction),"FinalPlacementGuard"));
   harmony.Patch(AccessTools.Method("ai.behaviours.CityBehBuild:upgradeBuilding"),prefix:new HarmonyMethod(typeof(Construction),"ReserveUpgrade"));
   harmony.Patch(AccessTools.Method(typeof(Building),"get_city"),prefix:new HarmonyMethod(typeof(Construction),"SiteCity"));
   harmony.Patch(AccessTools.Method(typeof(Building),"getConstructionTile"),prefix:new HarmonyMethod(typeof(Construction),"ConstructionApproach"));
   // Reuse the actual house-building task, including travel, hammer animation and work.
   CreateCrewTask(AssetManager.tasks_actor,AssetManager.unit_hand_tools);
   harmony.Patch(AccessTools.Method(typeof(Building),"updateBuild"),prefix:new HarmonyMethod(typeof(Construction),"AllowWork"),postfix:new HarmonyMethod(typeof(Construction),"AfterWork"));
  }
  public static bool ProtectGraphics(BuildingAsset __instance) {
   if(__instance.id!=SiteId) return true;
   __instance.building_sprites=graphics; __instance.sprites_are_initiated=true; return false;
  }
  public static bool SiteSprite(Building __instance,ref Sprite __result) {
   if(OrcWalls.IsSite(__instance) || ElfWalls.IsSite(__instance) || DwarfWalls.IsSite(__instance) || !IsSite(__instance)) return true;
   __result=__instance.isUnderConstruction()?scaffold:segment;
   LastSprite.SetValue(__instance,__result); return false;
  }
  public static bool IsSite(Building b) { return b!=null && (((BuildingAsset)Asset.GetValue(b))?.id==SiteId || OrcWalls.IsSite(b) || ElfWalls.IsSite(b) || DwarfWalls.IsSite(b) || TownWalls.IsSite(b)); }
  public static bool IsWorksite(Building b) { return IsSite(b) || Gates.IsGate(b); }
  public static bool IsVegetation(Building b) {
   var asset=b==null?null:(BuildingAsset)Asset.GetValue(b);
   return asset!=null && !asset.city_building && (asset.can_be_living_plant || asset.is_vegetation);
  }
  public static bool BlocksWall(Building b) { return b!=null && !IsSite(b) && !IsVegetation(b); }
  public static bool SiteCity(Building __instance,ref City __result) {
   if(!IsSite(__instance)) return true;
   __result=MapBox.instance.cities.get(((BuildingData)Data.GetValue(__instance)).cityID); return false;
  }
  public static void BeforeCityBuild(City pCity) {
   var active=(Building)Active.GetValue(pCity);
   if(IsWorksite(active)) Active.SetValue(pCity,null);
   if(!Enabled || Faulted) return;
   if(!IsFortifiedCity(pCity)) {
    ReserveYoungTown(pCity);
    return;
   }
   TownReservations.Remove(pCity);
   Fortification existing;
   if(CityPlans.TryGetValue(pCity,out existing)) {
    return;
   }
   var kingdom=Owner(pCity); if(kingdom==null || !kingdom.isCiv()) return;
   var center=(Vector2)AccessTools.Field(typeof(City),"city_center").GetValue(pCity);
   var plan=new Fortification(pCity,(int)center.x,(int)center.y);
   CityPlans[pCity]=plan;
   if(IsCapital(pCity)) kingdom.data.set(Fortification.CityKey,pCity.data.id);
  }
  public static void ReserveYoungTown(City city) {
   if(city==null) return;
   if(!ReservationEligible(city) || IsCapital(city)) { TownReservations.Remove(city); return; }
   Fortification plan;
   if(CityPlans.TryGetValue(city,out plan)) {
    TownReservations.Remove(city);
    return;
   }
   KeyValuePair<int,int> center;
   if(TownReservations.TryGetValue(city,out center)) return;
   string saved=null; city.data.get(TownCenterKey,out saved,null);
   if(string.IsNullOrEmpty(saved)) {
    bool existingTown=false; city.data.get(TownWalls.LayoutKey,out existingTown,false);
    if(existingTown) city.data.get(Fortification.CenterKey,out saved,null);
   }
   var parts=(saved??"").Split(','); int x,y;
   if(parts.Length!=2 || !int.TryParse(parts[0],out x) || !int.TryParse(parts[1],out y)) {
    var current=(Vector2)AccessTools.Field(typeof(City),"city_center").GetValue(city);
    x=(int)current.x; y=(int)current.y;
   }
   city.data.set(TownCenterKey,x+","+y);
   TownReservations[city]=new KeyValuePair<int,int>(x,y);
  }
  public static bool BuildAllowed(BuildingAsset asset,WorldTile tile) {
   if(asset==null || tile==null) return true;
   if(asset.tower && IsCapital(tile.zone_city) && (OrcWalls.IsOrcCity(tile.zone_city) || ElfWalls.IsElfCity(tile.zone_city) || DwarfWalls.IsDwarfCity(tile.zone_city))) return false;
   if(!asset.city_building || asset.id==SiteId || asset.id==OrcWalls.SiteId || asset.id==ElfWalls.SiteId || asset.id==DwarfWalls.SiteId) return true;
   var f=asset.fundament; if(f==null) return true;
   foreach(var plan in CityPlans.Values) {
    if(!ReservationEligible(plan.City)) continue;
    if(ElfWalls.Reserved(plan,tile.x-f.left,tile.y-f.bottom,tile.x+f.right,tile.y+f.top)) return false;
    if(GateTowers.Intersects(plan,tile.x-f.left,tile.y-f.bottom,tile.x+f.right,tile.y+f.top)) return false;
   }
   foreach(var reservation in TownReservations) {
    if(!ReservationEligible(reservation.Key) || IsCapital(reservation.Key)) continue;
    var center=reservation.Value;
    if(TownLayout.IntersectsReserved(center.Key,center.Value,tile.x-f.left,tile.y-f.bottom,tile.x+f.right,tile.y+f.top)) return false;
   }
   return true;
  }
  public static bool CityBuildAllowed(City city,BuildingAsset asset,WorldTile tile) {
   return !(asset!=null && asset.tower && IsCapital(city) && (OrcWalls.IsOrcCity(city) || ElfWalls.IsElfCity(city) || DwarfWalls.IsDwarfCity(city))) && BuildAllowed(asset,tile);
  }
  public static bool ReserveFootprint(WorldTile pTile,BuildingAsset pNewBuildingAsset,BuildPlacingType pType,ref bool __result) {
   if(pType!=BuildPlacingType.New || BuildAllowed(pNewBuildingAsset,pTile)) return true;
   __result=false; return false;
  }
  public static bool ReserveUpgrade(Building pBuilding,ref bool __result) {
   var asset=(BuildingAsset)Asset.GetValue(pBuilding);
   if(asset==null || !asset.can_be_upgraded || string.IsNullOrEmpty(asset.upgrade_to) || BuildAllowed(AssetManager.buildings.get(asset.upgrade_to),pBuilding.current_tile)) return true;
   __result=false; return false;
  }
  public static IEnumerable<CodeInstruction> FinalPlacementGuard(IEnumerable<CodeInstruction> source,ILGenerator generator) {
   // Special placement (docks, house replacement, training dummies) can bypass canBuildFrom.
   // Reject the chosen footprint before native code creates the building or spends resources.
   var codes=source.ToList(); int add=codes.FindIndex(c=>c.Calls(Add));
   if(add<0 || codes.Count(c=>c.Calls(Add))!=1) throw new InvalidOperationException("Unsupported WorldBox city placement layout");
   int start=add-1;
   while(start>=0) {
    var method=codes[start].operand as System.Reflection.MethodInfo;
    if(method!=null && method.Name=="get_world" && method.IsStatic && method.ReturnType==typeof(MapBox)) break;
    start--;
   }
   if(start<0) throw new InvalidOperationException("Missing WorldBox city placement entry");
   var proceed=generator.DefineLabel(); var first=new CodeInstruction(OpCodes.Ldarg_0);
   first.labels.AddRange(codes[start].labels); codes[start].labels.Clear();
   first.blocks.AddRange(codes[start].blocks); codes[start].blocks.Clear(); codes[start].labels.Add(proceed);
   codes.InsertRange(start,new[]{first,new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Ldloc_0),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Construction),"CityBuildAllowed")),new CodeInstruction(OpCodes.Brtrue,proceed),new CodeInstruction(OpCodes.Ldnull),new CodeInstruction(OpCodes.Ret)});
   return codes;
  }
  public static bool Safe(WorldTile tile,City city,bool converting,bool planning=false) {
   if(tile==null || city==null || !city.isAlive() || tile.zone_city!=null && Owner(tile.zone_city)!=Owner(city)) return false;
   // Reserve future land in the plan, but create/finish sites only in the owning city.
   if(!planning && !OwnsTile(tile,city)) return false;
   var original=Walls.Original(tile);
   if(original==null || !Walls.Types.ContainsKey(original.id) || BlocksWall(tile.building)) return false;
   // Reserve this same two-tile building clearance for the entire future perimeter.
   for(int x=-2;x<=2;x++) for(int y=-2;y<=2;y++) {
    var near=MapBox.instance.GetTile(tile.x+x,tile.y+y);
    // Roads beside masonry are valid work lanes. Native city road construction
    // continues after planning, so rejecting adjacent roads permanently stalls a ring.
    // The segment itself still must be a supported non-road terrain type above.
    if(near==null || !(Walls.IsWall(near) || Walls.Land(near) || Walls.Road(near)) || BlocksWall(near.building) && !Gates.IsGate(near.building)) return false;
    
   }
   // Units only delay work. Their position while planning must not permanently erase a segment.
   return planning || !tile.hasUnits() && (!converting || !WallMovement.HasPhysicalOccupant(tile));
  }
  public const string CrewTask="ksw_build_wall";
  public const int WorkReach=2;
  public static BehaviourTaskActor CreateCrewTask(BehaviourTaskActorLibrary tasks,UnitHandToolLibrary tools) {
   var task=tasks.clone(CrewTask,"build_building");
   // AssetLibrary.clone skips NonSerialized fields, including this runtime tool cache.
   // The renderer trusts force_hand_tool and dereferences this asset when drawing a mason.
   task.cached_hand_tool_asset=tools.get(task.force_hand_tool);
   if(task.cached_hand_tool_asset==null) throw new InvalidOperationException("Wall construction hammer asset is unavailable");
   return task;
  }
  public static void SetCrewTask(Actor actor,string task) {
   actor.setTask(task,true,false,false);
   ItemSpriteDirty.SetValue(actor,true);
  }
  static readonly System.Reflection.FieldInfo TargetBuilding=AccessTools.Field(typeof(Actor),"beh_building_target"), ActorAI=AccessTools.Field(typeof(Actor),"ai"), AITask=AccessTools.Field(typeof(AiSystemActor),"task"), ActorCity=AccessTools.Field(typeof(Actor),"city"), Frozen=AccessTools.Field(typeof(Actor),"is_ai_frozen"), Sleeping=AccessTools.Field(typeof(Actor),"_has_status_sleeping"), ItemSpriteDirty=AccessTools.Field(typeof(Actor),"_dirty_sprite_item");
  public static Building Target(Actor actor) { return (Building)TargetBuilding.GetValue(actor); }
  public static string TaskId(Actor actor) {
   var ai=ActorAI.GetValue(actor); var task=ai==null?null:(BehaviourTaskActor)AITask.GetValue(ai);
   return task==null?"":task.id;
  }
  public static bool NativePending(Building building) { return !IsWorksite(building) && building.isUnderConstruction(); }
  public static bool OwnsTile(WorldTile tile,City city) { return tile!=null && city!=null && tile.zone_city==city; }
  public static IEnumerable<CodeInstruction> IndependentSites(IEnumerable<CodeInstruction> source) {
   // The native city scans every unfinished building when choosing its one house job.
   // Dedicated wall sites must never occupy that slot, including sites restored from saves.
   var codes=source.ToList(); var check=AccessTools.Method(typeof(Building),"isUnderConstruction");
   int matches=0;
   foreach(var code in codes) if(code.Calls(check)) {
    code.opcode=OpCodes.Call; code.operand=AccessTools.Method(typeof(Construction),"NativePending"); matches++;
   }
   if(matches!=1) throw new InvalidOperationException("Unsupported WorldBox pending-building selection");
   return codes;
  }
  public static bool ConstructionApproach(Building __instance,ref WorldTile __result) {
   if(!IsWorksite(__instance)) return true;
   var tile=__instance.current_tile; Fortification plan;
   var city=__instance.getCity();
   if(!IsFortifiedCity(city) || !CityPlans.TryGetValue(city,out plan)) { __result=tile; return false; }
   WorldTile lane;
   var mason=plan.Crew.FirstOrDefault(a=>a!=null && Target(a)==__instance);
   if(plan.WorkLanes.TryGetValue(__instance,out lane) && ValidWorkLane(tile,lane)) { __result=lane; return false; }
   lane=mason==null?null:ChooseWorkLane(tile,mason);
   if(lane!=null) plan.WorkLanes[__instance]=lane;
   __result=lane??tile; return false;
  }
  public static bool ValidWorkLane(WorldTile tile,WorldTile near) {
   int distance=tile==null || near==null?0:Math.Abs(near.x-tile.x)+Math.Abs(near.y-tile.y);
   if(distance<1 || distance>WorkReach || !(Walls.Land(near) || Walls.Road(near)) || Walls.IsWall(near) || BlocksWall(near.building)) return false;
   City plannedCity;
   return !Plans.TryGetValue(near,out plannedCity) || !IsFortifiedCity(plannedCity);
  }
  public static WorldTile ChooseWorkLane(WorldTile tile,Actor mason) {
   // A short geometric distance can lead through a completed ring. Test the
   // actual route from this mason before reserving a side of the scaffold.
   if(tile==null || mason==null || mason.current_tile==null) return null;
   var lanes=new List<WorldTile>();
   for(int dx=-WorkReach;dx<=WorkReach;dx++) for(int dy=-WorkReach;dy<=WorkReach;dy++) {
    if(Math.Abs(dx)+Math.Abs(dy)>WorkReach) continue;
    var near=MapBox.instance.GetTile(tile.x+dx,tile.y+dy);
    if(ValidWorkLane(tile,near)) lanes.Add(near);
   }
   foreach(var near in lanes.OrderBy(t=>Math.Abs(t.x-mason.current_tile.x)+Math.Abs(t.y-mason.current_tile.y)))
    if(WallMovement.CanReach(mason,near)) return near;
   return null;
  }
  public static void StopCrew(Fortification plan) {
   Supply.Stop(plan.City);
   foreach(var actor in plan.Crew) if(actor!=null && actor.isAlive() && TaskId(actor)==CrewTask) SetCrewTask(actor,"nothing");
   plan.Crew.Clear(); plan.WorkLanes.Clear();
  }
  public static bool EligibleMason(Actor actor) {
   if(actor==null || !actor.isAlive() || !actor.isAdult() || !actor.is_profession_citizen || actor.isFighting() || actor.isHungry() || (bool)Frozen.GetValue(actor) || (bool)Sleeping.GetValue(actor)) return false;
   var task=TaskId(actor);
   // The native try_build_building task only searches for a house job; build_road
   // is ordinary citizen work. Neither should exclude a citizen from the wall crew.
   // Preserve actual building work and our already assigned wall workers.
   return !task.Contains("food") && !task.Contains("eat") && !task.Contains("sleep") && !task.Contains("fight") && !task.Contains("flee") && !task.Contains("heal") && task!="build_building" && task!=CrewTask && !Supply.IsRunner(actor);
  }
  public static void TickCrew(Fortification plan,double now) {
   if(!IsFortifiedCity(plan.City)) { StopCrew(plan); return; }
   if(now<plan.NextWork) return;
   plan.NextWork=now+1;
   var city=plan.City;
   if(plan.ReachabilityRevision!=Walls.Revision) { plan.UnreachableUntil.Clear(); plan.ReachabilityRevision=Walls.Revision; }
   if(!Enabled || Faulted || !city.isAlive()) { StopCrew(plan); return; }
   if(!plan.Ready) return;
   // A kingdom at war cannot work on fortifications. Check before scanning
   // wall rings or gate footprints for every city in a large world.
   if(!AtPeace(city) || city.getPopulationPeople()<40 || city.isInDanger() || city.isGettingCaptured() || city.isCityUnderDangerFire()) { StopCrew(plan); return; }
   int ring=CapitalStages.ActiveRing(plan);
   WorldTile creating=null;
   try {
    // Finished rings can receive paid gate jobs while the next ring is still growing.
    var gateTiles=Gates.WorkTiles(plan);
    var tiles=gateTiles.Concat(ring>=0?plan.Rings[ring]:Enumerable.Empty<WorldTile>()).ToList();
    int limit=CrewPolicy.Size(city.getPopulationPeople());
    // Preserve active workers; never restart their travel/hammer task every scheduler tick.
    for(int i=plan.Crew.Count-1;i>=0;i--) {
     var actor=plan.Crew[i]; var building=actor==null?null:Target(actor);
     if(i>=limit || actor==null || !actor.isAlive() || ActorCity.GetValue(actor)!=city || TaskId(actor)!=CrewTask || !IsWorksite(building) || !building.isAlive() || !building.isUnderConstruction() || !tiles.Contains(building.current_tile) || !OwnsTile(building.current_tile,city) || actor.isHungry() || actor.isFighting()) {
      if(actor!=null && actor.isAlive() && TaskId(actor)==CrewTask) SetCrewTask(actor,"nothing");
      if(building!=null) plan.WorkLanes.Remove(building);
      plan.Crew.RemoveAt(i);
     }
    }
    var assigned=new HashSet<Building>(plan.Crew.Select(a=>Target(a)));
    var sites=new List<Building>();
    foreach(var tile in tiles) if(OwnsTile(tile,city) && IsWorksite(tile.building) && tile.building.getCity()==city) {
     var building=tile.building;
     if(!building.isUnderConstruction()) { Enqueue(building); continue; }
     // An interrupted/unreachable job is retained and offered to another nearby citizen.
     int progress=building.getConstructionProgress(); SiteProgress previous;
     var mason=plan.Crew.FirstOrDefault(a=>Target(a)==building);
     WorldTile workLane;
     int distance=mason!=null && mason.current_tile!=null && plan.WorkLanes.TryGetValue(building,out workLane) && workLane!=null?Math.Abs(mason.current_tile.x-workLane.x)+Math.Abs(mason.current_tile.y-workLane.y):int.MaxValue;
     if(!plan.Progress.TryGetValue(building,out previous) || progress!=previous.Value) plan.Progress[building]=new SiteProgress {Value=progress,At=now,BestDistance=distance};
     else if(distance<previous.BestDistance) { previous.BestDistance=distance; previous.At=now; }
     else if(now-previous.At>=30) {
      foreach(var actor in plan.Crew.ToArray()) if(Target(actor)==building) {
       SetCrewTask(actor,"nothing"); plan.Crew.Remove(actor); assigned.Remove(building); plan.Retry[actor]=now+30;
      }
      plan.WorkLanes.Remove(building);
      previous.At=now;
     }
     double until;
     if(!assigned.Contains(building) && (!plan.UnreachableUntil.TryGetValue(tile,out until) || now>=until)) sites.Add(building);
    }
    while(plan.Crew.Count<limit) {
     Building job=sites.Count>0?sites[0]:null;
    var location=job==null?tiles.FirstOrDefault(t=>OwnsTile(t,city) && (!plan.UnreachableUntil.TryGetValue(t,out var until) || now>=until) && (t.building==null || IsVegetation(t.building) || gateTiles.Contains(t) && Gates.IsWoodGate(t.building)) && !CapitalStages.Complete(t,plan) && (gateTiles.Contains(t)?Gates.CanAfford(plan,t):Safe(t,city,false))):job.current_tile;
     if(location==null) break;
     Actor worker=null; WorldTile lane=null;
     foreach(var actor in city.units.Where(a=>a!=null && a.current_tile!=null && EligibleMason(a) && !plan.Crew.Contains(a) && (!plan.Retry.TryGetValue(a,out var retry) || now>=retry)).OrderBy(a=>Math.Abs(a.current_tile.x-location.x)+Math.Abs(a.current_tile.y-location.y))) {
      lane=ChooseWorkLane(location,actor);
      if(lane!=null) { worker=actor; break; }
     }
     if(worker==null) { plan.UnreachableUntil[location]=now+15; if(job!=null) { sites.RemoveAt(0); continue; } break; }
     plan.UnreachableUntil.Remove(location);
     if(job==null) {
      if(gateTiles.Contains(location)) {
       if(!Gates.CanAfford(plan,location)) break;
       // An upgrade reuses the wooden gate; never remove it during failed site cleanup.
       creating=Gates.IsWoodGate(location.building)?null:location; job=Gates.Start(plan,location);
       if(job==null) { creating=null; break; }
       Gates.Pay(job,city); creating=null;
       plan.Progress[job]=new SiteProgress {Value=job.getConstructionProgress(),At=now};
      } else {
      // No housing or native-job gate: three to six masons work independently of houses.
      // Preserve twenty units of the current wall material; charge one per section.
      if(!(bool)HasResources.Invoke(city,new object[]{OrcWalls.ReserveFor(city)})) break;
      var site=CapitalStages.SiteFor(plan);
      ProtectGraphics(site); OrcWalls.ProtectGraphics(site); ElfWalls.ProtectGraphics(site); DwarfWalls.ProtectGraphics(site); creating=location;
      // Clear only vegetation on the paid job's owned tile, never nearby houses.
      if(IsVegetation(location.building)) Remove.Invoke(location.building,null);
      job=(Building)Add.Invoke(MapBox.instance.buildings,new object[]{site,location,false,false,BuildPlacingType.New});
      if(job==null) break;
      ((BuildingData)Data.GetValue(job)).cityID=city.data.id;
      SetKingdom.Invoke(job,new object[]{Owner(city)});
      job.setUnderConstruction(); Spend.Invoke(city,new object[]{site.cost}); creating=null;
      ((BuildingData)Data.GetValue(job)).set("ksw_wall_cost",WallCost);
      plan.Progress[job]=new SiteProgress {Value=0,At=now};
      }
     } else sites.RemoveAt(0);
     plan.WorkLanes[job]=lane;
     SetCrewTask(worker,CrewTask);
     TargetBuilding.SetValue(worker,job); plan.Crew.Add(worker); assigned.Add(job);
     plan.Progress[job].BestDistance=Math.Abs(worker.current_tile.x-lane.x)+Math.Abs(worker.current_tile.y-lane.y); plan.Progress[job].At=now;
    }
    if(ring>=0 && now>=plan.NextReport) {
     plan.NextReport=now+120;
     int waitingBorder=tiles.Count(t=>!CapitalStages.Complete(t,plan) && !OwnsTile(t,city));
     int buildable=tiles.Count(t=>OwnsTile(t,city) && (t.building==null || IsVegetation(t.building)) && !CapitalStages.Complete(t,plan) && Safe(t,city,false));
     int completedWaiting=tiles.Count(t=>IsSite(t.building) && !t.building.isUnderConstruction());
     var waiting=tiles.FirstOrDefault(t=>IsSite(t.building) && !t.building.isUnderConstruction());
     if(waiting!=null) {
      string reason=!OwnsTile(waiting,city)?"outside owning city":waiting.hasUnits()?"unit on wall tile":!Safe(waiting,city,true)?"terrain or building clearance":"ready for conversion";
      Debug.Log("Kingdom Stone Walls completed scaffold at "+waiting.x+","+waiting.y+": "+reason+".");
     }
     int available=city.units.Count(a=>EligibleMason(a) && !plan.Crew.Contains(a));
     bool funded=(bool)HasResources.Invoke(city,new object[]{OrcWalls.ReserveFor(city)});
     Debug.Log("Kingdom Stone Walls city "+city.data.id+": ring "+(ring+1)+", finished "+tiles.Count(t=>CapitalStages.Complete(t,plan))+"/"+tiles.Count+", masons "+plan.Crew.Count+"/"+limit+", waiting for borders "+waitingBorder+", buildable owned sites "+buildable+", completed scaffolds waiting "+completedWaiting+", available citizens "+available+", "+CapitalStages.MaterialFor(plan)+" reserve met "+funded+".");
    }
    // Drop references to converted or destroyed sites; retries are bounded by the crew size.
    foreach(var building in plan.Progress.Keys.ToArray()) if(!building.isAlive() || Gates.IsGate(building) && !building.isUnderConstruction()) plan.Progress.Remove(building);
    foreach(var building in plan.WorkLanes.Keys.ToArray()) if(!assigned.Contains(building) || !building.isAlive()) plan.WorkLanes.Remove(building);
    foreach(var actor in plan.Retry.Keys.ToArray()) if(now>=plan.Retry[actor] || !actor.isAlive()) plan.Retry.Remove(actor);
   } catch(Exception ex) {
    if(creating!=null && IsWorksite(creating.building)) {
     bool removing=Gates.Removing; Gates.Removing=true;
     try { RemoveSite(creating.building,false); } catch(Exception cleanup) { Debug.LogError("Kingdom Stone Walls partial-site cleanup failed: "+cleanup); }
     finally { Gates.Removing=removing; }
    }
    Faulted=true; Enabled=false; StopCrew(plan); Debug.LogError("Kingdom Stone Walls paused after crew error: "+ex);
   }
  }
  public static void Enqueue(Building b) { if(queued.Add(b)) Finished.Enqueue(b); }
  public static void Dequeued(Building b) { queued.Remove(b); }
  public static void AfterWork(Building __instance,bool __result) {
   if(!__result) return;
   if(Gates.IsGate(__instance)) Gates.Complete(__instance);
   else if(IsSite(__instance)) Enqueue(__instance);
  }
  public static bool AllowWork(Building __instance,ref bool __result) {
   if(Gates.IsGate(__instance)) {
    Fortification plan; var city=__instance.getCity();
    if(__instance.isUnderConstruction() && Enabled && !Faulted && IsFortifiedCity(city) && CityPlans.TryGetValue(city,out plan) && plan.Ready && AtPeace(city) && !city.isInDanger() && !city.isGettingCaptured() && !city.isCityUnderDangerFire() && Gates.CanWork(__instance,plan)) return true;
    __result=false; return false;
   }
   if(!IsSite(__instance)) return true;
   if(Enabled && !Faulted && AtPeace(__instance.getCity()) && IsActiveSegment(__instance.current_tile,__instance.getCity())) return true;
   __result=false; return false;
  }
  public static bool IsActiveSegment(WorldTile tile,City city) {
   Fortification plan; if(!IsFortifiedCity(city) || !OwnsTile(tile,city) || !CityPlans.TryGetValue(city,out plan) || !plan.Ready) return false;
   int ring=CapitalStages.ActiveRing(plan);
   return ring>=0 && plan.Rings[ring].Contains(tile);
  }
  public static bool TryFinish(Building b) {
   // Queue entries can outlive their site; never finalize a recycled or unfinished object.
   if(b==null || !b.isAlive() || !IsSite(b)) return true;
   if(b.isUnderConstruction()) return true;
   if(!IsFortifiedCity(b.getCity())) return false;
   var tile=b.current_tile; City city;
   bool wanted=Plans.TryGetValue(tile,out city) && city==b.getCity();
   if(!wanted) { RemoveSite(b,true); return true; }
   TileType wall;
   // Preserve paid, finished work while any final condition is temporarily blocked.
   // Deleting/refunding here made workers buy and build the same gap indefinitely.
   if(!IsActiveSegment(tile,city) || !Safe(tile,city,true) || !CapitalStages.TypeFor(b,tile,city,out wall)) return false;
   bool upgrading=Walls.IsWall(tile);
   RemoveSite(b,false);
   if(upgrading) Walls.Upgrade(tile,wall); else Walls.Place(tile,wall);
   return true;
  }
  public static void RemoveSite(Building b,bool refund) {
   var city=b.getCity();
   if(city!=null && Active.GetValue(city)==b) Active.SetValue(city,null);
   if(refund && city!=null && city.isAlive()) city.addResourcesToRandomStockpile(Gates.IsGate(b)?Gates.ResourceFor(b):OrcWalls.ResourceFor(b),Gates.IsGate(b)?Gates.CostFor(b):CostFor(b));
   queued.Remove(b); Remove.Invoke(b,null);
  }
 }
 public static class Walls {
  public const string Prefix="ksw_";
  public static readonly Dictionary<string,TileType> Types=new Dictionary<string,TileType>();
  public static int Revision;
  static readonly HashSet<TileType> wallTypes=new HashSet<TileType>();
  sealed class Previous { public TopTileType Top; public int Height; }
  static readonly Dictionary<WorldTile,Previous> previous=new Dictionary<WorldTile,Previous>();
  public static void ClearHistory() { previous.Clear(); }
  public static void Place(WorldTile tile,TileType wall) {
   previous[tile]=new Previous {Top=tile.top_type,Height=tile.Height};
   tile.setTileTypes(wall,null,true);
   WallMovement.MarkWall(tile,true);
   Revision++;
  }
  public static void Upgrade(WorldTile tile,TileType wall) {
   // Keep the original ground snapshot and collision index while replacing
   // the material of this exact wall tile.
   tile.setTileTypes(wall,null,true);
   Revision++;
  }
  public static Sprite MakeSprite(bool construction) {
   var texture=new Texture2D(16,16,TextureFormat.RGBA32,false); texture.filterMode=FilterMode.Point;
   texture.LoadRawTextureData(WallArt.Pixels(construction,0)); texture.Apply();
   var sprite=Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f),construction?4:1);
   sprite.name=construction?"ksw_scaffold":"ksw_segment"; return sprite;
  }
  public static void Register() {
   // Separate mountain clones keep natural mountains visually unchanged.
   var sprites=new TileSprites();
   for(int variant=0;variant<4;variant++) {
    var texture=new Texture2D(16,16,TextureFormat.RGBA32,false);
    texture.filterMode=FilterMode.Point;
    texture.LoadRawTextureData(WallArt.Pixels(false,variant)); texture.Apply();
    // Use native building pixel scale so masonry matches the small houses.
    sprites.addVariation(Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f),1),"stone"+variant);
   }
   foreach(var original in AssetManager.tiles.list.ToArray()) {
    if(!original.ground || original.liquid || original.block || original.mountains || original.summit || original.road || original.id.StartsWith(Prefix)) continue;
    var wall=AssetManager.tiles.clone(Prefix+original.id,TileLibrary.mountains.id);
    wall.sprites=sprites; wall.color=new Color32(135,132,124,255);
    wall.can_build_on=false; wall.used_in_generator=false;
    wall.block=true; wall.wall=true; wall.block_height=1000;
    wall.mountains=false; wall.summit=false; wall.ground=true;
    wall.edge_mountains=false; wall.edge_hills=false; wall.check_edge=false;
    wall.height_min=original.height_min; wall.additional_height=new[]{0};
    wall.damaged_when_walked=false; wall.damage_units=false; wall.step_action=null;
    Types[original.id]=wall; wallTypes.Add(wall);
   }
  }
  public static bool IsWall(WorldTile tile) { return tile!=null && wallTypes.Contains(tile.main_type); }
  public static void Track(TileType type) { wallTypes.Add(type); }
  public static TileType Original(WorldTile tile) {
   if(tile==null) return null;
   return IsWall(tile)?AssetManager.tiles.get(tile.main_type.id.Substring(tile.main_type.id.StartsWith(TownWalls.Prefix)?TownWalls.Prefix.Length:tile.main_type.id.StartsWith(OrcWalls.Prefix)?OrcWalls.Prefix.Length:tile.main_type.id.StartsWith(ElfWalls.Prefix)?ElfWalls.Prefix.Length:tile.main_type.id.StartsWith(DwarfWalls.Prefix)?DwarfWalls.Prefix.Length:Prefix.Length)):tile.main_type;
  }
  public static bool Road(WorldTile tile) { var original=Original(tile); return original!=null && original.road; }
  public static bool Land(WorldTile tile) {
   if(tile==null) return false;
   var type=Original(tile);
   return type!=null && type.ground && !type.liquid && !type.block && !type.mountains && !type.summit;
  }
  public static void Restore(WorldTile tile) {
   var original=Original(tile);
   if(IsWall(tile) && original!=null) {
    Revision++;
    WallMovement.MarkWall(tile,false);
    Previous saved;
    if(previous.TryGetValue(tile,out saved)) {
     tile.setTileTypes(original,saved.Top,true); tile.Height=saved.Height; previous.Remove(tile);
    } else tile.setTileType(original,true);
   }
  }
 }
 public static class WallArt {
  // RGBA pixels in Unity's bottom-up order; also exported as PNGs with the release.
  public static byte[] Pixels(bool construction,int variant) {
   var pixels=new byte[16*16*4];
   for(int y=0;y<16;y++) for(int x=0;x<16;x++) {
    int i=(y*16+x)*4;
    if(construction && y>8 || !construction && y>=13 && x%8>=4) continue;
    bool mortar=y%4==0 || (x+(y/4%2)*4)%8==0;
    int shade=mortar?64:133+(x*7+y*3+variant*11)%17;
    if(!construction && (y==12 || y==15)) shade=193;
    if(x==0 || y==0) shade=55;
    pixels[i]=(byte)shade; pixels[i+1]=(byte)(shade*98/100); pixels[i+2]=(byte)(shade*94/100); pixels[i+3]=255;
    if(construction && (x==2 || x==13 || y==7)) { pixels[i]=151; pixels[i+1]=100; pixels[i+2]=52; }
   } return pixels;
  }
 }
 public static class WallMovement {
  const int ChunkShift=4;
  static readonly Dictionary<long,int> occupied=new Dictionary<long,int>();
  static readonly Dictionary<Building,List<long>> gateChunks=new Dictionary<Building,List<long>>();
  static bool indexed;
  static long Key(int x,int y) { return ((long)(x>>ChunkShift)<<32) | (uint)(y>>ChunkShift); }
  static void Count(long key,int delta) {
   int count; occupied.TryGetValue(key,out count); count+=delta;
   if(count<=0) occupied.Remove(key); else occupied[key]=count;
  }
  public static void MarkWall(WorldTile tile,bool present) {
   if(indexed && tile!=null) Count(Key(tile.x,tile.y),present?1:-1);
  }
  public static void TrackGate(Building gate,bool present) {
   if(!indexed || gate==null) return;
   List<long> keys;
   if(gateChunks.TryGetValue(gate,out keys)) {
    if(present) return;
    foreach(var key in keys) Count(key,-1);
    gateChunks.Remove(gate);
   }
   if(!present || !Gates.Intact(gate) || gate.current_tile==null) return;
   keys=new List<long>();
   var f=((BuildingAsset)AccessTools.Field(typeof(Building),"asset").GetValue(gate)).fundament;
   for(int x=gate.current_tile.x-f.left;x<=gate.current_tile.x+f.right;x++) for(int y=gate.current_tile.y-f.bottom;y<=gate.current_tile.y+f.top;y++) {
    long key=Key(x,y); if(keys.Contains(key)) continue; keys.Add(key); Count(key,1);
   }
   gateChunks[gate]=keys;
  }
  public static void IndexWorld(WorldTile[] tiles) {
   occupied.Clear(); gateChunks.Clear(); indexed=false;
   if(tiles==null) return;
   foreach(var tile in tiles) if(Walls.IsWall(tile)) Count(Key(tile.x,tile.y),1);
   indexed=true;
   foreach(var gate in Gates.All) TrackGate(gate,true);
  }
  static bool MayCross(int x,int y,int tx,int ty) {
   if(!indexed) return true;
   // A missing wall segment can be sealed by terrain up to two tiles away.
   int x0=(Math.Min(x,tx)-WallGap.Reach)>>ChunkShift,x1=(Math.Max(x,tx)+WallGap.Reach)>>ChunkShift;
   int y0=(Math.Min(y,ty)-WallGap.Reach)>>ChunkShift,y1=(Math.Max(y,ty)+WallGap.Reach)>>ChunkShift;
   for(int cx=x0;cx<=x1;cx++) for(int cy=y0;cy<=y1;cy++) if(occupied.ContainsKey(((long)cx<<32)|(uint)cy)) return true;
   return false;
  }
  static bool Crosses(int x,int y,int tx,int ty,Actor actor) {
   int nx=Math.Abs(tx-x),ny=Math.Abs(ty-y),sx=Math.Sign(tx-x),sy=Math.Sign(ty-y),ix=0,iy=0;
   // A* neighbours use the original single index probe. A long route only
   // needs chunks near its line, not every chunk in its bounding rectangle.
   bool checkEachTile=nx>1 || ny>1;
   if(!checkEachTile && !MayCross(x,y,tx,ty)) return false;
   // Keep the supercover traversal so diagonal corners cannot pass through
   // walls and each probe avoids allocating an actor-capturing delegate.
   while(ix<nx || iy<ny) {
    long decision=(1L+2L*ix)*ny-(1L+2L*iy)*nx;
    if(decision==0) {
     if(BlockedAt(x+sx,y,actor,checkEachTile) || BlockedAt(x,y+sy,actor,checkEachTile)) return true;
     x+=sx; y+=sy; ix++; iy++;
    } else if(decision<0) { x+=sx; ix++; }
    else { y+=sy; iy++; }
    if(BlockedAt(x,y,actor,checkEachTile)) return true;
   }
   return false;
  }
  public static bool LineBlocked(Actor actor,WorldTile target) {
   var from=actor==null?null:actor.current_tile;
   return from!=null && target!=null && MayCross(from.x,from.y,target.x,target.y) &&
    Crosses(from.x,from.y,target.x,target.y,actor);
  }
  static readonly System.Reflection.MethodInfo Stop=AccessTools.Method(typeof(Actor),"stopMovement");
  static readonly System.Reflection.MethodInfo ClearPath=AccessTools.Method(typeof(Actor),"clearOldPath"),SetTarget=AccessTools.Method(typeof(Actor),"setTileTarget"),Flying=AccessTools.Method(typeof(Actor),"isFlying"),InWater=AccessTools.Method(typeof(BaseSimObject),"isInWater"),InLiquid=AccessTools.Method(typeof(BaseSimObject),"isInLiquid"),Status=AccessTools.Method(typeof(BaseSimObject),"hasStatus"),CalcPath=AccessTools.Method(typeof(MapBox),"calcPath"),ResetParam=AccessTools.Method(typeof(EpPathFinding.cs.AStarParam),"resetParam");
  static readonly System.Reflection.FieldInfo TileTarget=AccessTools.Field(typeof(Actor),"tile_target"),Param=AccessTools.Field(typeof(MapBox),"pathfinding_param"),Visualiser=AccessTools.Field(typeof(MapBox),"path_finding_visualiser"),RegionFinder=AccessTools.Field(typeof(MapBox),"region_path_finder"),SearchMax=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"max_open_list"),Ground=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"ground"),Ocean=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"ocean"),Fire=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"fire"),Lava=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"lava"),Limit=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"limit"),GlobalLock=AccessTools.Field(typeof(EpPathFinding.cs.AStarParam),"use_global_path_lock");
  [ThreadStatic] static Actor pathActor;
  [ThreadStatic] static bool rerouting;
  static readonly Func<int,int,bool> WallAt=(x,y)=>Walls.IsWall(MapBox.instance.GetTile(x,y));
  static readonly Func<int,int,bool> AnchorAt=(x,y)=>IsAnchor(MapBox.instance.GetTile(x,y));
  static bool IsAnchor(WorldTile tile) {
   if(tile==null) return true; // The map boundary closes the far end of a gap.
   var type=tile.Type;
   // Native actor collision follows terrain, not vegetation buildings. A tree
   // that sprouts beside a wall must not turn an open work lane into a seal.
   return type!=null && (type.block || type.mountains || type.summit);
  }
  static bool GapBlocks(WorldTile tile) {
   return tile!=null && MayCross(tile.x,tile.y,tile.x,tile.y) && WallGap.Seals(tile.x,tile.y,WallAt,AnchorAt);
  }
  public static bool Blocks(WorldTile tile,Actor actor) { return Walls.IsWall(tile) || Gates.Blocks(tile,actor) || GapBlocks(tile); }
  static bool BlockedAt(int x,int y,Actor actor,bool checkNearby) {
   // Gap seals cannot reach farther than WallGap.Reach from a wall. Skip map
   // and building lookups for long-route tiles with no nearby masonry/gate.
   if(checkNearby && !MayCross(x,y,x,y)) return false;
   var tile=MapBox.instance.GetTile(x,y);
   return Walls.IsWall(tile) || Gates.Blocks(tile,actor) || tile!=null && WallGap.Seals(x,y,WallAt,AnchorAt);
  }
  public static bool PathBlocked(WorldTile tile,WorldTile from) {
   if(tile==null) return true;
   if(from==null) return MayCross(tile.x,tile.y,tile.x,tile.y) && Blocks(tile,pathActor);
   return Crosses(from.x,from.y,tile.x,tile.y,pathActor);
  }
  sealed class MotionEpoch { public int Revision=-1; public Kingdom Kingdom; public MotionEpoch() {} }
  sealed class PathFault { public WorldTile From, Target, Requested; public Building Gate; public int Revision, Tick, Failures; public PathFault() {} }
  public struct PathState { public Actor PreviousActor; public WorldTile Requested; public Building Gate; }
  sealed class TreeRoutes {
   public int X, Y, Revision, Tick;
   public Kingdom Kingdom;
   public readonly Dictionary<Building,KeyValuePair<WorldTile,bool>> Results=new Dictionary<Building,KeyValuePair<WorldTile,bool>>();
   public TreeRoutes() {}
  }
  static System.Runtime.CompilerServices.ConditionalWeakTable<Actor,MotionEpoch> motionEpochs=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,MotionEpoch>();
  static System.Runtime.CompilerServices.ConditionalWeakTable<Actor,PathFault> pathFaults=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,PathFault>();
  static System.Runtime.CompilerServices.ConditionalWeakTable<Actor,TreeRoutes> treeRoutes=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,TreeRoutes>();
  static int pathErrorReports;
  static float nextRescue;
  public static void Reset() { motionEpochs=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,MotionEpoch>(); pathFaults=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,PathFault>(); treeRoutes=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,TreeRoutes>(); pathErrorReports=0; nextRescue=0; pathActor=null; rerouting=false; Walls.Revision=0; occupied.Clear(); gateChunks.Clear(); indexed=false; }
  public static bool HasPhysicalOccupant(WorldTile tile) {
   var actors=MapBox.instance?.units?.units_only_alive;
   if(tile==null || actors==null) return false;
   // moveTo may advance current_tile before current_position reaches the next tile.
   // The wall tile's _units list alone can therefore miss someone standing here.
   foreach(var actor in actors) if(actor!=null && actor.isAlive() &&
    Mathf.FloorToInt(actor.current_position.x)==tile.x && Mathf.FloorToInt(actor.current_position.y)==tile.y) return true;
   return false;
  }
  static WorldTile LandingFor(Actor actor,WorldTile wall) {
   var map=MapBox.instance;
   for(int radius=1;radius<=3;radius++) {
    WorldTile best=null; float bestScore=float.MaxValue;
    for(int dx=-radius;dx<=radius;dx++) for(int dy=-radius;dy<=radius;dy++) {
     if(Math.Max(Math.Abs(dx),Math.Abs(dy))!=radius) continue;
     var tile=map.GetTile(wall.x+dx,wall.y+dy);
     var type=tile?.Type;
     if(type==null || !type.ground || type.block || type.lava || Blocks(tile,actor)) continue;
     float score=dx*dx+dy*dy+0.001f*((tile.posV3.x-actor.current_position.x)*(tile.posV3.x-actor.current_position.x)+
      (tile.posV3.y-actor.current_position.y)*(tile.posV3.y-actor.current_position.y));
     if(score<bestScore) { best=tile; bestScore=score; }
    }
    if(best!=null) return best;
   }
   return null;
  }
  public static void RescueEmbedded() {
   if(Time.unscaledTime<nextRescue) return;
   nextRescue=Time.unscaledTime+1;
   var map=MapBox.instance;
   var actors=map?.units?.units_only_alive;
   if(actors==null) return;
   List<Actor> trapped=null;
   foreach(var actor in actors) {
    if(actor==null || !actor.isAlive() || actor.asset==null || actor.asset.is_boat) continue;
    var wall=map.GetTile(Mathf.FloorToInt(actor.current_position.x),Mathf.FloorToInt(actor.current_position.y));
    if(!Walls.IsWall(wall) || (bool)Flying.Invoke(actor,null)) continue;
    if(trapped==null) trapped=new List<Actor>();
    trapped.Add(actor);
   }
   if(trapped==null) return;
   int rescued=0;
   foreach(var actor in trapped) {
    if(!actor.isAlive()) continue;
    var wall=map.GetTile(Mathf.FloorToInt(actor.current_position.x),Mathf.FloorToInt(actor.current_position.y));
    if(!Walls.IsWall(wall)) continue;
    var landing=LandingFor(actor,wall);
    if(landing==null) continue;
    actor.cancelAllBeh(); actor.stopMovement();
    actor.current_position=new Vector2(landing.posV3.x,landing.posV3.y);
    actor.current_tile=landing;
    motionEpochs.Remove(actor);
    rescued++;
   }
   if(rescued>0) Debug.Log("Kingdom Stone Walls moved "+rescued+" units off finished walls.");
  }
  public static void Register() {
   var harmony=new Harmony("custom.kingdom_stone_walls.movement");
   harmony.Patch(AccessTools.Method(typeof(ai.ActorTool),"findNewTargetInZones"),transpiler:new HarmonyMethod(typeof(WallMovement),"ReachableResourceTargets"));
   harmony.Patch(AccessTools.Method(typeof(ActorMove),"goTo"),prefix:new HarmonyMethod(typeof(WallMovement),"PathContext"),postfix:new HarmonyMethod(typeof(WallMovement),"FinishPath"),finalizer:new HarmonyMethod(typeof(WallMovement),"RecoverPathError"));
   harmony.Patch(AccessTools.Method(typeof(ActorMove),"goToCurved"),prefix:new HarmonyMethod(typeof(WallMovement),"CurvedContext"),finalizer:new HarmonyMethod(typeof(WallMovement),"EndPathContext"));
   harmony.Patch(AccessTools.Method(typeof(EpPathFinding.cs.AStarFinder),"FindPath"),prefix:new HarmonyMethod(typeof(WallMovement),"SearchLimit"),transpiler:new HarmonyMethod(typeof(WallMovement),"SolidNeighbors"));
   harmony.Patch(AccessTools.Method(typeof(PathfinderTools),"tryToGetSimplePath"),postfix:new HarmonyMethod(typeof(WallMovement),"SimplePath"));
   harmony.Patch(AccessTools.Method(typeof(Actor),"moveTo"),prefix:new HarmonyMethod(typeof(WallMovement),"Move"));
   harmony.Patch(AccessTools.Method(typeof(Actor),"updateMovement"),prefix:new HarmonyMethod(typeof(WallMovement),"OngoingMove"));
   harmony.Patch(AccessTools.Method(typeof(Actor),"checkVelocityAgainstBlock"),prefix:new HarmonyMethod(typeof(WallMovement),"Velocity"));
   harmony.Patch(AccessTools.Method(typeof(WorldTile),"get_Type"),postfix:new HarmonyMethod(typeof(WallMovement),"WallType"));
  }
  public static void CurvedContext(Actor pActor,out Actor __state) { __state=pathActor; pathActor=pActor; }
  static bool ReadyForNativePath(Actor actor,WorldTile target) {
   // goTo dereferences tile types, region islands and the pathfinding services.
   // A removed or not-yet-indexed tile can still be a non-null AI destination.
   if(actor==null || actor.current_tile==null || actor.asset==null || actor.current_path==null || target==null) return false;
   var current=actor.current_tile;
   if(current.region==null || target.region==null || current.region.island==null || target.region.island==null ||
    current.Type==null || target.Type==null) return false;
   var map=MapBox.instance;
   return map!=null && Param.GetValue(map)!=null && Visualiser.GetValue(map)!=null && RegionFinder.GetValue(map)!=null;
  }
  public static bool PathContext(Actor pActor,ref WorldTile pTileTarget,ref ai.ExecuteEvent __result,out PathState __state) {
   __state=new PathState { PreviousActor=pathActor }; pathActor=pActor;
   if(!ReadyForNativePath(pActor,pTileTarget)) {
    __result=ai.ExecuteEvent.False; return false;
   }
   var requested=pTileTarget;
   var gate=pTileTarget.building;
   bool siege=Gates.Intact(gate) && !Gates.CanPass(pActor,gate) && pActor.areFoes(gate);
   if(siege && !Gates.RouteToSiege(pActor,gate)) { __result=ai.ExecuteEvent.False; return false; }
   __state.Requested=requested; __state.Gate=siege?gate:null;
   PathFault fault;
   if(siege && pathFaults.TryGetValue(pActor,out fault) && fault.From==pActor.current_tile &&
    fault.Requested==requested && fault.Gate==gate &&
    unchecked((uint)(Environment.TickCount-fault.Tick))<RetryDelay(fault)) {
    __result=ai.ExecuteEvent.False; return false;
   }
   if(siege) {
    var approach=Gates.Approach(pActor,gate); if(approach!=null && ReadyForNativePath(pActor,approach)) pTileTarget=approach;
   }
   if(pathFaults.TryGetValue(pActor,out fault) && fault.From==pActor.current_tile &&
    (fault.Target==pTileTarget || fault.Requested==requested) && fault.Gate==(siege?gate:null) &&
    (siege || fault.Revision==Walls.Revision) &&
    unchecked((uint)(Environment.TickCount-fault.Tick))<RetryDelay(fault)) {
    __result=ai.ExecuteEvent.False; return false;
   }
   return true;
  }
  static uint RetryDelay(PathFault fault) { return fault.Gate==null?2000u:(uint)(2000<<Math.Min(Math.Max(fault.Failures-1,0),3)); }
  public static Exception EndPathContext(Exception __exception,Actor __state) { pathActor=__state; return __exception; }
  public static Exception RecoverPathError(Exception __exception,PathState __state,Actor pActor,WorldTile pTileTarget,ref ai.ExecuteEvent __result) {
   pathActor=__state.PreviousActor;
   if(!(__exception is NullReferenceException)) return __exception;
   // The AI can choose another action when native pathfinding encounters a stale tile.
   // Briefly reject a repeated request instead of throwing every simulation update.
   __result=ai.ExecuteEvent.False;
   if(pActor!=null) {
    if(pActor.current_path!=null) pActor.current_path.Clear();
    var fault=pathFaults.GetOrCreateValue(pActor);
    var requested=__state.Requested;
    var gate=__state.Gate;
    bool repeat=fault.From==pActor.current_tile && fault.Target==pTileTarget && fault.Requested==requested && fault.Gate==gate;
    fault.Failures=repeat?Math.Min(fault.Failures+1,4):1;
    fault.From=pActor.current_tile; fault.Target=pTileTarget;
    fault.Requested=requested; fault.Gate=gate;
    fault.Revision=Walls.Revision; fault.Tick=Environment.TickCount;
   }
   if(System.Threading.Interlocked.Increment(ref pathErrorReports)<=3)
    Debug.LogWarning("Kingdom Stone Walls recovered a null actor route at "+
     (pActor?.current_tile==null?"?":pActor.current_tile.x+","+pActor.current_tile.y)+" -> "+
     (pTileTarget==null?"?":pTileTarget.x+","+pTileTarget.y)+": "+__exception);
   return null;
  }
  public static void SearchLimit(EpPathFinding.cs.AStarParam __0) {
   // Native A* otherwise explores a large region on every failed gate approach.
   var requested=pathActor==null?null:TileTarget.GetValue(pathActor) as WorldTile;
   var gate=requested==null?null:requested.building;
   if(rerouting || gate!=null && Gates.Intact(gate) && !Gates.CanPass(pathActor,gate) && pathActor.areFoes(gate)) SearchMax.SetValue(__0,2048);
  }
  static bool PathCrosses(Actor actor,IEnumerable<WorldTile> path) {
   int x=(int)actor.current_position.x,y=(int)actor.current_position.y;
   foreach(var tile in path) {
    if(tile==null || Crosses(x,y,tile.x,tile.y,actor)) return true;
    x=tile.x; y=tile.y;
   }
   return false;
  }
  static bool PathCrosses(Actor actor) { return PathCrosses(actor,actor.current_path); }
  static void PreparePath(Actor actor,WorldTile start,MapBox map) {
   var param=(EpPathFinding.cs.AStarParam)Param.GetValue(map); ResetParam.Invoke(param,null);
   bool water=actor.isWaterCreature();
   Ground.SetValue(param,!water || actor.asset.force_land_creature || !(bool)InWater.Invoke(actor,null));
   Ocean.SetValue(param,water || (bool)InLiquid.Invoke(actor,null));
   Fire.SetValue(param,actor.isImmuneToFire() || (bool)Status.Invoke(actor,new object[]{"burning"}) || start.isOnFire());
   Lava.SetValue(param,actor.isImmuneToFire() || start.Type.lava);
   Limit.SetValue(param,true); GlobalLock.SetValue(param,false);
  }
  public static bool CanReach(Actor actor,WorldTile target) {
   if(!ReadyForNativePath(actor,target) || actor.asset.is_boat || (bool)Flying.Invoke(actor,null) || Blocks(target,actor)) return false;
   var map=MapBox.instance;
   var start=map.GetTile((int)actor.current_position.x,(int)actor.current_position.y);
   if(start==null || start.Type==null || !start.isSameIsland(target)) return false;
   if(start==target) return true;
   var previousActor=pathActor; var previousRerouting=rerouting;
   try {
    pathActor=actor; rerouting=true;
    PreparePath(actor,start,map);
    var path=new List<WorldTile>();
    return (bool)CalcPath.Invoke(map,new object[]{start,target,path}) && path.Count>0 && !PathCrosses(actor,path);
   } catch(System.Reflection.TargetInvocationException exception) when(exception.InnerException is NullReferenceException) {
    return false;
   } finally { pathActor=previousActor; rerouting=previousRerouting; }
  }
  public static void FinishPath(Actor pActor,WorldTile pTileTarget,PathState __state,ref ai.ExecuteEvent __result) {
   if(!ReadyForNativePath(pActor,pTileTarget)) return;
   // A failed attack route otherwise runs A* again on every fighting tick.
   // Gate retries back off while the actor stays put; ordinary wall routes retry
   // after two seconds or when the walls change.
   if(__result==ai.ExecuteEvent.False) {
    RememberFailedPath(pActor,pTileTarget,__state.Requested,__state.Gate);
    return;
   }
   // The native same-region shortcut can return a single unchecked destination.
   // Region locks can also hide the passage needed to get around a completed ring.
   if(!PathCrosses(pActor) && (pActor.current_path.Count>0 || !Crosses((int)pActor.current_position.x,(int)pActor.current_position.y,pTileTarget.x,pTileTarget.y,pActor))) {
    pathFaults.Remove(pActor); return;
   }
   __result=RebuildPath(pActor,pTileTarget)?ai.ExecuteEvent.True:ai.ExecuteEvent.False;
   if(__result==ai.ExecuteEvent.True) pathFaults.Remove(pActor);
   else RememberFailedPath(pActor,pTileTarget,__state.Requested,__state.Gate);
  }
  static void RememberFailedPath(Actor actor,WorldTile target,WorldTile requested,Building gate) {
   int x=(int)actor.current_position.x,y=(int)actor.current_position.y;
   if(gate==null && !MayCross(x,y,target.x,target.y)) return;
   var fault=pathFaults.GetOrCreateValue(actor);
   bool repeat=fault.From==actor.current_tile && fault.Target==target && fault.Requested==requested && fault.Gate==gate &&
      (gate!=null || fault.Revision==Walls.Revision);
   if(repeat && unchecked((uint)(Environment.TickCount-fault.Tick))<RetryDelay(fault)) return;
   fault.Failures=repeat?Math.Min(fault.Failures+1,4):1;
   fault.From=actor.current_tile; fault.Target=target;
   fault.Requested=requested; fault.Gate=gate;
   fault.Revision=Walls.Revision; fault.Tick=Environment.TickCount;
  }
  public static bool ResourceTargetReachable(string type,Actor actor,Building resource) {
   if(actor==null || actor.current_tile==null || resource==null || resource.current_tile==null) return false;
   var target=resource.current_tile;
   if(Blocks(target,actor)) return false;
   // The native picker uses this list for trees, minerals, fruit, hives and
   // other harvestables. A blocked target must not enter the random pool: a
   // failed walk otherwise picks it again every time the task restarts.
   // Clear straight approaches avoid the full path search.
   int x=(int)actor.current_position.x,y=(int)actor.current_position.y;
   if(!MayCross(x,y,target.x,target.y)) return true;
   if(!Crosses(x,y,target.x,target.y,actor)) return true;
   var routes=treeRoutes.GetOrCreateValue(actor);
   int now=Environment.TickCount;
   var kingdom=Gates.Membership(actor);
   if(routes.X!=x || routes.Y!=y || routes.Revision!=Walls.Revision || routes.Kingdom!=kingdom ||
      unchecked((uint)(now-routes.Tick))>10000u) {
    routes.X=x; routes.Y=y; routes.Revision=Walls.Revision; routes.Kingdom=kingdom; routes.Tick=now;
    routes.Results.Clear();
   }
   KeyValuePair<WorldTile,bool> previous;
   if(routes.Results.TryGetValue(resource,out previous) && previous.Key==target) return previous.Value;
   bool reachable=CanReach(actor,target);
   if(routes.Results.Count>=128) routes.Results.Clear();
   routes.Results[resource]=new KeyValuePair<WorldTile,bool>(target,reachable);
   return reachable;
  }
  public static IEnumerable<CodeInstruction> ReachableResourceTargets(IEnumerable<CodeInstruction> source,ILGenerator generator) {
   var codes=source.ToList();
   var add=AccessTools.Method(typeof(ListPool<Building>),"Add");
   int at=codes.FindIndex(c=>c.Calls(add));
   if(at<2 || codes.Count(c=>c.Calls(add))!=1 || codes[at-2].opcode!=OpCodes.Ldarg_2 || !codes[at-1].IsLdloc())
    throw new InvalidOperationException("Unsupported WorldBox tree target selection layout");
   var skip=generator.DefineLabel();
   codes[at+1].labels.Add(skip);
   codes.InsertRange(at-2,new[]{
    new CodeInstruction(OpCodes.Ldarg_1),
    new CodeInstruction(OpCodes.Ldarg_0),
    new CodeInstruction(codes[at-1].opcode,codes[at-1].operand),
    new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(WallMovement),"ResourceTargetReachable")),
    new CodeInstruction(OpCodes.Brfalse,skip)
   });
   return codes;
  }
  static bool RebuildPath(Actor actor,WorldTile target) {
   var map=MapBox.instance;
   var start=map.GetTile((int)actor.current_position.x,(int)actor.current_position.y);
   if(start==null || target==null || Blocks(target,actor) || actor.asset.is_boat || (bool)Flying.Invoke(actor,null) || !start.isSameIsland(target)) { ClearPath.Invoke(actor,null); return false; }
   var previousActor=pathActor; var previousRerouting=rerouting;
   try {
    pathActor=actor; rerouting=true;
    ClearPath.Invoke(actor,null);
    PreparePath(actor,start,map);
    if(!(bool)CalcPath.Invoke(map,new object[]{start,target,actor.current_path}) || actor.current_path.Count==0 || PathCrosses(actor)) { ClearPath.Invoke(actor,null); return false; }
    actor.split_path=EpPathFinding.cs.AStarFinder.result_split_path?SplitPathStatus.Prepare:SplitPathStatus.Normal;
    SetTarget.Invoke(actor,new object[]{target});
    motionEpochs.GetOrCreateValue(actor).Revision=-1;
    return true;
   } finally { pathActor=previousActor; rerouting=previousRerouting; }
  }
  public static void WallType(WorldTile __instance,ref TileTypeBase __result) {
   // A biome/road top layer must not conceal a completed wall's collision or artwork.
   if(Walls.IsWall(__instance)) __result=__instance.main_type;
  }
  public static IEnumerable<CodeInstruction> SolidNeighbors(IEnumerable<CodeInstruction> source,ILGenerator generator) {
   var codes=source.ToList();
   var type=AccessTools.Method(typeof(WorldTile),"get_Type");
   var block=AccessTools.Field(typeof(TileTypeBase),"block");
   int at=codes.FindIndex(c=>c.Calls(type));
   if(at<0 || codes.Count(c=>c.Calls(type))!=1) throw new InvalidOperationException("Unsupported WorldBox AStar neighbor layout");
   CodeInstruction rejection=null;
   for(int i=at+1;i<Math.Min(at+14,codes.Count-1);i++) {
    if(codes[i].opcode==OpCodes.Ldfld && Equals(codes[i].operand,block)) { rejection=codes[i+1]; break; }
   }
   if(rejection==null || !(rejection.operand is Label)) throw new InvalidOperationException("Missing WorldBox blocked-neighbor branch");
   var normal=generator.DefineLabel();
   var first=new CodeInstruction(OpCodes.Dup);
   first.labels.AddRange(codes[at].labels); codes[at].labels.Clear();
   first.blocks.AddRange(codes[at].blocks); codes[at].blocks.Clear();
   codes[at].labels.Add(normal);
   // Derive the current tile local from the native neighbours lookup.
   var neighbors=AccessTools.Field(typeof(WorldTile),"neighbours");
   int neighborsAt=codes.FindIndex(c=>c.opcode==OpCodes.Ldfld && Equals(c.operand,neighbors));
   if(neighborsAt<1 || !codes[neighborsAt-1].IsLdloc()) throw new InvalidOperationException("Missing WorldBox AStar current tile");
   var from=new CodeInstruction(codes[neighborsAt-1].opcode,codes[neighborsAt-1].operand);
   codes.InsertRange(at,new[]{first,from,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(WallMovement),"PathBlocked")),new CodeInstruction(OpCodes.Brfalse,normal),new CodeInstruction(OpCodes.Pop),new CodeInstruction(OpCodes.Br,rejection.operand)});
   return codes;
  }
  public static void SimplePath(ref bool __result) {
   if(!__result) return;
   var tiles=PathfinderTools.getLastRaycastResult();
   for(int i=0;i<tiles.Count;i++) if(PathBlocked(tiles[i],i==0?null:tiles[i-1])) { __result=false; return; }
  }
  public static bool Move(Actor __instance,WorldTile pTileTarget) {
   if(pTileTarget==null || __instance.current_tile==null) return true;
   if(Crosses((int)__instance.current_position.x,(int)__instance.current_position.y,pTileTarget.x,pTileTarget.y,__instance)) { RecoverMove(__instance,pTileTarget.x,pTileTarget.y); return false; }
   return true;
  }
  public static bool Velocity(Actor __instance,Vector2 pNewPos,ref Vector2 __result) {
   if(__instance.current_tile==null) return true;
   if(Crosses((int)__instance.current_position.x,(int)__instance.current_position.y,(int)pNewPos.x,(int)pNewPos.y,__instance)) { __result=__instance.current_position; return false; }
   return true;
  }
  public static bool OngoingMove(Actor __instance) {
   // A wall may finish while a unit is following an already accepted long, straight path.
   // Check that remaining segment once per terrain revision, without scanning it every frame.
   var epoch=motionEpochs.GetOrCreateValue(__instance);
   var kingdom=Gates.Membership(__instance);
   if(epoch.Revision==Walls.Revision && epoch.Kingdom==kingdom) return true;
   epoch.Revision=Walls.Revision; epoch.Kingdom=kingdom;
   if(Crosses((int)__instance.current_position.x,(int)__instance.current_position.y,(int)__instance.next_step_position.x,(int)__instance.next_step_position.y,__instance)) {
    RecoverMove(__instance,(int)__instance.next_step_position.x,(int)__instance.next_step_position.y); return false;
   }
   return true;
  }
  static void RecoverMove(Actor actor,int tx,int ty) {
   var target=(WorldTile)TileTarget.GetValue(actor)??MapBox.instance.GetTile(tx,ty);
   Building gate=null;
   WallLine.Crosses((int)actor.current_position.x,(int)actor.current_position.y,tx,ty,(x,y)=>{
    var tile=MapBox.instance.GetTile(x,y);
    if(Gates.Blocks(tile,actor)) { gate=tile.building; return true; }
    return Walls.IsWall(tile);
   });
   Stop.Invoke(actor,null);
   if(gate!=null) {
    // A closed gate has no route for an animal or a neutral traveler. Let their
    // AI choose another action instead of retrying an expensive blocked route.
    if(!Gates.Retarget(actor,gate)) actor.cancelAllBeh();
    return;
   }
   if(!rerouting && RebuildPath(actor,target)) { actor.updatePathMovement(); return; }
   // Let the AI choose another target when no passage exists; clearing movement
   // alone leaves a successful army movement behaviour with no path to finish.
   actor.cancelAllBeh();
  }
 }
 public static class WallGap {
  // The planner needs two clear tiles around masonry. Seal only a corridor of
  // that width between a completed wall and solid terrain or the map edge.
  public const int Reach=3;
  public static bool Seals(int x,int y,Func<int,int,bool> wall,Func<int,int,bool> anchor) {
   for(int axis=0;axis<2;axis++) for(int sign=-1;sign<=1;sign+=2) {
    int dx=axis==0?sign:0,dy=axis==1?sign:0;
    for(int wallDistance=1;wallDistance<=Reach;wallDistance++) {
     if(!wall(x-dx*wallDistance,y-dy*wallDistance)) continue;
     for(int anchorDistance=0;anchorDistance<=Reach-wallDistance;anchorDistance++)
      if(anchor(x+dx*anchorDistance,y+dy*anchorDistance)) return true;
    }
   }
   return false;
  }
 }
 public static class WallLine {
  // Supercover traversal checks both corner-adjacent tiles, preventing diagonal clipping.
  public static bool Crosses(int x,int y,int tx,int ty,Func<int,int,bool> blocked) {
   int nx=Math.Abs(tx-x),ny=Math.Abs(ty-y),sx=Math.Sign(tx-x),sy=Math.Sign(ty-y),ix=0,iy=0;
   while(ix<nx || iy<ny) {
    long decision=(1L+2L*ix)*ny-(1L+2L*iy)*nx;
    if(decision==0) {
     if(blocked(x+sx,y) || blocked(x,y+sy)) return true;
     x+=sx; y+=sy; ix++; iy++;
    } else if(decision<0) { x+=sx; ix++; }
    else { y+=sy; iy++; }
    if(blocked(x,y)) return true;
   }
   return false;
  }
 }
 public static class RingOrder {
  // Only finished wall terrain counts: scaffolds, queued conversion and temporary blockers do not.
  public static int FirstIncomplete<T>(IList<T>[] rings,Func<T,bool> completed) {
   for(int ring=0;ring<rings.Length;ring++) foreach(var segment in rings[ring]) if(!completed(segment)) return ring;
   return -1;
  }
 }
 public static class FixedLayout {
  public const int Inner=24,Outer=32,Clearance=2,Gate=4;
  public const string LayoutKey="ksw_human_gatehouses";
  public static bool Refresh(Fortification plan) {
   if(!plan.HumanMigration || !Construction.IsCapital(plan.City)) return false;
   Construction.StopCrew(plan);
   var keep=new HashSet<KeyValuePair<int,int>>(Gatehouses(plan.X,plan.Y));
   foreach(var p in Ring(plan.X,plan.Y,Inner)) {
    if(keep.Contains(p)) continue;
    var tile=MapBox.instance.GetTile(p.Key,p.Value); if(tile==null) continue;
    if(Construction.IsSite(tile.building) && tile.building.getCity()==plan.City) {Construction.RemoveSite(tile.building,true);return true;}
    if(Construction.OwnsTile(tile,plan.City) && Walls.IsWall(tile) && tile.main_type.id.StartsWith(Walls.Prefix)) {Walls.Restore(tile);return true;}
   }
   plan.City.data.set(Fortification.MaskKey,"");
   plan.City.data.set(Fortification.VegetationKey,false);
   plan.City.data.set(LayoutKey,true);
   plan.HumanMigration=false;
   return true;
  }
  public static IEnumerable<KeyValuePair<int,int>> Points(int cx,int cy) {
   foreach(var point in Gatehouses(cx,cy)) yield return point;
   foreach(var point in Ring(cx,cy,Outer)) yield return point;
  }
  public static IEnumerable<KeyValuePair<int,int>> OldPoints(int cx,int cy) {
   foreach(int radius in new[]{Inner,Outer}) foreach(var point in Ring(cx,cy,radius)) yield return point;
  }
  public static IEnumerable<KeyValuePair<int,int>> Gatehouses(int cx,int cy) {
   // One short passage on each side, with a gate at both ends.
   for(int distance=Inner;distance<Outer;distance++) foreach(int side in new[]{-1,1}) {
    int shoulder=side*(Gate+1);
    yield return new KeyValuePair<int,int>(cx+shoulder,cy+distance);
    yield return new KeyValuePair<int,int>(cx+shoulder,cy-distance);
    yield return new KeyValuePair<int,int>(cx+distance,cy+shoulder);
    yield return new KeyValuePair<int,int>(cx-distance,cy+shoulder);
   }
  }
  public static IEnumerable<KeyValuePair<int,int>> Ring(int cx,int cy,int radius) {
   // Keep this order stable: existing saves store their segment mask in this order.
   for(int offset=-radius;offset<=radius;offset++) {
    if(Math.Abs(offset)<=Gate) continue;
    yield return new KeyValuePair<int,int>(cx+offset,cy-radius);
    yield return new KeyValuePair<int,int>(cx+offset,cy+radius);
    if(Math.Abs(offset)==radius) continue;
    yield return new KeyValuePair<int,int>(cx-radius,cy+offset);
    yield return new KeyValuePair<int,int>(cx+radius,cy+offset);
   }
  }
  public static int WorkOrder(int cx,int cy,int radius,int x,int y) {
   x-=cx; y-=cy;
   // A continuous perimeter route, with each corner occurring once.
   if(y==-radius) return x+radius;
   if(x==radius) return 2*radius+y+radius;
   if(y==radius) return 4*radius+radius-x;
   return 6*radius+radius-y;
  }
  static bool Overlap(int x0,int y0,int x1,int y1,int a,int b,int c,int d) {
   return x0<=c && x1>=a && y0<=d && y1>=b;
  }
  public static bool IntersectsReserved(int cx,int cy,int x0,int y0,int x1,int y1) {
   x0-=cx; x1-=cx; y0-=cy; y1-=cy;
   // Reserve the perimeter and the four gatehouse corridors.
   return LaneOverlap(Outer,x0,y0,x1,y1) ||
          GateOverlap(x0,y0,x1,y1);
  }
  static bool LaneOverlap(int r,int x0,int y0,int x1,int y1) {
    return Overlap(x0,y0,x1,y1,-r-Clearance,r-Clearance,r+Clearance,r+Clearance) ||
       Overlap(x0,y0,x1,y1,-r-Clearance,-r-Clearance,r+Clearance,-r+Clearance) ||
       Overlap(x0,y0,x1,y1,r-Clearance,-r-Clearance,r+Clearance,r+Clearance) ||
       Overlap(x0,y0,x1,y1,-r-Clearance,-r-Clearance,-r+Clearance,r+Clearance);
  }
  static bool GateOverlap(int x0,int y0,int x1,int y1) {
   // Include the side walls and their two-tile work clearance.
   int width=Gate+1+Clearance;
   return Overlap(x0,y0,x1,y1,-width,Inner-Clearance,width,Outer+Clearance) ||
          Overlap(x0,y0,x1,y1,-width,-Outer-Clearance,width,-Inner+Clearance) ||
          Overlap(x0,y0,x1,y1,Inner-Clearance,-width,Outer+Clearance,width) ||
          Overlap(x0,y0,x1,y1,-Outer-Clearance,-width,-Inner+Clearance,width);
  }
 }
 public sealed class SiteProgress { public int Value,BestDistance=int.MaxValue; public double At; }
 public static class CapitalBorders {
  static readonly Dictionary<TileZone,City> Reserved=new Dictionary<TileZone,City>();
  static readonly System.Reflection.MethodInfo AddZone=AccessTools.Method(typeof(City),"addZone");
  static readonly System.Reflection.FieldInfo Zones=AccessTools.Field(typeof(City),"zones");
  public static void Register() {
   new Harmony("custom.kingdom_stone_walls.capital_borders").Patch(AccessTools.Method(typeof(TileZone),"canBeClaimedByCity"),postfix:new HarmonyMethod(typeof(CapitalBorders),"CanClaim"));
  }
  public static void Reset() { Reserved.Clear(); }
  public static void CanClaim(TileZone __instance,City pCity,ref bool __result) {
   City capital;
   if(__result && __instance.city==null && Reserved.TryGetValue(__instance,out capital) && capital!=pCity && Construction.IsFortifiedCity(capital)) __result=false;
  }
  static void Include(WorldTile tile,City capital) {
   if(tile!=null && tile.zone!=null && (Walls.IsWall(tile) || Walls.Land(tile) || Walls.Road(tile)) && !Reserved.ContainsKey(tile.zone)) Reserved[tile.zone]=capital;
  }
  public static void Refresh(IEnumerable<Fortification> plans) {
   Reserved.Clear();
   foreach(var plan in plans) {
    var city=plan.City;
    if(!plan.Ready || !Construction.IsFortifiedCity(city)) continue;
    foreach(var ring in plan.Rings) foreach(var tile in ring) Include(tile,city);
    if(plan.IsTown || !OrcWalls.IsOrcCity(city)) {
     for(int slot=0;slot<Gates.SlotCount(plan);slot++) {
      var p=Gates.Position(plan,slot); var f=Gates.AssetFor(plan,slot).fundament;
      for(int x=p.Key-f.left;x<=p.Key+f.right;x++) for(int y=p.Value-f.bottom;y<=p.Value+f.top;y++) Include(MapBox.instance.GetTile(x,y),city);
     }
     GateTowers.ReserveFootprints(plan,(tile)=>Include(tile,city));
    }
   }
   var claimed=new Dictionary<City,int>();
   foreach(var pair in Reserved.ToArray()) {
    var zone=pair.Key; var capital=pair.Value; var donor=zone.city;
    if(donor==capital || !Construction.IsFortifiedCity(capital)) continue;
    int count; claimed.TryGetValue(capital,out count); if(count>=4) continue;
    if(donor!=null) {
     if(!donor.isAlive() || Construction.Owner(donor)!=Construction.Owner(capital)) continue;
     var donorZones=(List<TileZone>)Zones.GetValue(donor);
     if(donorZones==null || donorZones.Count<=1) continue;
    }
    AddZone.Invoke(capital,new object[]{zone}); claimed[capital]=count+1;
   }
  }
 }
 public static class GateTowers {
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"), Data=AccessTools.Field(typeof(Building),"data"), Buildings=AccessTools.Field(typeof(City),"buildings");
  static readonly System.Reflection.MethodInfo Add=AccessTools.Method(typeof(BuildingManager),"addBuilding",new Type[]{typeof(BuildingAsset),typeof(WorldTile),typeof(bool),typeof(bool),typeof(BuildPlacingType)}), SetKingdom=AccessTools.Method(typeof(Building),"setKingdomCiv"), HasResources=AccessTools.Method(typeof(City),"hasEnoughResourcesFor"), Spend=AccessTools.Method(typeof(City),"spendResourcesForBuildingAsset");
  static BuildingAsset NativeTower(Fortification plan) {
   if(plan.IsTown) return null;
   if(OrcWalls.IsOrcCity(plan.City)) return null;
   if(ElfWalls.IsElfCity(plan.City)) return ElfWalls.Tower;
   if(DwarfWalls.IsDwarfCity(plan.City)) return DwarfWalls.Tower;
   var buildings=(List<Building>)Buildings.GetValue(plan.City);
   var style=buildings.Select(b=>(BuildingAsset)Asset.GetValue(b)).Where(a=>a!=null && a.city_building && !string.IsNullOrEmpty(a.civ_kingdom)).GroupBy(a=>a.civ_kingdom).OrderByDescending(g=>g.Count()).Select(g=>g.Key).FirstOrDefault();
   return style==null?null:AssetManager.buildings.list.FirstOrDefault(a=>a.city_building && a.tower && a.civ_kingdom==style);
  }
  public static IEnumerable<KeyValuePair<int,int>> Points(Fortification plan,BuildingFundament f,int r) {
   // Foundations start immediately beside the nine-tile passage and three tiles
   // inward from masonry, preserving the existing two-tile wall work clearance.
   if(ElfWalls.IsElfCity(plan.City)) {foreach(var point in ElfLayout.TowerPoints(plan.X,plan.Y,f))yield return point;yield break;}
   if(DwarfWalls.IsDwarfCity(plan.City)) {foreach(var point in DwarfLayout.TowerPoints(plan.X,plan.Y,f))yield return point;yield break;}
   foreach(int side in new[]{-1,1}) {
    int x=side<0?-FixedLayout.Gate-1-f.right:FixedLayout.Gate+1+f.left;
    int y=side<0?-FixedLayout.Gate-1-f.top:FixedLayout.Gate+1+f.bottom;
    yield return new KeyValuePair<int,int>(plan.X+x,plan.Y+r-FixedLayout.Clearance-1-f.top);
    yield return new KeyValuePair<int,int>(plan.X+x,plan.Y-r+FixedLayout.Clearance+1+f.bottom);
    yield return new KeyValuePair<int,int>(plan.X+r-FixedLayout.Clearance-1-f.right,plan.Y+y);
    yield return new KeyValuePair<int,int>(plan.X-r+FixedLayout.Clearance+1+f.left,plan.Y+y);
   }
  }
  public static bool Intersects(Fortification plan,int x0,int y0,int x1,int y1) {
   var tower=NativeTower(plan); if(tower==null || tower.fundament==null) return false;
   var f=tower.fundament;
   foreach(var p in Points(plan,f,FixedLayout.Inner))
    if(x0<=p.Key+f.right && x1>=p.Key-f.left && y0<=p.Value+f.top && y1>=p.Value-f.bottom) return true;
   return false;
  }
  public static void ReserveFootprints(Fortification plan,Action<WorldTile> include) {
   var tower=NativeTower(plan); if(tower==null || tower.fundament==null) return;
   var f=tower.fundament;
   foreach(var p in Points(plan,f,FixedLayout.Inner))
    for(int x=p.Key-f.left;x<=p.Key+f.right;x++) for(int y=p.Value-f.bottom;y<=p.Value+f.top;y++) include(MapBox.instance.GetTile(x,y));
  }
  public static void Place(Fortification plan) {
   if(plan.IsTown || !plan.Ready || !Construction.IsCapital(plan.City) || !Construction.AtPeace(plan.City) || plan.City.getPopulationPeople()<40 || plan.City.isInDanger() || plan.City.isGettingCaptured()) return;
   var tower=NativeTower(plan); if(tower==null || tower.fundament==null) return;
   var cost=tower.cost ?? new ConstructionCost {stone=5,wood=5};
   var f=tower.fundament;
   {
    if(plan.Rings.Any(ring=>ring.Count==0 || ring.Any(t=>!CapitalStages.IsStone(t)))) return;
    const int r=FixedLayout.Inner;
    foreach(var p in Points(plan,f,r)) {
     var tile=MapBox.instance.GetTile(p.Key,p.Value); if(tile==null || tile.building!=null && !((ElfWalls.IsElfCity(plan.City) || DwarfWalls.IsDwarfCity(plan.City)) && Construction.IsVegetation(tile.building))) continue;
     bool clear=true;
     for(int x=p.Key-f.left;x<=p.Key+f.right;x++) for(int y=p.Value-f.bottom;y<=p.Value+f.top;y++) {
      var cell=MapBox.instance.GetTile(x,y);
      if(cell==null || !Construction.OwnsTile(cell,plan.City) || !(Walls.Land(cell) || Walls.Road(cell)) || Walls.IsWall(cell) || cell.building!=null && !((ElfWalls.IsElfCity(plan.City) || DwarfWalls.IsDwarfCity(plan.City)) && Construction.IsVegetation(cell.building)) || cell.hasUnits()) clear=false;
     }
     if(!clear || !(bool)HasResources.Invoke(plan.City,new object[]{cost})) continue;
     if(ElfWalls.IsElfCity(plan.City) || DwarfWalls.IsDwarfCity(plan.City)) {
      var plants=new HashSet<Building>();
      for(int x=p.Key-f.left;x<=p.Key+f.right;x++)for(int y=p.Value-f.bottom;y<=p.Value+f.top;y++){var b=MapBox.instance.GetTile(x,y).building;if(Construction.IsVegetation(b))plants.Add(b);}
      foreach(var b in plants)AccessTools.Method(typeof(Building),"removeBuildingFinal").Invoke(b,null);
     }
     var building=(Building)Add.Invoke(MapBox.instance.buildings,new object[]{tower,tile,false,false,BuildPlacingType.New});
     if(building==null) continue;
     ((BuildingData)Data.GetValue(building)).cityID=plan.City.data.id;
     SetKingdom.Invoke(building,new object[]{Construction.Owner(plan.City)});
     Spend.Invoke(plan.City,new object[]{cost});
    }
   }
  }
 }
 public static class CrewPolicy {
  public static int Size(int population) { return population<40?0:Math.Min(6,Math.Max(3,population/25)); }
 }
 public static class TowerLifecycle {
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"), Data=AccessTools.Field(typeof(Building),"data");
  static readonly System.Reflection.MethodInfo Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
  static bool IsCustomTower(Building building) {
   var asset=building==null?null:(BuildingAsset)Asset.GetValue(building);
   return asset!=null && (asset.id==ElfWalls.TowerId || asset.id==DwarfWalls.TowerId);
  }
  public static void Register() {
   var harmony=new Harmony("custom.kingdom_stone_walls.tower_lifecycle");
   harmony.Patch(AccessTools.Method(typeof(Building),"getHit"),postfix:new HarmonyMethod(typeof(TowerLifecycle),"AfterHit"));
   harmony.Patch(AccessTools.Method(typeof(Building),"startRemove"),postfix:new HarmonyMethod(typeof(TowerLifecycle),"AfterRemoveStart"));
  }
  public static void AfterHit(Building __instance) {
   if(IsCustomTower(__instance) && !__instance.hasHealth() && ((BuildingData)Data.GetValue(__instance)).state!=BuildingState.Removed) Remove.Invoke(__instance,null);
  }
  public static void AfterRemoveStart(Building __instance) {
   if(IsCustomTower(__instance) && ((BuildingData)Data.GetValue(__instance)).state!=BuildingState.Removed) Remove.Invoke(__instance,null);
  }
  public static void Cleanup() {
   foreach(var building in MapBox.instance.buildings.getSimpleList().ToArray()) {
    if(!IsCustomTower(building)) continue;
    var state=((BuildingData)Data.GetValue(building)).state;
    if(state==BuildingState.Removed) continue;
    if(state==BuildingState.Ruins || !building.isAlive() || !building.hasHealth() || building.isOnRemove()) Remove.Invoke(building,null);
   }
  }
 }
 public sealed class Fortification {
  public const string CityKey="ksw_fort_city",CenterKey="ksw_plan_center",MaskKey="ksw_plan_mask",CheckedKey="ksw_legacy_checked",VegetationKey="ksw_vegetation_plan";
  public readonly City City;
  public readonly bool IsTown;
  public int X,Y;
  public bool Ready,ElfMigration,DwarfMigration,HumanMigration;
  public double NextWork,NextReport;
  public readonly List<Actor> Crew=new List<Actor>();
  public readonly Dictionary<Building,SiteProgress> Progress=new Dictionary<Building,SiteProgress>();
  public readonly Dictionary<Building,WorldTile> WorkLanes=new Dictionary<Building,WorldTile>();
  public readonly Dictionary<WorldTile,double> UnreachableUntil=new Dictionary<WorldTile,double>();
  public int ReachabilityRevision=-1;
  public readonly Dictionary<Actor,double> Retry=new Dictionary<Actor,double>();
  public IList<WorldTile>[] Rings=new IList<WorldTile>[] {new List<WorldTile>(),new List<WorldTile>()};
  public Fortification(City city,int x,int y) {
   City=city; bool town=false;city.data.get(TownWalls.LayoutKey,out town,false);
   IsTown=town || !Construction.IsCapital(city);
   if(IsTown) TownWalls.Upgrade(city);
   string saved=null; city.data.get(CenterKey,out saved,null);
   if(IsTown && string.IsNullOrEmpty(saved)) city.data.get(Construction.TownCenterKey,out saved,null);
   var parts=(saved??"").Split(','); int sx,sy;
   if(parts.Length==2 && int.TryParse(parts[0],out sx) && int.TryParse(parts[1],out sy)) { x=sx; y=sy; }
   X=x; Y=y; SaveCenter(); if(IsTown) { city.data.set(Construction.TownCenterKey,X+","+Y); Rings=new IList<WorldTile>[] {new List<WorldTile>()}; }
   else {
    ElfWalls.Prepare(this); DwarfWalls.Prepare(this);
    if(!ElfWalls.IsElfCity(city) && !DwarfWalls.IsDwarfCity(city) && !OrcWalls.IsOrcCity(city)) {
     bool current=false; string mask=null;
     city.data.get(FixedLayout.LayoutKey,out current,false);
     city.data.get(MaskKey,out mask,null);
     HumanMigration=!current && mask!=null && mask.Length==FixedLayout.OldPoints(0,0).Count();
    }
   }
  }
  public void SaveCenter() { City.data.set(CenterKey,X+","+Y); }
  public static void Forget(MapBox map) {
   foreach(var city in map.cities.list) { city.data.set(CenterKey,""); city.data.set(Construction.TownCenterKey,""); city.data.set(MaskKey,""); city.data.set(CheckedKey,false); city.data.set(Gates.DestroyedKey,0); city.data.set(ElfWalls.LayoutKey,false); city.data.set(DwarfWalls.LayoutKey,false); city.data.set(FixedLayout.LayoutKey,false); city.data.set(TownWalls.LayoutKey,false); city.data.set(TownWalls.OldLayoutKey,false); }
   foreach(var kingdom in map.kingdoms.list) kingdom.data.set(CityKey,0L);
  }
 }
 public class Controller : MonoBehaviour {
  static readonly System.Reflection.FieldInfo Tiles=AccessTools.Field(typeof(MapBox),"tiles_list"), Elapsed=AccessTools.Field(typeof(MapBox),"elapsed"), Center=AccessTools.Field(typeof(City),"city_center");
  const int Margin=35;
  bool enabledWalls=true; float nextDiscover, nextGateRefresh, nextTowerCleanup, nextCrewUpdate, nextMigration; WorldTile[] world;
  System.Collections.IEnumerator planner;
  System.Collections.IEnumerator removal;
  void Update() {
   var map=MapBox.instance; if(map==null) return;
   var current=(WorldTile[])Tiles.GetValue(map); if(current==null) return;
   if(world!=current) { world=current; Construction.Reset(); Walls.ClearHistory(); WallMovement.Reset(); Gates.Reset(); WallMovement.IndexWorld(current); planner=null; removal=null; nextDiscover=0; nextGateRefresh=0; nextTowerCleanup=0; nextCrewUpdate=0; nextMigration=0; }
   bool allRaces=Main.Instance.GetConfig()[Main.RaceSettingGroup][Main.RaceSettingId].BoolVal;
   if(allRaces!=Main.AllRacesBuildWalls) {
    Main.AllRacesBuildWalls=allRaces; planner=null;
    Construction.TownReservations.Clear();
    foreach(var plan in Construction.CityPlans.Values) if(!Construction.IsFortifiedCity(plan.City)) Construction.StopCrew(plan);
    foreach(var city in Construction.CityPlans.Keys.Where(c=>!Construction.IsFortifiedCity(c)).ToArray()) Construction.CityPlans.Remove(city);
    foreach(var tile in Construction.Plans.Keys.Where(t=>!Construction.IsFortifiedCity(Construction.Plans[t])).ToArray()) Construction.Plans.Remove(tile);
    CapitalBorders.Reset(); nextDiscover=0;
    Debug.Log("Kingdom Stone Walls builders: "+(allRaces?"all races":"humans, orcs, elves and dwarves"));
   }
   if(Time.unscaledTime>=nextGateRefresh) { Gates.RefreshOwners(); nextGateRefresh=Time.unscaledTime+2; }
   WallMovement.RescueEmbedded();
   if(Time.unscaledTime>=nextTowerCleanup) { TowerLifecycle.Cleanup(); nextTowerCleanup=Time.unscaledTime+10; }
   if(Input.GetKeyDown(KeyCode.F8)) {
    if(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)) {
     enabledWalls=false; planner=null; Gates.Removing=true; foreach(var plan in Construction.CityPlans.Values) Construction.StopCrew(plan); Construction.Plans.Clear(); Construction.CityPlans.Clear(); Construction.TownReservations.Clear(); CapitalBorders.Reset(); Fortification.Forget(map); removal=ClearWorld();
    } else if(!Construction.Faulted) enabledWalls=!enabledWalls;
    Debug.Log("Kingdom Stone Walls construction: "+enabledWalls);
   }
   Construction.Enabled=enabledWalls && !Construction.Faulted;
   if(removal!=null) { if(!removal.MoveNext()) removal=null; return; }
   if(!Construction.Enabled) { CapitalBorders.Reset(); foreach(var plan in Construction.CityPlans.Values) Construction.StopCrew(plan); return; }
   if((float)Elapsed.GetValue(map)<=0) return;
   if(Time.unscaledTime>=nextMigration) {
    nextMigration=Time.unscaledTime+.25f;
    foreach(var plan in Construction.CityPlans.Values) if(DwarfWalls.Refresh(plan) || ElfWalls.Refresh(plan) || OrcWalls.Refresh(plan) || FixedLayout.Refresh(plan)) return;
   }
   if(Time.unscaledTime>=nextCrewUpdate) {
    nextCrewUpdate=Time.unscaledTime+.5f;
    double now=map.getCurWorldTime();
    foreach(var plan in Construction.CityPlans.Values) {
     if(!Construction.IsFortifiedCity(plan.City)) { Construction.StopCrew(plan); continue; }
     Supply.Tick(plan,now);
     Construction.TickCrew(plan,now);
    }
   }
   // At most one completed segment per frame; citizens build from the adjacent lane.
   if(Construction.Finished.Count>0) {
    var b=Construction.Finished.Dequeue(); Construction.Dequeued(b);
    if(!Construction.TryFinish(b)) Construction.Enqueue(b);
   }
   if(planner!=null) { if(!planner.MoveNext()) planner=null; return; }
   if(Time.unscaledTime<nextDiscover) return;
   nextDiscover=Time.unscaledTime+1;
   CapitalBorders.Refresh(Construction.CityPlans.Values);
   foreach(var plan in Construction.CityPlans.Values) GateTowers.Place(plan);
   foreach(var city in Construction.TownReservations.Keys.ToArray())
    if(!Construction.ReservationEligible(city) || Construction.IsCapital(city) || Construction.CityPlans.ContainsKey(city)) Construction.TownReservations.Remove(city);
   // Reserve young capitals before native house placement; construction still waits for 40 people.
   foreach(var kingdom in map.kingdoms.list) {
    if(!kingdom.isCiv()) continue;
    var city=kingdom.capital;
    if(!Construction.IsCapital(city) || Construction.Owner(city)!=kingdom) continue;
    Construction.BeforeCityBuild(city);
   }
   foreach(var city in map.cities.list) if(Construction.ReservationEligible(city) && !Construction.IsCapital(city)) Construction.BeforeCityBuild(city);
   foreach(var plan in Construction.CityPlans.Values) {
    if(plan.Ready || !Construction.IsFortifiedCity(plan.City)) continue;
    planner=Plan(map,plan,Construction.Owner(plan.City)); break;
   }
  }
  System.Collections.IEnumerator ClearWorld() {
   int n=0;
   foreach(var tile in world) {
    Walls.Restore(tile); if(Construction.IsSite(tile.building)) Construction.RemoveSite(tile.building,true);
    if(Gates.IsGate(tile.building)) {
     if(tile.building.isUnderConstruction()) Construction.RemoveSite(tile.building,true);
     else Gates.RemoveGate(tile.building);
    }
    if(++n%64==0) yield return null;
   }
   Construction.Finished.Clear(); Gates.Removing=false; nextDiscover=0;
  }
  System.Collections.IEnumerator Plan(MapBox map,Fortification plan,Kingdom kingdom) {
   if(!Construction.IsFortifiedCity(plan.City)) yield break;
   var city=plan.City; int work=0; bool town=plan.IsTown,elven=!town && ElfWalls.IsElfCity(city),dwarven=!town && DwarfWalls.IsDwarfCity(city);int margin=town?TownLayout.Margin:elven?ElfLayout.Margin:dwarven?DwarfLayout.Margin:Margin;
   bool checkedLegacy=false; city.data.get(Fortification.CheckedKey,out checkedLegacy,false);
   if(!checkedLegacy && !town) {
    // Recover 0.4's unsaved center from nearby existing segments, using bounded, sliced voting.
    var evidence=new List<WorldTile>();
    for(int x=-margin;x<=margin;x++) for(int y=-margin;y<=margin;y++) {
     var tile=map.GetTile(plan.X+x,plan.Y+y);
     if(tile!=null && (Walls.IsWall(tile) && (tile.zone_city==null || Construction.Owner(tile.zone_city)==kingdom) || Construction.IsSite(tile.building) && tile.building.getCity()==city)) evidence.Add(tile);
     if(++work%64==0) yield return null;
    }
    if(evidence.Count>0) {
     var votes=new Dictionary<KeyValuePair<int,int>,int>();
     foreach(var tile in evidence) {
      foreach(var offset in (town?TownLayout.Points(0,0):elven?ElfLayout.Points(0,0):dwarven?DwarfLayout.Points(0,0):plan.HumanMigration?FixedLayout.OldPoints(0,0):FixedLayout.Points(0,0))) {
       var center=new KeyValuePair<int,int>(tile.x-offset.Key,tile.y-offset.Value);
       if(Math.Abs(center.Key-plan.X)>margin || Math.Abs(center.Value-plan.Y)>margin) continue;
       int count; votes.TryGetValue(center,out count); votes[center]=count+1;
      }
      if(++work%24==0) yield return null;
     }
     var best=votes.OrderByDescending(v=>v.Value).ThenBy(v=>Math.Abs(v.Key.Key-plan.X)+Math.Abs(v.Key.Value-plan.Y)).First();
     plan.X=best.Key.Key; plan.Y=best.Key.Value; plan.SaveCenter();
     if(best.Value<evidence.Count) Debug.Log("Kingdom Stone Walls found overlapping legacy layouts; resuming the strongest match. Shift+F8 clears old layouts if desired.");
    }
    city.data.set(Fortification.CheckedKey,true);
   }
   if(town && !checkedLegacy) city.data.set(Fortification.CheckedKey,true);
   int cx=plan.X,cy=plan.Y;
   string savedMask=null; city.data.get(Fortification.MaskKey,out savedMask,null);
   var positions=(town?TownLayout.Points(cx,cy):elven?ElfLayout.Points(cx,cy):dwarven?DwarfLayout.Points(cx,cy):FixedLayout.Points(cx,cy)).ToList();
   bool resumed=savedMask!=null && savedMask.Length==positions.Count;
   bool vegetationPlan=false; city.data.get(Fortification.VegetationKey,out vegetationPlan,false);
   // Older towns can have gaps from houses built before their wall plan existed.
   // Keep every saved segment, but retry omitted town tiles when loading a save.
   bool repair=resumed && (town || !vegetationPlan);
   var mask=resumed?savedMask.ToCharArray():new char[positions.Count];
   var candidates=new HashSet<WorldTile>();
   for(int i=0;i<positions.Count;i++) {
    var pos=positions[i]; var tile=map.GetTile(pos.Key,pos.Value);
    bool wanted=resumed && mask[i]=='1' || (!resumed || repair) && Construction.Safe(tile,city,false,true);
    if(!resumed || repair) mask[i]=wanted?'1':'0';
    if(wanted && tile!=null) candidates.Add(tile);
    if(++work%32==0) yield return null;
   }
   // Preserve saved segments. Add only newly safe town tiles after the connectivity check.
   if(!resumed || repair) {
    var seed=map.GetTile(cx,cy); var before=new HashSet<WorldTile>(); var after=new HashSet<WorldTile>();
    foreach(var blocked in new[]{new HashSet<WorldTile>(),candidates}) {
     var seen=blocked==candidates?after:before; var queue=new Queue<WorldTile>();
     if(Walls.Land(seed) && !Walls.IsWall(seed) && !blocked.Contains(seed)) { seen.Add(seed); queue.Enqueue(seed); }
     while(queue.Count>0) {
      var tile=queue.Dequeue();
      foreach(var near in tile.neighbours) {
       if(near==null || Math.Abs(near.x-cx)>margin || Math.Abs(near.y-cy)>margin || !Walls.Land(near) || Walls.IsWall(near) || blocked.Contains(near) || !seen.Add(near)) continue;
       queue.Enqueue(near);
      }
      if(++work%64==0) yield return null;
     }
    }
    if(before.Any(t=>!candidates.Contains(t)&&!after.Contains(t))) {
     if(!resumed) { plan.Ready=true; Debug.Log("Kingdom Stone Walls skipped an unsafe fixed layout."); yield break; }
     // Keep the original saved layout if expanding its mask would isolate land.
     mask=savedMask.ToCharArray(); candidates.Clear();
     for(int i=0;i<positions.Count;i++) if(mask[i]=='1') {
      var tile=map.GetTile(positions[i].Key,positions[i].Value); if(tile!=null) candidates.Add(tile);
     }
    }
   }
   if(!Construction.IsFortifiedCity(city) || Construction.Owner(city)!=kingdom) yield break;
   for(int ring=0;ring<plan.Rings.Length;ring++) foreach(var pos in town?TownLayout.Points(cx,cy):elven?ElfLayout.Points(cx,cy):dwarven?DwarfLayout.Points(cx,cy):ring==0?FixedLayout.Gatehouses(cx,cy):FixedLayout.Ring(cx,cy,FixedLayout.Outer)) {
    var tile=map.GetTile(pos.Key,pos.Value); if(tile==null || !candidates.Contains(tile)) continue;
    plan.Rings[ring].Add(tile); Construction.Plans[tile]=city;
    if(Construction.IsSite(tile.building)&&!tile.building.isUnderConstruction()) Construction.Enqueue(tile.building);
   }
   // Sort only the work lists, after decoding the unchanged saved-mask order.
   if(town) ((List<WorldTile>)plan.Rings[0]).Sort((a,b)=>FixedLayout.WorkOrder(cx,cy,TownLayout.Radius,a.x,a.y).CompareTo(FixedLayout.WorkOrder(cx,cy,TownLayout.Radius,b.x,b.y)));
   else if(!elven && !dwarven) ((List<WorldTile>)plan.Rings[1]).Sort((a,b)=>FixedLayout.WorkOrder(cx,cy,FixedLayout.Outer,a.x,a.y).CompareTo(FixedLayout.WorkOrder(cx,cy,FixedLayout.Outer,b.x,b.y)));
   city.data.set(Fortification.MaskKey,new string(mask)); city.data.set(Fortification.VegetationKey,true); if(!town && !elven && !dwarven) city.data.set(FixedLayout.LayoutKey,true); plan.Ready=true;
   Debug.Log(town?"Kingdom Stone Walls planned "+plan.Rings[0].Count+" wooden town segments for city "+city.data.id:dwarven?"Kingdom Stone Walls planned "+plan.Rings[0].Count+" square dwarven segments, one big gate and four corner cannon towers for city "+city.data.id:elven?"Kingdom Stone Walls planned "+plan.Rings[0].Count+" circular elven segments for city "+city.data.id:"Kingdom Stone Walls planned "+plan.Rings[0].Count+" gatehouse and "+plan.Rings[1].Count+" perimeter segments for city "+city.data.id+"; four double-gated gatehouses, gatehouse walls first.");
  }
 }
}







using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
 public static class Gates {
  public const string HorizontalId="ksw_gate_horizontal", VerticalId="ksw_gate_vertical", DestroyedKey="ksw_destroyed_gates";
  public const string WoodHorizontalId="ksw_capital_wood_gate_horizontal",WoodVerticalId="ksw_capital_wood_gate_vertical",WoodDwarfId="ksw_capital_wood_gate_dwarf",UpgradeKey="ksw_gate_stone_upgrade",UpgradeTargetKey="ksw_gate_stone_target";
  public const int Health=10000, IronCost=2;
  const string HealthDoubledKey="ksw_gate_health_doubled_v1";
  static readonly FieldInfo LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
  static readonly FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"), Data=AccessTools.Field(typeof(Building),"data"), KingdomField=AccessTools.Field(typeof(BaseSimObject),"kingdom");
  static readonly FieldInfo AttackTarget=AccessTools.Field(typeof(Actor),"attack_target"), HasAttackTarget=AccessTools.Field(typeof(Actor),"has_attack_target");
  static readonly MethodInfo Add=AccessTools.Method(typeof(BuildingManager),"addBuilding",new Type[]{typeof(BuildingAsset),typeof(WorldTile),typeof(bool),typeof(bool),typeof(BuildPlacingType)}), SetKingdom=AccessTools.Method(typeof(Building),"setKingdomCiv"), Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
  static readonly Dictionary<string,BuildingSprites> Graphics=new Dictionary<string,BuildingSprites>();
  static readonly MethodInfo CanAttack=AccessTools.Method(typeof(BaseSimObject),"canAttackTarget");
  static readonly Dictionary<string,Sprite> Sprites=new Dictionary<string,Sprite>();
  // Sprite hooks may run from the parallel renderer while the simulation adds
  // or removes a gate. Keep membership reads safe across those threads.
  static readonly ConcurrentDictionary<Building,byte> Live=new ConcurrentDictionary<Building,byte>();
  // Target searches run for many actors each tick. Index gates by their center
  // tile so each actor considers only gates within attack-search distance.
  const int TargetChunkShift=4, TargetSearchRadius=18;
  static readonly ConcurrentDictionary<long,ConcurrentDictionary<Building,byte>> TargetChunks=new ConcurrentDictionary<long,ConcurrentDictionary<Building,byte>>();
  static readonly ConcurrentDictionary<Building,long> TargetKeys=new ConcurrentDictionary<Building,long>();
  // Admit at most ten units per gate before they request a route. A short grace
  // period covers findEnemyObjectTarget returning a gate before the AI stores it.
  public const int MaxGateAttackers=10;
  const uint SiegeReservationGrace=1000;
  sealed class SiegeSlot { public Actor Actor; public int Tick; }
  sealed class SiegeRoster { public readonly List<SiegeSlot> Slots=new List<SiegeSlot>(MaxGateAttackers); public int NextSweep; }
  static readonly object SiegeLock=new object();
  static readonly Dictionary<Building,SiegeRoster> SiegeSlots=new Dictionary<Building,SiegeRoster>();
  sealed class BlockedTargetPause { public WorldTile Tile; public int Until; }
  static System.Runtime.CompilerServices.ConditionalWeakTable<Actor,BlockedTargetPause> blockedTargetPauses=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,BlockedTargetPause>();
  static void PauseBlockedTarget(Actor actor) {
   var pause=blockedTargetPauses.GetOrCreateValue(actor);
   pause.Tile=actor.current_tile; pause.Until=unchecked(Environment.TickCount+1000);
  }
  public static bool BeforeFindTarget(BaseSimObject __instance,ref BaseSimObject __result) {
   var actor=__instance as Actor;
   if(actor==null) return true;
   BlockedTargetPause pause;
   if(!blockedTargetPauses.TryGetValue(actor,out pause) || pause.Tile!=actor.current_tile ||
      unchecked(Environment.TickCount-pause.Until)>=0) return true;
   __result=null; return false;
  }
  static void PruneSiegeSlots(Building gate,SiegeRoster roster,int now) {
   for(int i=roster.Slots.Count-1;i>=0;i--) {
    var slot=roster.Slots[i];
    if(slot.Actor==null || !slot.Actor.isAlive() ||
       AttackTarget.GetValue(slot.Actor)!=gate && unchecked((uint)(now-slot.Tick))>=SiegeReservationGrace) roster.Slots.RemoveAt(i);
   }
   roster.NextSweep=unchecked(now+500);
   if(roster.Slots.Count==0) SiegeSlots.Remove(gate);
  }
  static bool CanJoinSiege(Actor actor,Building gate) {
   if(actor==null || gate==null) return false;
   lock(SiegeLock) {
    SiegeRoster roster;
    if(!SiegeSlots.TryGetValue(gate,out roster)) return true;
    foreach(var slot in roster.Slots) if(slot.Actor==actor) return true;
    if(roster.Slots.Count<MaxGateAttackers) return true;
    int now=Environment.TickCount;
    if(unchecked(now-roster.NextSweep)>=0) PruneSiegeSlots(gate,roster,now);
    return roster.Slots.Count<MaxGateAttackers;
   }
  }
  public static bool JoinSiege(Actor actor,Building gate) {
   if(actor==null || gate==null) return false;
   lock(SiegeLock) {
    SiegeRoster roster;
    int now=Environment.TickCount;
    if(!SiegeSlots.TryGetValue(gate,out roster)) SiegeSlots[gate]=roster=new SiegeRoster { NextSweep=now };
    foreach(var slot in roster.Slots) if(slot.Actor==actor) {slot.Tick=now;return true;}
    if(roster.Slots.Count>=MaxGateAttackers && unchecked(now-roster.NextSweep)>=0) PruneSiegeSlots(gate,roster,now);
    if(roster.Slots.Count>=MaxGateAttackers) return false;
    SiegeSlots[gate]=roster;
    roster.Slots.Add(new SiegeSlot { Actor=actor, Tick=now });
    return true;
   }
  }
  static void ReleaseSiege(Actor actor,Building gate) {
   lock(SiegeLock) {
    SiegeRoster roster;
    if(!SiegeSlots.TryGetValue(gate,out roster)) return;
    roster.Slots.RemoveAll(slot=>slot.Actor==actor);
    if(roster.Slots.Count==0) SiegeSlots.Remove(gate);
   }
  }
  static void ReleaseGateSiege(Building gate) { lock(SiegeLock) SiegeSlots.Remove(gate); }
  public static bool RouteToSiege(Actor actor,Building gate) { return CanSiege(actor,gate) && JoinSiege(actor,gate); }
  static long TargetKey(int x,int y) { return ((long)(x>>TargetChunkShift)<<32)|(uint)(y>>TargetChunkShift); }
  static void IndexTarget(Building gate,bool present) {
   long old;
   if(TargetKeys.TryRemove(gate,out old)) {
    ConcurrentDictionary<Building,byte> bucket; byte marker;
    if(TargetChunks.TryGetValue(old,out bucket)) bucket.TryRemove(gate,out marker);
   }
   if(!present || gate.current_tile==null) return;
   long key=TargetKey(gate.current_tile.x,gate.current_tile.y);
   TargetChunks.GetOrAdd(key,_=>new ConcurrentDictionary<Building,byte>()).TryAdd(gate,0);
   TargetKeys[gate]=key;
  }
  public static IEnumerable<Building> All { get { foreach(var pair in Live) yield return pair.Key; } }
  public static bool Removing;
  public static BuildingAsset Horizontal, Vertical,WoodHorizontal,WoodVertical,WoodDwarf;
  public static bool IsWoodGate(Building b) {
   var asset=b==null?null:(BuildingAsset)Asset.GetValue(b);
   return asset!=null && (asset.id==WoodHorizontalId || asset.id==WoodVerticalId || asset.id==WoodDwarfId);
  }
  static bool Upgrading(Building b) {
   bool upgrading=false;var data=b==null?null:(BuildingData)Data.GetValue(b);
   if(data!=null) data.get(UpgradeKey,out upgrading,false);
   return upgrading;
  }
  public static bool IsGate(Building b) {
   var asset=b==null?null:(BuildingAsset)Asset.GetValue(b);
   return asset!=null && (asset.id==HorizontalId || asset.id==VerticalId || asset.id==WoodHorizontalId || asset.id==WoodVerticalId || asset.id==WoodDwarfId || ElfWalls.IsGateId(asset.id) || DwarfWalls.IsGateId(asset.id) || TownWalls.IsGateId(asset.id));
  }
  public static bool Intact(Building b) {
   if(!IsGate(b) || b.current_tile==null) return false;
   var data=Data.GetValue(b) as BuildingData;
   return data!=null && b.isAlive() && b.hasHealth() && (!b.isUnderConstruction() || Upgrading(b)) && data.state!=BuildingState.Ruins && !b.isOnRemove();
  }
  public static Kingdom Membership(BaseSimObject obj) { return obj==null?null:(Kingdom)KingdomField.GetValue(obj); }
  public static City CityOf(Building b) {
   var data=b==null?null:Data.GetValue(b) as BuildingData;
   return data==null || MapBox.instance==null?null:MapBox.instance.cities.get(data.cityID);
  }
  public static Kingdom Owner(Building gate) {
   var city=CityOf(gate);
   return city!=null && city.isAlive()?Construction.Owner(city):Membership(gate);
  }
  public static bool CanPass(Actor actor,Building gate) {
   var owner=Owner(gate);
   return owner!=null && actor!=null && Membership(actor)==owner;
  }
  public static bool Blocks(WorldTile tile,Actor actor) {
   var gate=tile==null?null:tile.building;
   // Every loaded/placed gate is registered in Live. Most path tiles contain
   // ordinary buildings or vegetation, so reject those before asset reflection.
   return gate!=null && Live.ContainsKey(gate) && Intact(gate) && !CanPass(actor,gate);
  }
  public static void Register() {
   Horizontal=MakeAsset(HorizontalId,false); Vertical=MakeAsset(VerticalId,true);
   WoodHorizontal=MakeAsset(WoodHorizontalId,false,false,false,false,true);
   WoodVertical=MakeAsset(WoodVerticalId,true,false,false,false,true);
   WoodDwarf=MakeAsset(WoodDwarfId,false,false,true,false,true);
   var h=new Harmony("custom.kingdom_stone_walls.gates");
   h.Patch(AccessTools.Method(typeof(BuildingAsset),"loadBuildingSprites"),prefix:new HarmonyMethod(typeof(Gates),"ProtectGraphics"));
   h.Patch(AccessTools.Method(typeof(BuildingAsset),"checkSpritesAreLoaded"),prefix:new HarmonyMethod(typeof(Gates),"ProtectGraphics"));
   h.Patch(AccessTools.Method(typeof(Building),"calculateMainSprite"),prefix:new HarmonyMethod(typeof(Gates),"GateSprite"));
   // Native recoloring recreates sprites at one pixel per world unit, discarding
   // our scale. Render gate sprites directly, choosing their current build state
   // every frame so a completed job cannot keep a cached construction image.
   h.Patch(AccessTools.Method(typeof(Building),"checkSpriteToRender"),prefix:new HarmonyMethod(typeof(Gates),"GateSprite"));
   // The parallel/cached renderer uses these methods instead of checkSpriteToRender.
   h.Patch(AccessTools.Method(typeof(Building),"calculateColoredSprite"),prefix:new HarmonyMethod(typeof(Gates),"GateSprite"));
   h.Patch(AccessTools.Method(typeof(Building),"getLastColoredSprite"),prefix:new HarmonyMethod(typeof(Gates),"GateSprite"));
   h.Patch(AccessTools.Method(typeof(Building),"isColoredSpriteNeedsCheck"),prefix:new HarmonyMethod(typeof(Gates),"GateColorCheck"));
   h.Patch(AccessTools.Method(typeof(Building),"get_city"),prefix:new HarmonyMethod(typeof(Gates),"GateCity"));
   h.Patch(AccessTools.Method(typeof(Building),"setBuilding"),postfix:new HarmonyMethod(typeof(Gates),"Loaded"));
   h.Patch(AccessTools.Method(typeof(Building),"kill"),prefix:new HarmonyMethod(typeof(Gates),"Destroyed"));
   h.Patch(AccessTools.Method(typeof(Building),"startRemove"),prefix:new HarmonyMethod(typeof(Gates),"Destroyed"));
   h.Patch(AccessTools.Method(typeof(Building),"getHit"),postfix:new HarmonyMethod(typeof(Gates),"AfterHit"));
   h.Patch(AccessTools.Method(typeof(BaseSimObject),"canAttackTarget"),prefix:new HarmonyMethod(typeof(Gates),"Attackable"));
   h.Patch(AccessTools.Method(typeof(BaseSimObject),"findEnemyObjectTarget"),prefix:new HarmonyMethod(typeof(Gates),"BeforeFindTarget"),postfix:new HarmonyMethod(typeof(Gates),"FindTarget"));
   h.Patch(AccessTools.Method(typeof(Actor),"isInAttackRange"),prefix:new HarmonyMethod(typeof(Gates),"AttackRange"));
   h.Patch(AccessTools.Method(typeof(Actor),"distanceToObjectTarget"),prefix:new HarmonyMethod(typeof(Gates),"TargetDistance"));
   h.Patch(AccessTools.Method(typeof(Actor),"shouldContinueToAttackTarget"),prefix:new HarmonyMethod(typeof(Gates),"KeepGateTarget"));
   h.Patch(AccessTools.Method(typeof(ai.behaviours.BehFightCheckEnemyIsOk),"execute"),prefix:new HarmonyMethod(typeof(Gates),"FightBehindGate"));
  }
  public static BuildingAsset MakeAsset(string id,bool vertical,bool elven=false,bool dwarven=false,bool town=false,bool capitalWood=false) {
   var asset=AssetManager.buildings.clone(id,Construction.SiteId);
   asset.atlas_asset=Construction.Site.atlas_asset;
   asset.base_stats=new BaseStats(); asset.base_stats["health"]=dwarven?(capitalWood?DwarfWalls.GateHealth/2:DwarfWalls.GateHealth):town||capitalWood?Health/2:Health;
   int span=dwarven?DwarfLayout.Gate:FixedLayout.Gate;
   asset.fundament=vertical?new BuildingFundament(0,0,span,span):new BuildingFundament(span,span,0,0);
   // WorldBox stores the iron construction resource under common_metals.
   asset.cost=town||capitalWood?new ConstructionCost {wood=IronCost}:new ConstructionCost {common_metals=dwarven?0:IronCost}; asset.construction_progress_needed=10;
   asset.type="type_ksw_gate"; asset.kingdom=null; asset.civ_kingdom=null;
   asset.ignored_by_cities=true; asset.can_be_abandoned=false; asset.can_be_upgraded=false;
   asset.can_be_demolished=true; asset.burnable=false; asset.damaged_by_rain=false;
   asset.has_ruin_state=false; asset.has_ruins_graphics=false; asset.has_sprites_ruin=false;
   asset.has_sprite_construction=true; asset.auto_remove_ruin=true; asset.remove_ruins=true;
   asset.random_flip=false; asset.scale_base=Vector3.one;
   asset.resources_given=new List<ResourceContainer>();
   var sprite=capitalWood?CapitalWoodGateArt.MakeSprite(vertical,dwarven,false):dwarven?DwarfWalls.MakeSprite("gate"):town?TownGateArt.MakeSprite(vertical,false):GateArt.MakeSprite(vertical,false,elven); Sprites[id]=sprite;
   var frames=new BuildingAnimationData {main=new[]{sprite},main_disabled=new[]{sprite},spawn=new[]{sprite},ruins=new[]{sprite},special=new[]{sprite}};
   var scaffold=capitalWood?CapitalWoodGateArt.MakeSprite(vertical,dwarven,true):dwarven?DwarfWalls.MakeSprite("gate",true):town?TownGateArt.MakeSprite(vertical,true):GateArt.MakeSprite(vertical,true,elven);
   var graphics=new BuildingSprites {construction=scaffold,map_icon=new BuildingMapIcon(sprite)};
   graphics.animation_data.Add(frames); Graphics[id]=graphics;
   ProtectGraphics(asset); PreloadHelpers.all_preloaded_sprites_buildings.Add(sprite); PreloadHelpers.all_preloaded_sprites_buildings.Add(scaffold);
   return asset;
  }
  public static bool ProtectGraphics(BuildingAsset __instance) {
   BuildingSprites graphics; if(!Graphics.TryGetValue(__instance.id,out graphics)) return true;
   __instance.building_sprites=graphics; __instance.sprites_are_initiated=true; return false;
  }
  public static bool GateSprite(Building __instance,ref Sprite __result) {
   if(!Live.ContainsKey(__instance)) return true;
   var asset=Asset.GetValue(__instance) as BuildingAsset;
   BuildingSprites graphics;
   Sprite sprite;
   // WorldBox reuses Building instances. A former gate can now be vegetation,
   // so membership alone cannot identify a gate during sprite preloading.
   if(asset==null || !Graphics.TryGetValue(asset.id,out graphics) || !Sprites.TryGetValue(asset.id,out sprite)) return true;
   __result=__instance.isUnderConstruction() && !Upgrading(__instance)?graphics.construction:sprite;
   LastSprite.SetValue(__instance,__result); return false;
  }
  public static bool GateCity(Building __instance,ref City __result) {
   if(!Live.ContainsKey(__instance) || !IsGate(__instance)) return true;
   __result=CityOf(__instance); return false;
  }
  public static void Loaded(Building __instance) {
   if(IsGate(__instance)) { Live.TryAdd(__instance,0); IndexTarget(__instance,true); WallMovement.TrackGate(__instance,true); Walls.Revision++; }
   else { byte marker; IndexTarget(__instance,false); ReleaseGateSiege(__instance); if(Live.TryRemove(__instance,out marker)) Walls.Revision++; }
  }
  public static void Reset() {
   Live.Clear(); TargetChunks.Clear(); TargetKeys.Clear(); lock(SiegeLock) SiegeSlots.Clear(); blockedTargetPauses=new System.Runtime.CompilerServices.ConditionalWeakTable<Actor,BlockedTargetPause>(); Removing=false;
   // Loading happens before the controller sees the new tile array. Re-index the
   // restored buildings and scale older saved health once, preserving damage percent.
   foreach(var b in MapBox.instance.buildings.getSimpleList()) if(IsGate(b)) {
    Live.TryAdd(b,0); IndexTarget(b,true);
    var data=(BuildingData)Data.GetValue(b);
    bool doubled=false;data.get(HealthDoubledKey,out doubled,false);
    if(doubled) continue;
    if(b.hasHealth() && (!b.isUnderConstruction() || Upgrading(b)))
     b.setHealth(Math.Min(HealthFor(b),data.health*2),false);
    data.set(HealthDoubledKey,true);
   }
  }
  public static bool GateColorCheck(Building __instance,ref bool __result) {
   if(!Live.ContainsKey(__instance) || !IsGate(__instance)) return true;
   __result=false; return false;
  }
  public static KeyValuePair<int,int> Position(Fortification plan,int slot) {
   if(plan.IsTown) return new KeyValuePair<int,int>(plan.X,plan.Y+(slot==0?TownLayout.Radius:-TownLayout.Radius));
   if(DwarfWalls.IsDwarfCity(plan.City))return new KeyValuePair<int,int>(plan.X,plan.Y-DwarfLayout.Radius);
   int r=ElfWalls.IsElfCity(plan.City)?ElfLayout.Radius:slot<4?FixedLayout.Inner:FixedLayout.Outer;
   switch(slot%4) {
    case 0: return new KeyValuePair<int,int>(plan.X,plan.Y+r);
    case 1: return new KeyValuePair<int,int>(plan.X,plan.Y-r);
    case 2: return new KeyValuePair<int,int>(plan.X+r,plan.Y);
    default: return new KeyValuePair<int,int>(plan.X-r,plan.Y);
   }
  }
  public static void Destroyed(Building __instance) {
   byte marker;
   if(!IsGate(__instance) || !Live.TryRemove(__instance,out marker)) return;
   ReleaseGateSiege(__instance);
   IndexTarget(__instance,false);
   WallMovement.TrackGate(__instance,false);
   Walls.Revision++;
   if(Removing) return;
   var city=CityOf(__instance); if(city==null) return;
   int slot=-1; ((BuildingData)Data.GetValue(__instance)).get("ksw_gate_slot",out slot,-1);
   if(slot<0 || slot>=8) return;
   int destroyed=0; city.data.get(DestroyedKey,out destroyed,0); city.data.set(DestroyedKey,destroyed | (1<<slot));
  }
  public static void AfterHit(Building __instance) {
   // Native getHit applies damage, animations and sounds. Remove the whole footprint
   // at zero HP, so rubble cannot continue blocking passage or heal after a reload.
   if(IsGate(__instance) && !__instance.hasHealth() && ((BuildingData)Data.GetValue(__instance)).state!=BuildingState.Removed) { Destroyed(__instance); Remove.Invoke(__instance,null); }
  }
  public static void RemoveGate(Building b) { if(IsGate(b)) Remove.Invoke(b,null); }
  static bool IsOrcCapitalGate(Building gate) {
   var city=CityOf(gate);
   return city!=null && Construction.IsCapital(city) && OrcWalls.IsOrcCity(city);
  }
  static void RemoveOrcCapitalGate(Building gate) {
   var city=CityOf(gate);
   bool refundUpgrade=Upgrading(gate) && !gate.isUnderConstruction();
   bool removing=Removing; Removing=true;
   try {
    if(gate.isUnderConstruction()) Construction.RemoveSite(gate,true);
    else {
     if(refundUpgrade && city!=null && city.isAlive()) city.addResourcesToRandomStockpile(ResourceFor(gate),CostFor(gate));
     RemoveGate(gate);
    }
   } finally { Removing=removing; }
  }
  public static void RefreshOwners() {
   foreach(var gate in Live.Keys.ToArray()) {
    if(!IsGate(gate)) { byte marker; IndexTarget(gate,false); if(Live.TryRemove(gate,out marker)) Walls.Revision++; continue; }
    if(!gate.isAlive() || !gate.hasHealth() || gate.isOnRemove()) { Destroyed(gate); continue; }
    if(IsOrcCapitalGate(gate)) { RemoveOrcCapitalGate(gate); continue; }
    if(Upgrading(gate) && !gate.isUnderConstruction()) Complete(gate);
    SyncOwner(gate);
   }
  }
  static void SyncOwner(Building gate) {
   var owner=Owner(gate);
   if(owner!=Membership(gate)) { SetKingdom.Invoke(gate,new object[]{owner}); Walls.Revision++; }
  }
  static bool CanSiege(Actor actor,Building gate) {
   if(actor==null || !actor.isAlive()) return false;
   var faction=Membership(actor);
   if(faction==null || !Intact(gate) || CanPass(actor,gate)) return false;
   if(actor.isKingdomCiv()) return true;
   // Skeletons and other hostile non-animal mobs can fight at a closed gate,
   // even when their native asset does not attack ordinary buildings.
   var owner=Owner(gate);
   return GateAssailant(actor) && owner!=null && faction.isEnemy(owner);
  }
  static bool GateAssailant(Actor actor) {
   var asset=actor==null?null:actor.asset;
   return asset!=null && (asset.can_attack_buildings || asset.unit_other || asset.unit_zombie);
  }
  static bool CanAttackGate(Actor actor,Building gate) { SyncOwner(gate); return (bool)CanAttack.Invoke(actor,new object[]{gate,true,true}); }
   public static int SlotCount(Fortification plan) {
    if(Construction.IsCapital(plan.City) && OrcWalls.IsOrcCity(plan.City)) return 0;
    // A conquered capital can retain its old one-ring plan while its current
    // city race expects the two-ring layout (or vice versa). Never schedule
    // gates against ring positions that the active plan does not contain.
    int expected=plan.IsTown||DwarfWalls.IsDwarfCity(plan.City)||ElfWalls.IsElfCity(plan.City)?1:2;
    if(plan.Rings==null || plan.Rings.Length!=expected || plan.Rings.Any(r=>r==null)) return 0;
    return plan.IsTown?2:DwarfWalls.IsDwarfCity(plan.City)?1:ElfWalls.IsElfCity(plan.City)?4:8;
   }
  public static BuildingAsset AssetFor(Fortification plan,int slot) {return plan.IsTown?TownWalls.HorizontalGate:DwarfWalls.IsDwarfCity(plan.City)?DwarfWalls.BigGate:ElfWalls.IsElfCity(plan.City)?(slot%4<2?ElfWalls.HorizontalGate:ElfWalls.VerticalGate):(slot%4<2?Horizontal:Vertical);}
  public static BuildingAsset WoodAssetFor(Fortification plan,int slot) {return plan.IsTown?TownWalls.HorizontalGate:DwarfWalls.IsDwarfCity(plan.City)?WoodDwarf:slot%4<2?WoodHorizontal:WoodVertical;}
  public static int HealthFor(Building gate) {string id=((BuildingAsset)Asset.GetValue(gate)).id;return id==WoodDwarfId?DwarfWalls.GateHealth/2:TownWalls.IsGateId(id)||id==WoodHorizontalId||id==WoodVerticalId?Health/2:DwarfWalls.IsGateId(id)?DwarfWalls.GateHealth:Health;}
  static string StoneResource(Fortification plan) {return DwarfWalls.IsDwarfCity(plan.City)?"gems":"common_metals";}
  static int StoneCost(Fortification plan) {return DwarfWalls.IsDwarfCity(plan.City)?DwarfWalls.GemCost:IronCost;}
  static bool StoneRingReady(Fortification plan,int slot) {return !plan.IsTown && !OrcWalls.IsOrcCity(plan.City) && plan.Rings[slot/4].Count>0 && plan.Rings[slot/4].All(CapitalStages.IsStone);}
  public static string ResourceFor(Building gate) {
   string paid=null;((BuildingData)Data.GetValue(gate)).get("ksw_gate_resource",out paid,null);
   string id=((BuildingAsset)Asset.GetValue(gate)).id;
   return paid??(TownWalls.IsGateId(id)||id==WoodHorizontalId||id==WoodVerticalId||id==WoodDwarfId?"wood":DwarfWalls.IsGateId(id)?"gems":"common_metals");
  }
  public static int CostFor(Building gate) {
   int paid=0;((BuildingData)Data.GetValue(gate)).get("ksw_gate_cost",out paid,0);
   return paid>0?paid:ResourceFor(gate)=="gems"?DwarfWalls.GemCost:IronCost;
  }
  public static bool CanAfford(Fortification plan,WorldTile tile) {
   bool upgrade=tile!=null && IsWoodGate(tile.building);
   if(upgrade && DwarfWalls.IsDwarfCity(plan.City)) return DebugConfig.isOn(DebugOption.CityInfiniteResources)||plan.City.getResourcesAmount("gems")>=DwarfWalls.GemCost;
   var cost=upgrade?new ConstructionCost{common_metals=IronCost}:new ConstructionCost{wood=IronCost};
   return (bool)AccessTools.Method(typeof(City),"hasEnoughResourcesFor").Invoke(plan.City,new object[]{cost});
  }
  public static void Pay(Building gate,City city) {
   var data=(BuildingData)Data.GetValue(gate);Fortification plan=null;
   bool upgrade=Upgrading(gate) && Construction.CityPlans.TryGetValue(city,out plan);
   string resource=upgrade?StoneResource(plan):"wood";
   int cost=upgrade?StoneCost(plan):IronCost;
   data.set("ksw_gate_resource",resource);data.set("ksw_gate_cost",cost);
   city.takeResource(resource,cost);
  }
  public static List<WorldTile> WorkTiles(Fortification plan) {
   var tiles=new List<WorldTile>();
   if(!plan.Ready || !Construction.IsFortifiedCity(plan.City)) return tiles;
   int destroyed=0; plan.City.data.get(DestroyedKey,out destroyed,0);
   for(int slot=0;slot<SlotCount(plan);slot++) {
    int ring=slot/4;
    if((destroyed & (1<<slot))!=0 && !Construction.AtPeace(plan.City) || plan.Rings[ring].Count==0 || plan.Rings[ring].Any(t=>!Walls.IsWall(t))) continue;
    var p=Position(plan,slot); var tile=MapBox.instance.GetTile(p.Key,p.Value); if(tile==null) continue;
    if(IsGate(tile.building)) {
     if(tile.building.isAlive() && CityOf(tile.building)==plan.City &&
        (tile.building.isUnderConstruction() && CanWork(tile.building,plan) ||
         IsWoodGate(tile.building) && StoneRingReady(plan,slot) && CanBuild(plan,slot,false))) tiles.Add(tile);
     continue;
    }
    if(CanBuild(plan,slot,true)) tiles.Add(tile);
   }
   return tiles;
  }
  // Supply can be ordered while citizens temporarily occupy an otherwise valid
  // gate footprint. Placement itself still waits for the footprint to clear.
  public static bool NeedsMaterials(Fortification plan) {
   if(!plan.Ready || !Construction.IsFortifiedCity(plan.City)) return false;
   int destroyed=0; plan.City.data.get(DestroyedKey,out destroyed,0);
   for(int slot=0;slot<SlotCount(plan);slot++) {
    int ring=slot/4;
    if((destroyed & (1<<slot))!=0 && !Construction.AtPeace(plan.City) ||
       plan.Rings[ring].Count==0 || plan.Rings[ring].Any(t=>!Walls.IsWall(t))) continue;
    var p=Position(plan,slot); var tile=MapBox.instance.GetTile(p.Key,p.Value);
     // CanBuild(false) intentionally permits an existing gate at its center for
     // CanWork. A different blocking building must not trigger a supply order.
     if(tile!=null && (IsWoodGate(tile.building) && StoneRingReady(plan,slot) && CanBuild(plan,slot,false) ||
         !IsGate(tile.building) && !Construction.BlocksWall(tile.building) && CanBuild(plan,slot,false))) return true;
   }
   return false;
  }
  public static string ResourceNeeded(Fortification plan) {
   if(!plan.Ready || !Construction.IsFortifiedCity(plan.City)) return null;
   int destroyed=0;plan.City.data.get(DestroyedKey,out destroyed,0);
   for(int slot=0;slot<SlotCount(plan);slot++) {
    int ring=slot/4;
    if((destroyed & (1<<slot))!=0 && !Construction.AtPeace(plan.City) ||
       plan.Rings[ring].Count==0 || plan.Rings[ring].Any(t=>!Walls.IsWall(t))) continue;
    var p=Position(plan,slot);var tile=MapBox.instance.GetTile(p.Key,p.Value);
    if(tile==null || !CanBuild(plan,slot,false)) continue;
    if(IsWoodGate(tile.building) && !tile.building.isUnderConstruction() && StoneRingReady(plan,slot)) return StoneResource(plan);
    if(!IsGate(tile.building) && !Construction.BlocksWall(tile.building)) return "wood";
   }
   return null;
  }
  public static bool CanBuild(Fortification plan,int slot,bool starting) {
    if(!Construction.IsFortifiedCity(plan.City)) return false;
    if(slot<0 || slot>=SlotCount(plan) || plan.Rings[slot/4].Count==0 || plan.Rings[slot/4].Any(t=>!Walls.IsWall(t))) return false;
    int destroyed=0; plan.City.data.get(DestroyedKey,out destroyed,0);
    if(starting && (destroyed & (1<<slot))!=0 && !Construction.AtPeace(plan.City)) return false;
    var p=Position(plan,slot); var tile=MapBox.instance.GetTile(p.Key,p.Value); if(tile==null) return false;
    var existing=tile.building;
    var asset=AssetFor(plan,slot); var f=asset.fundament;
    bool clear=true;
    for(int x=p.Key-f.left;x<=p.Key+f.right;x++) for(int y=p.Value-f.bottom;y<=p.Value+f.top;y++) {
     var cell=MapBox.instance.GetTile(x,y);
     if(cell==null || !Construction.OwnsTile(cell,plan.City) || !(Walls.Land(cell) || Walls.Road(cell)) || Walls.IsWall(cell) || starting && cell.hasUnits() || Construction.BlocksWall(cell.building) && (starting || cell.building!=existing)) clear=false;
    }
    // A partial original wall mask can leave an extra entrance; never erase it.
    // Both shoulders must exist before filling this designated opening.
    var left=MapBox.instance.GetTile(p.Key-(slot%4<2?f.left+1:0),p.Value-(slot%4<2?0:f.bottom+1));
    var right=MapBox.instance.GetTile(p.Key+(slot%4<2?f.right+1:0),p.Value+(slot%4<2?0:f.top+1));
    return clear && Walls.IsWall(left) && Walls.IsWall(right);
  }
  public static Building Start(Fortification plan,WorldTile tile) {
    int slot=-1;
    for(int i=0;i<SlotCount(plan);i++) {var p=Position(plan,i); if(tile.x==p.Key && tile.y==p.Value) {slot=i;break;}}
    int destroyed=0; plan.City.data.get(DestroyedKey,out destroyed,0);
    if(slot<0) return null;
    if(IsWoodGate(tile.building) && StoneRingReady(plan,slot)) return StartUpgrade(plan,tile.building,slot);
    if(!CanBuild(plan,slot,true)) return null;
    var asset=WoodAssetFor(plan,slot); var f=asset.fundament;
    var vegetation=new HashSet<Building>();
    for(int x=tile.x-f.left;x<=tile.x+f.right;x++) for(int y=tile.y-f.bottom;y<=tile.y+f.top;y++) {
     var plant=MapBox.instance.GetTile(x,y).building; if(Construction.IsVegetation(plant)) vegetation.Add(plant);
    }
    foreach(var plant in vegetation) Remove.Invoke(plant,null);
    var gate=(Building)Add.Invoke(MapBox.instance.buildings,new object[]{asset,tile,false,false,BuildPlacingType.New});
    if(gate==null) return null;
    var data=(BuildingData)Data.GetValue(gate); data.cityID=plan.City.data.id; data.set("ksw_gate_slot",slot); data.set(HealthDoubledKey,true);
    bool rebuilding=(destroyed & (1<<slot))!=0; data.set("ksw_gate_rebuild",rebuilding);
    if(rebuilding) plan.City.data.set(DestroyedKey,destroyed & ~(1<<slot));
    SetKingdom.Invoke(gate,new object[]{Construction.Owner(plan.City)});
    gate.setUnderConstruction();
    // setBuilding fires Loaded before a new gate becomes a construction site.
    // Remove that temporary finished-gate entry from the movement chunk index.
    WallMovement.TrackGate(gate,false);
   Live.TryAdd(gate,0); IndexTarget(gate,true); Walls.Revision++;
    return gate;
  }
  static Building StartUpgrade(Fortification plan,Building gate,int slot) {
   if(!CanBuild(plan,slot,false) || gate.isUnderConstruction() || CityOf(gate)!=plan.City) return null;
   var data=(BuildingData)Data.GetValue(gate);
   int health=data.health;
   data.set(UpgradeTargetKey,AssetFor(plan,slot).id);
   data.set(UpgradeKey,true);
   gate.setUnderConstruction();
   if(!gate.hasHealth()) gate.setHealth(Math.Max(1,health),false);
   // The old gate remains at the same foundation and keeps its collision while
   // citizens perform the paid upgrade job.
   WallMovement.TrackGate(gate,true);Walls.Revision++;
   return gate;
  }
  public static bool CanWork(Building gate,Fortification plan) {
   int slot=-1; var data=(BuildingData)Data.GetValue(gate); data.get("ksw_gate_slot",out slot,-1);
   bool rebuilding=false; data.get("ksw_gate_rebuild",out rebuilding,false);
   return (!rebuilding || Construction.AtPeace(plan.City)) && CityOf(gate)==plan.City && CanBuild(plan,slot,false);
  }
  public static void Complete(Building gate) {
   if(!IsGate(gate)) return;
   if(IsOrcCapitalGate(gate)) { RemoveOrcCapitalGate(gate); return; }
   if(gate.isUnderConstruction()) return;
   if(Upgrading(gate)) {
    var data=(BuildingData)Data.GetValue(gate);
    string target=null;data.get(UpgradeTargetKey,out target,null);
    var stone=string.IsNullOrEmpty(target)?null:AssetManager.buildings.get(target);
    if(stone==null) {Debug.LogError("Kingdom Stone Walls missing gate upgrade asset "+target);return;}
    Asset.SetValue(gate,stone);data.asset_id=stone.id;
    data.set(UpgradeKey,false);
   }
   gate.setHealth(HealthFor(gate),false); ((BuildingData)Data.GetValue(gate)).set(HealthDoubledKey,true); WallMovement.TrackGate(gate,true); Walls.Revision++;
  }
  public static bool Attackable(BaseSimObject __instance,BaseSimObject pTarget,ref bool pAttackBuildings,ref bool __result) {
   var gate=pTarget as Building; if(!IsGate(gate)) return true;
   var actor=__instance as Actor;
   if(!CanJoinSiege(actor,gate) || !CanSiege(actor,gate)) { __result=false; return false; }
   SyncOwner(gate);
   // Allow this building category, retaining native faction, combat capability,
   // water, island and passive-creature checks in the original method.
   pAttackBuildings=true; return true;
  }
  public static float Distance(Actor actor,Building gate) {
   var f=((BuildingAsset)Asset.GetValue(gate)).fundament;
   var p=actor.current_position; var t=gate.current_tile;
   float dx=Math.Max(0,Math.Max(t.x-f.left-.5f-p.x,p.x-(t.x+f.right+.5f)));
   float dy=Math.Max(0,Math.Max(t.y-f.bottom-.5f-p.y,p.y-(t.y+f.top+.5f)));
   return (float)Math.Sqrt(dx*dx+dy*dy);
  }
  public static bool AttackRange(Actor __instance,BaseSimObject pObject,ref bool __result) {
   var gate=pObject as Building; if(!IsGate(gate)) return true;
   __result=Intact(gate) && Distance(__instance,gate)<=__instance.getAttackRange(); return false;
  }
  public static bool TargetDistance(Actor __instance,BaseSimObject pBaseSimObject,ref float __result) {
   var gate=pBaseSimObject as Building; if(!IsGate(gate)) return true;
    if(gate.current_tile==null) { __result=float.MaxValue; return false; }
   __result=Distance(__instance,gate); return false;
  }
  public static bool KeepGateTarget(Actor __instance,ref bool __result) {
   var gate=__instance==null?null:AttackTarget.GetValue(__instance) as Building;
   if(gate==null || !IsGate(gate)) return true;
   __result=CanSiege(__instance,gate) && JoinSiege(__instance,gate) && CanAttackGate(__instance,gate);
   if(!__result) ReleaseSiege(__instance,gate);
   return false;
  }
  public static bool FightBehindGate(Actor pActor,ref ai.behaviours.BehResult __result) {
   var target=pActor==null?null:AttackTarget.GetValue(pActor) as BaseSimObject;
   if(target==null || IsGate(target as Building) || !(bool)HasAttackTarget.GetValue(pActor) ||
      pActor.current_tile==null || target.current_tile==null || MapBox.instance==null) return true;
   if(!WallMovement.LineBlocked(pActor,target.current_tile)) return true;
   BaseSimObject replacement=target;
   FindTarget(pActor,ref replacement);
   var gate=replacement as Building;
   if(gate!=null && IsGate(gate) && Retarget(pActor,gate)) { __result=ai.behaviours.BehResult.Stop; return false; }
   // A blocked unit is not a reachable combat destination. Clear it before
   // BehGoToActorTarget can request another full path through the wall.
   PauseBlockedTarget(pActor); pActor.clearAttackTarget(); pActor.cancelAllBeh();
   __result=ai.behaviours.BehResult.Stop;
   return false;
  }
  public static void FindTarget(BaseSimObject __instance,ref BaseSimObject __result) {
   var actor=__instance as Actor; if(actor==null || !actor.isAlive() || actor.current_tile==null || Membership(actor)==null ||
      !actor.isKingdomCiv() && !GateAssailant(actor)) return;
   BlockedTargetPause pause;
   if(blockedTargetPauses.TryGetValue(actor,out pause) && pause.Tile==actor.current_tile &&
      unchecked(Environment.TickCount-pause.Until)<0) { __result=null; return; }
   var nativeGate=__result as Building;
   if(IsGate(nativeGate) && !CanJoinSiege(actor,nativeGate)) __result=null;
   bool blocked=__result!=null && __result.current_tile!=null && WallMovement.LineBlocked(actor,__result.current_tile);
   if(__result!=null && !blocked) return;
   Building best=null; float distance=float.MaxValue; bool fullGateNearby=false;
   int x0=(actor.current_tile.x-TargetSearchRadius)>>TargetChunkShift,x1=(actor.current_tile.x+TargetSearchRadius)>>TargetChunkShift;
   int y0=(actor.current_tile.y-TargetSearchRadius)>>TargetChunkShift,y1=(actor.current_tile.y+TargetSearchRadius)>>TargetChunkShift;
   for(int cx=x0;cx<=x1;cx++) for(int cy=y0;cy<=y1;cy++) {
    ConcurrentDictionary<Building,byte> bucket;
    if(!TargetChunks.TryGetValue(((long)cx<<32)|(uint)cy,out bucket)) continue;
    foreach(var pair in bucket) {
     var gate=pair.Key;
    if(!Live.ContainsKey(gate) || !Intact(gate)) continue;
    float d=Distance(actor,gate); if(d>8 || !CanSiege(actor,gate)) continue;
    if(!CanJoinSiege(actor,gate)) { fullGateNearby=true; continue; }
    if(d>=distance) continue;
    if(!CanAttackGate(actor,gate)) continue;
    best=gate; distance=d;
    }
   }
   if(best!=null && JoinSiege(actor,best)) __result=best;
   else if(IsGate(__result as Building) && !JoinSiege(actor,(Building)__result)) __result=null;
   if(best==null && (blocked || fullGateNearby && __result==null)) { __result=null; PauseBlockedTarget(actor); }
  }
  public static WorldTile Approach(Actor actor,Building gate) {
   var f=((BuildingAsset)Asset.GetValue(gate)).fundament; var t=gate.current_tile;
   WorldTile best=null; float distance=float.MaxValue;
   for(int x=t.x-f.left-1;x<=t.x+f.right+1;x++) for(int y=t.y-f.bottom-1;y<=t.y+f.top+1;y++) {
    if(x>=t.x-f.left && x<=t.x+f.right && y>=t.y-f.bottom && y<=t.y+f.top) continue;
    var tile=MapBox.instance.GetTile(x,y);
    if(tile==null || !(Walls.Land(tile) || Walls.Road(tile)) || WallMovement.Blocks(tile,actor)) continue;
    float d=(actor.current_position-new Vector2(x,y)).sqrMagnitude;
    if(d<distance) { best=tile; distance=d; }
   }
   return best;
  }
  public static bool Retarget(Actor actor,Building gate) {
   if(!CanJoinSiege(actor,gate) || !CanSiege(actor,gate) || !CanAttackGate(actor,gate) || !JoinSiege(actor,gate)) return false;
   actor.cancelAllBeh(); actor.setAttackTarget(gate); return true;
  }
 }
 public static class GateArt {
  public static byte[] Pixels(bool vertical,bool construction=false) {
   int width=vertical?24:36,height=vertical?36:24; var pixels=new byte[width*height*4];
   for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
    // Plan-view side gates use the same iron-bound timber and masonry palette.
    int across=vertical?y:x, up=vertical?x:y;
    if(up<3 || up>21 || up>18 && across>=5 && across<31) continue;
    int r=0,g=0,b=0;
    if(across<5 || across>=31) {
     bool mortar=up%4==0 || (across+up/4*2)%4==0;
     r=mortar?62:145+(across*3+up*7)%17; g=r*98/100; b=r*94/100;
     if(up==21) {r=195;g=191;b=182;}
    } else {
     if(construction && up!=6 && up!=15 && across!=8 && across!=27) continue;
     bool seam=across%3==0 || across==17 || across==18;
     r=seam?63:126+(across*3+up)%17; g=seam?42:79+(across+up)%9; b=seam?29:43;
     if(up==6 || up==15) {r=57;g=64;b=68;}
     if((up==6 || up==15) && across%4==2) {r=168;g=171;b=162;}
     if(up==10 && (across==16 || across==19)) {r=211;g=165;b=67;}
    }
    int i=(y*width+x)*4; pixels[i]=(byte)r; pixels[i+1]=(byte)g; pixels[i+2]=(byte)b; pixels[i+3]=255;
   }
   return pixels;
  }
  public static Sprite MakeSprite(bool vertical,bool construction=false,bool elven=false) {
   int width=vertical?24:36,height=vertical?36:24;
   var texture=new Texture2D(width,height,TextureFormat.RGBA32,false) {filterMode=FilterMode.Point};
   texture.LoadRawTextureData(elven?ElfArt.GatePixels(vertical,construction):Pixels(vertical,construction)); texture.Apply();
   // 36 pixels / 4 = the nine-tile opening. Native recoloring must be bypassed
   // above: changing pixels-per-unit alone does not survive its atlas rebuild.
   var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),4);
   sprite.name=(vertical?Gates.VerticalId:Gates.HorizontalId)+(construction?"_construction":""); return sprite;
  }
 }
 public static class CapitalWoodGateArt {
  public static Sprite MakeSprite(bool vertical,bool dwarven,bool construction) {
   if(!dwarven) return TownGateArt.MakeSprite(vertical,construction);
   const int width=68,height=32;
   var pixels=new byte[width*height*4];
   for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
    bool post=x<8 || x>=60;
    bool beam=y==5 || y==6 || y==19 || y==20;
    bool timber=y>=3 && y<=25;
    if(!timber || construction && !post && !beam && x%9!=0) continue;
    int r,g,b;
    if(post) {
     int grain=(x*5+y*7)%15;
     r=125+grain;g=79+grain/2;b=42+grain/3;
     if(x%4==0) {r=65;g=42;b=24;}
     if(y>=23 && x%4!=0) {r=181;g=130;b=71;}
    } else {
     int grain=(x*3+y)%11;
     r=118+grain;g=75+grain/2;b=39+grain/3;
     if(x%5==0 || x==33 || x==34) {r=62;g=39;b=25;}
    }
    if(beam) {r=81;g=49;b=28;if(x%8==3) {r=169;g=143;b=88;}}
    int i=(y*width+x)*4;pixels[i]=(byte)r;pixels[i+1]=(byte)g;pixels[i+2]=(byte)b;pixels[i+3]=255;
   }
   var texture=new Texture2D(width,height,TextureFormat.RGBA32,false) {filterMode=FilterMode.Point};
   texture.LoadRawTextureData(pixels);texture.Apply();
   var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),4);
   sprite.name=Gates.WoodDwarfId+(construction?"_construction":"");return sprite;
  }
 }
}

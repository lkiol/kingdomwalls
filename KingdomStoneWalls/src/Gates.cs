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
   public const string WoodHorizontalId="ksw_capital_wood_gate_horizontal",WoodVerticalId="ksw_capital_wood_gate_vertical",WoodDwarfId="ksw_capital_wood_gate_dwarf",UpgradeKey="ksw_gate_stone_upgrade",UpgradeTargetKey="ksw_gate_upgrade_target";
   public const int Health=10000, IronCost=2;
   const string HealthDoubledKey="ksw_gate_health_doubled_v1";
   static readonly FieldInfo LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
   static readonly FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"), Data=AccessTools.Field(typeof(Building),"data"), KingdomField=AccessTools.Field(typeof(BaseSimObject),"kingdom");
   static readonly FieldInfo AttackTarget=AccessTools.Field(typeof(Actor),"attack_target"), HasAttackTarget=AccessTools.Field(typeof(Actor),"has_attack_target");
   static readonly MethodInfo Add=AccessTools.Method(typeof(BuildingManager),"addBuilding",new Type[]{typeof(BuildingAsset),typeof(WorldTile),typeof(bool),typeof(bool),typeof(BuildPlacingType)}), SetKingdom=AccessTools.Method(typeof(Building),"setKingdom"), Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
   static readonly Dictionary<string,BuildingSprites> Graphics=new Dictionary<string,BuildingSprites>();
   static readonly MethodInfo CanAttack=AccessTools.Method(typeof(BaseSimObject),"canAttackTarget");
   static readonly Dictionary<string,Sprite> Sprites=new Dictionary<string,Sprite>();
   static readonly ConcurrentDictionary<Building,byte> Live=new ConcurrentDictionary<Building,byte>();
   const int TargetChunkShift=4, TargetSearchRadius=18;
   static readonly ConcurrentDictionary<long,ConcurrentDictionary<Building,byte>> TargetChunks=new ConcurrentDictionary<long,ConcurrentDictionary<Building,byte>>();
   static readonly ConcurrentDictionary<Building,long> TargetKeys=new ConcurrentDictionary<Building,long>();
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
    return asset!=null && (asset.id==HorizontalId || asset.id==VerticalId || asset.id==WoodHorizontalId || asset.id==WoodVerticalId || asset.id==WoodDwarfId || ElfWalls.IsGateId(asset.id) || DwarfWalls.IsGateId(asset.id));
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
    h.Patch(AccessTools.Method(typeof(Building),"checkSpriteToRender"),prefix:new HarmonyMethod(typeof(Gates),"GateSprite"));
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
    asset.cost=town||capitalWood?new ConstructionCost {wood=IronCost}:new ConstructionCost {common_metals=dwarven?0:IronCost}; asset.construction_progress_needed=10;
    asset.type="type_ksw_gate"; asset.kingdom=null; asset.civ_kingdom=null;
    asset.ignored_by_cities=true; asset.can_be_abandoned=false; asset.can_be_upgraded=false;
    asset.can_be_demolished=true; asset.burnable=false; asset.damaged_by_rain=false;
    asset.has_ruin_state=false; asset.has_ruins_graphics=false; asset.has_sprites_ruin=false;
    asset.has_sprite_construction=true; asset.auto_remove_ruin=true; asset.remove_ruins=true;
    asset.random_flip=false; asset.scale_base=Vector3.one;
    asset.resources_given=new List<ResourceContainer>();
    var sprite=capitalWood?CapitalWoodGateArt.MakeSprite(vertical,dwarven,false):dwarven?DwarfWalls.MakeSprite("gate"):town?TownGateArt.MakeSprite(vertical,false):GateArt.MakeSprite(vertical,false,elven);
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
    if(asset==null || !Graphics.TryGetValue(asset.id,out graphics) || !Sprites.TryGetValue(asset.id,out sprite)) return true;
    __result=__instance.isUnderConstruction() && !Upgrading(__instance)?graphics.construction:sprite;
    LastSprite.SetValue(__instance,__result); return false;
   }
   public static bool GateCity(Building __instance,ref City __result) {
    if(!Live.ContainsKey(__instance) || !IsGate(__instance)) return true;
    __result=CityOf(__instance); return false;
   }
   public static void Loaded(Building __instance) {
    if(IsGate(__instance)) { Live.TryAdd(__instance,0); Walls.Revision++; }
    else if(Live.TryRemove(__instance,out _)) Walls.Revision++;
   }
   public static bool GateColorCheck(Building __instance,ref bool __result) {
    if(!Live.ContainsKey(__instance) || !IsGate(__instance)) return true;
    __result=false; return false;
   }
   public static void Destroyed(Building __instance) {
    byte marker;
    if(!IsGate(__instance) || !Live.TryRemove(__instance,out marker)) return;
    Walls.Revision++;
   }
   public static void AfterHit(Building __instance) {
    if(IsGate(__instance) && !__instance.hasHealth() && ((BuildingData)Data.GetValue(__instance)).state!=BuildingState.Removed) { Destroyed(__instance); Remove.Invoke(__instance,null); }
   }
   public static void RemoveGate(Building b) { if(IsGate(b)) Remove.Invoke(b,null); }
   public static bool Attackable(BaseSimObject __instance,BaseSimObject pTarget,ref bool pAttackBuildings,ref bool __result) {
    var gate=pTarget as Building; if(!IsGate(gate)) return true;
    var actor=__instance as Actor;
    if(actor==null || !actor.isAlive()) { __result=false; return false; }
    pAttackBuildings=true; return true;
   }
   public static bool AttackRange(Actor __instance,BaseSimObject pObject,ref bool __result) {
    var gate=pObject as Building; if(!IsGate(gate)) return true;
    __result=gate!=null && Intact(gate); return false;
   }
   public static bool TargetDistance(Actor __instance,BaseSimObject pBaseSimObject,ref float __result) {
    var gate=pBaseSimObject as Building; if(!IsGate(gate)) return true;
    __result=gate.current_tile==null?float.MaxValue:0f; return false;
   }
   public static bool KeepGateTarget(Actor __instance,ref bool __result) {
    var gate=__instance==null?null:AttackTarget.GetValue(__instance) as Building;
    if(gate==null || !IsGate(gate)) return true;
    __result=true; return false;
   }
   public static bool FightBehindGate(Actor pActor,ref ai.behaviours.BehResult __result) { return true; }
   public static void FindTarget(BaseSimObject __instance,ref BaseSimObject __result) { }
   public static bool Retarget(Actor actor,Building gate) { return false; }
  }
  public static class GateArt {
   public static byte[] Pixels(bool vertical,bool construction=false) {
    int width=vertical?24:36,height=vertical?36:24; var pixels=new byte[width*height*4];
    for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
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

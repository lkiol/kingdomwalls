using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
  public static class OrcWalls {
   public const string SiteId="ksw_orc_log_site", Prefix="ksw_orc_log_";
   public static BuildingAsset Site;
   static BuildingSprites graphics;
   static Sprite segment, scaffold;
   public static readonly System.Collections.Generic.Dictionary<string,TileType> Types=new System.Collections.Generic.Dictionary<string,TileType>();
   static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset");
   static readonly System.Reflection.FieldInfo LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
   static readonly System.Reflection.FieldInfo Buildings=AccessTools.Field(typeof(City),"buildings");
   static readonly System.Reflection.MethodInfo Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
   public static bool IsOrc(ActorAsset asset) { return asset!=null && (asset.id=="orc" || asset.base_asset_id=="orc"); }
   public static bool IsOrcCity(City city) { return city!=null && IsOrc(city.getActorAsset()); }
   public static bool IsOrcActor(Actor actor) { return actor!=null && IsOrc(actor.getActorAsset()); }
   public static bool IsSite(Building b) { return b!=null && ((BuildingAsset)Asset.GetValue(b))?.id==SiteId; }
   public static BuildingAsset SiteFor(City city) { Fortification plan; if(Construction.CityPlans.TryGetValue(city,out plan) && plan.IsTown) return TownWalls.Site; return IsOrcCity(city)?Site:ElfWalls.IsElfCity(city)?ElfWalls.Site:DwarfWalls.IsDwarfCity(city)?DwarfWalls.Site:Site; }
   public static ConstructionCost ReserveFor(City city) { Fortification plan; bool wood=!Construction.CityPlans.TryGetValue(city,out plan) || CapitalStages.MaterialFor(plan)=="wood"; return wood?new ConstructionCost{wood=Construction.WallCost}:new ConstructionCost{stone=Construction.WallCost}; }
   public static string ResourceFor(Building site) { return IsSite(site) || TownWalls.IsSite(site)?"wood":"stone"; }
   public static bool TryType(WorldTile tile,City city,out TileType wall) {
    var original=Walls.Original(tile);
    if(original==null) { wall=null; return false; }
    Fortification plan; bool town=Construction.CityPlans.TryGetValue(city,out plan) && plan.IsTown;
    return (town?TownWalls.Types:IsOrcCity(city)?Types:ElfWalls.IsElfCity(city)?ElfWalls.Types:DwarfWalls.IsDwarfCity(city)?DwarfWalls.Types:Walls.Types).TryGetValue(original.id,out wall);
   }
   public static Sprite MakeSprite(bool construction,int variant=0) {
    var texture=new Texture2D(16,16,TextureFormat.RGBA32,false); texture.filterMode=FilterMode.Point;
    texture.LoadRawTextureData(OrcWallArt.Pixels(construction,variant)); texture.Apply();
    var sprite=Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f),construction?4:1);
    sprite.name=construction?"ksw_orc_log_scaffold":"ksw_orc_log_segment"; return sprite;
   }
   public static void Register() {
    var sprites=new TileSprites();
    for(int i=0;i<4;i++) sprites.addVariation(MakeSprite(false,i),"log"+i);
    foreach(var pair in Walls.Types) {
     var wall=AssetManager.tiles.clone(Prefix+pair.Key,pair.Value.id);
     wall.sprites=sprites; wall.color=new Color32(119,77,39,255);
     Types[pair.Key]=wall; Walls.Track(wall);
    }
    Site=AssetManager.buildings.clone(SiteId,Construction.SiteId);
    Site.atlas_asset=Construction.Site.atlas_asset;
    Site.base_stats=(BaseStats)Construction.Site.base_stats.Clone();
    Site.cost=new ConstructionCost {wood=Construction.WallCost};
    segment=MakeSprite(false); scaffold=MakeSprite(true);
    var frames=new BuildingAnimationData {main=new[]{segment},main_disabled=new[]{segment},spawn=new[]{segment},ruins=new[]{segment},special=new[]{segment}};
    graphics=new BuildingSprites {construction=scaffold,map_icon=new BuildingMapIcon(segment)};
    graphics.animation_data.Add(frames); ProtectGraphics(Site);
    PreloadHelpers.all_preloaded_sprites_buildings.Add(segment);
    PreloadHelpers.all_preloaded_sprites_buildings.Add(scaffold);
    var h=new Harmony("custom.kingdom_stone_walls.orcs");
    h.Patch(AccessTools.Method(typeof(BuildingAsset),"loadBuildingSprites"),prefix:new HarmonyMethod(typeof(OrcWalls),"ProtectGraphics"));
    h.Patch(AccessTools.Method(typeof(BuildingAsset),"checkSpritesAreLoaded"),prefix:new HarmonyMethod(typeof(OrcWalls),"ProtectGraphics"));
    foreach(var name in new[]{"calculateMainSprite","checkSpriteToRender","calculateColoredSprite","getLastColoredSprite"})
     h.Patch(AccessTools.Method(typeof(Building),name),prefix:new HarmonyMethod(typeof(OrcWalls),"SiteSprite"));
    h.Patch(AccessTools.Method(typeof(Building),"isColoredSpriteNeedsCheck"),prefix:new HarmonyMethod(typeof(OrcWalls),"ColorCheck"));
   }
   public static bool ProtectGraphics(BuildingAsset __instance) {
    if(__instance.id!=SiteId) return true;
    __instance.building_sprites=graphics; __instance.sprites_are_initiated=true; return false;
   }
   public static bool SiteSprite(Building __instance,ref Sprite __result) {
    if(!IsSite(__instance)) return true;
    __result=__instance.isUnderConstruction()?scaffold:segment;
    LastSprite.SetValue(__instance,__result); return false;
   }
   public static bool ColorCheck(Building __instance,ref bool __result) {
    if(!IsSite(__instance)) return true;
    __result=false; return false;
   }
   public static bool Refresh(Fortification plan) {
    if(plan.IsTown || !plan.Ready || !Construction.IsCapital(plan.City) || !IsOrcCity(plan.City)) return false;
    foreach(var ring in plan.Rings) foreach(var tile in ring) {
     if(!Construction.OwnsTile(tile,plan.City)) continue;
     if(Construction.IsSite(tile.building) && !IsSite(tile.building)) {
      Construction.RemoveSite(tile.building,true); return true;
     }
     if(CapitalStages.IsStone(tile)) {
      var original=Walls.Original(tile); TileType logs;
      if(original!=null && Types.TryGetValue(original.id,out logs)) {
       Walls.Upgrade(tile,logs); return true;
      }
     }
    }
    foreach(var building in ((System.Collections.Generic.List<Building>)Buildings.GetValue(plan.City)).ToArray()) {
     var asset=(BuildingAsset)Asset.GetValue(building);
     if(asset!=null && asset.tower && building.isAlive() && building.getCity()==plan.City) {
      Remove.Invoke(building,null); return true;
     }
    }
    return false;
   }
  }
  public static class OrcWallArt {
   public static byte[] Pixels(bool construction,int variant) {
    var pixels=new byte[16*16*4];
    for(int y=0;y<16;y++) for(int x=0;x<16;x++) {
     int log=x/4, local=x%4;
     int top=(construction?7:13)+(log+variant)%2;
     if(y>top || y==top && local!=1 && local!=2 || y==top-1 && local==3) continue;
     int grain=(x*13+y*7+variant*17)%13;
     int r=121+grain,g=77+grain/2,b=38+grain/3;
     if(local==0) {r=55;g=34;b=21;}
     if(local==1) {r+=34;g+=23;b+=12;}
     if(y>=top-1 && local>0) {r=191;g=140;b=78;}
     if((y==4+log%3 || y==5+log%3) && local==2) {r=68;g=40;b=23;}
     if(y==2 || y==3 || !construction && (y==8 || y==9)) {
      r=y==3 || y==9?155:83; g=y==3 || y==9?101:51; b=y==3 || y==9?52:28;
      if(local==1) {r=65;g=43;b=25;}
     }
     if(y==0) {r=48;g=31;b=20;}
     int i=(y*16+x)*4; pixels[i]=(byte)r; pixels[i+1]=(byte)g; pixels[i+2]=(byte)b; pixels[i+3]=255;
    }
    return pixels;
   }
  }
 }

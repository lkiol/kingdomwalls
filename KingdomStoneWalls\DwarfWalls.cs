using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
 public static class DwarfLayout {
  public const int Radius=32, Gate=8, Margin=40;
  public static IEnumerable<KeyValuePair<int,int>> Points(int cx,int cy) {
   for(int x=-Radius;x<=Radius;x++) {
    yield return new KeyValuePair<int,int>(cx+x,cy+Radius);
    if(Math.Abs(x)>Gate) yield return new KeyValuePair<int,int>(cx+x,cy-Radius);
   }
   for(int y=-Radius+1;y<Radius;y++) {
    yield return new KeyValuePair<int,int>(cx-Radius,cy+y);
    yield return new KeyValuePair<int,int>(cx+Radius,cy+y);
   }
  }
  public static bool IntersectsReserved(int cx,int cy,int x0,int y0,int x1,int y1) {
   // The southern gate joins the two bottom wall lanes into a continuous border.
   // Reject footprints outside that border without enumerating every wall tile.
   int outer=Radius+FixedLayout.Clearance,inner=Radius-FixedLayout.Clearance;
   return x0<=cx+outer && x1>=cx-outer && y0<=cy+outer && y1>=cy-outer &&
    (x0<=cx-inner || x1>=cx+inner || y0<=cy-inner || y1>=cy+inner);
  }
  public static IEnumerable<KeyValuePair<int,int>> TowerPoints(int cx,int cy,BuildingFundament f) {
   // Hug all four inner corners while keeping the full foundation outside the work lane.
   foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1})
    yield return new KeyValuePair<int,int>(cx+sx*(Radius-FixedLayout.Clearance-1-(sx<0?f.left:f.right)),cy+sy*(Radius-FixedLayout.Clearance-1-(sy<0?f.bottom:f.top)));
  }
 }
 public static class DwarfWalls {
  public const string Prefix="ksw_dwarf_",SiteId="ksw_dwarf_site",TowerId="ksw_dwarf_cannon_tower",GateId="ksw_dwarf_big_gate",LayoutKey="ksw_dwarf_square_v1";
  public const int GateHealth=20000, GemCost=5;
  public static BuildingAsset Site,Tower,BigGate;
  public static readonly Dictionary<string,TileType> Types=new Dictionary<string,TileType>();
  static readonly Dictionary<string,BuildingSprites> graphics=new Dictionary<string,BuildingSprites>();
  static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
  static Sprite empty;
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"),Data=AccessTools.Field(typeof(Building),"data"),Buildings=AccessTools.Field(typeof(City),"buildings"),LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
  static readonly System.Reflection.MethodInfo Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
  public static bool IsDwarfCity(City city) {var a=city?.getActorAsset();return a!=null&&(a.id=="dwarf"||a.base_asset_id=="dwarf");}
  public static bool IsSite(Building b) {return b!=null&&((BuildingAsset)Asset.GetValue(b))?.id==SiteId;}
  public static bool IsGateId(string id) {return id==GateId;}
  public static Sprite MakeSprite(string kind,bool construction=false,int variant=0) {
   int w=kind=="tower"?32:kind=="gate"?68:16,h=kind=="tower"?36:kind=="gate"?32:16;
   var texture=new Texture2D(w,h,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
   texture.LoadRawTextureData(kind=="tower"?DwarfArt.TowerPixels(construction):kind=="gate"?DwarfArt.GatePixels(construction):DwarfArt.WallPixels(construction,variant));texture.Apply();
   var sprite=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),kind=="gate"||construction?4:1);
   sprite.name=Prefix+kind+(construction?"_scaffold":"");return sprite;
  }
  static void Graphics(BuildingAsset asset,string kind) {
   var sprite=MakeSprite(kind);var scaffold=MakeSprite(kind,true);
   var frames=new BuildingAnimationData{main=new[]{sprite},main_disabled=new[]{sprite},spawn=new[]{sprite},ruins=new[]{sprite},special=new[]{sprite}};
   var data=new BuildingSprites{construction=scaffold,map_icon=new BuildingMapIcon(sprite)};data.animation_data.Add(frames);
   sprites[asset.id]=sprite;graphics[asset.id]=data;asset.has_kingdom_color=false;asset.random_flip=false;ProtectGraphics(asset);
   PreloadHelpers.all_preloaded_sprites_buildings.Add(sprite);PreloadHelpers.all_preloaded_sprites_buildings.Add(scaffold);
  }
  public static void Register() {
   var transparent=new Texture2D(1,1,TextureFormat.RGBA32,false);transparent.SetPixel(0,0,Color.clear);transparent.Apply();
   empty=Sprite.Create(transparent,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);
   var terrain=new TileSprites();for(int i=0;i<4;i++)terrain.addVariation(MakeSprite("wall",false,i),"dwarf"+i);
   foreach(var pair in Walls.Types) {var wall=AssetManager.tiles.clone(Prefix+pair.Key,pair.Value.id);wall.sprites=terrain;wall.color=new Color32(108,115,128,255);Types[pair.Key]=wall;Walls.Track(wall);}
   Site=AssetManager.buildings.clone(SiteId,Construction.SiteId);Site.atlas_asset=Construction.Site.atlas_asset;Site.base_stats=(BaseStats)Construction.Site.base_stats.Clone();Site.cost=new ConstructionCost{stone=Construction.WallCost};Graphics(Site,"wall");
   var native=AssetManager.buildings.list.FirstOrDefault(a=>a.tower&&a.city_building&&a.civ_kingdom=="dwarf");
   if(native==null)throw new InvalidOperationException("Native dwarven tower asset is unavailable");
   if(AssetManager.projectiles.get("cannonball")==null)throw new InvalidOperationException("Native boat cannonball projectile is unavailable");
   Tower=AssetManager.buildings.clone(TowerId,native.id);Tower.atlas_asset=native.atlas_asset;Tower.base_stats=(BaseStats)native.base_stats.Clone();
   Tower.ignored_by_cities=true;Tower.can_be_upgraded=false;Tower.upgrade_to=null;
   Tower.has_ruin_state=false;Tower.has_ruins_graphics=false;Tower.has_sprites_ruin=false;Tower.auto_remove_ruin=true;Tower.remove_ruins=true;
   ConfigureCannon(Tower);Graphics(Tower,"tower");
   BigGate=Gates.MakeAsset(GateId,false,false,true);
   var harmony=new Harmony("custom.kingdom_stone_walls.dwarves");
   foreach(var name in new[]{"loadBuildingSprites","checkSpritesAreLoaded"})harmony.Patch(AccessTools.Method(typeof(BuildingAsset),name),prefix:new HarmonyMethod(typeof(DwarfWalls),"ProtectGraphics"));
   foreach(var name in new[]{"calculateMainSprite","checkSpriteToRender","calculateColoredSprite","getLastColoredSprite"})harmony.Patch(AccessTools.Method(typeof(Building),name),prefix:new HarmonyMethod(typeof(DwarfWalls),"Render"));
   harmony.Patch(AccessTools.Method(typeof(Building),"isColoredSpriteNeedsCheck"),prefix:new HarmonyMethod(typeof(DwarfWalls),"ColorCheck"));
  }
  public static void ConfigureCannon(BuildingAsset asset) {
   asset.tower_projectile="cannonball";asset.tower_projectile_amount=1;asset.tower_projectile_reload=3f;asset.tower_projectile_offset=12f;
  }
  public static bool ProtectGraphics(BuildingAsset __instance) {BuildingSprites data;if(!graphics.TryGetValue(__instance.id,out data))return true;__instance.building_sprites=data;__instance.sprites_are_initiated=true;return false;}
  public static bool Render(Building __instance,ref Sprite __result) {
   var asset=__instance==null?null:(BuildingAsset)Asset.GetValue(__instance);BuildingSprites data;
   if(asset==null||!graphics.TryGetValue(asset.id,out data))return true;
   var state=((BuildingData)Data.GetValue(__instance)).state;
   bool hidden=asset.id==TowerId&&(state==BuildingState.Ruins||state==BuildingState.Removed||__instance.isOnRemove()||!__instance.isAlive()||!__instance.hasHealth());
   __result=hidden?empty:__instance.isUnderConstruction()?data.construction:sprites[asset.id];LastSprite.SetValue(__instance,__result);return false;
  }
  public static bool ColorCheck(Building __instance,ref bool __result) {var asset=__instance==null?null:(BuildingAsset)Asset.GetValue(__instance);if(asset==null||!graphics.ContainsKey(asset.id))return true;__result=false;return false;}
  public static void Prepare(Fortification plan) {
   if(!IsDwarfCity(plan.City))return;
   plan.Rings=new IList<WorldTile>[]{new List<WorldTile>()};bool current=false;plan.City.data.get(LayoutKey,out current,false);plan.DwarfMigration=!current;
  }
  public static bool Refresh(Fortification plan) {
   if(plan.IsTown || !Construction.IsCapital(plan.City)||!IsDwarfCity(plan.City))return false;
   if(plan.DwarfMigration) {
    Construction.StopCrew(plan);
    var square=new HashSet<KeyValuePair<int,int>>(DwarfLayout.Points(plan.X,plan.Y));
    foreach(var p in FixedLayout.OldPoints(plan.X,plan.Y).Concat(FixedLayout.Points(plan.X,plan.Y)).Distinct()) {
     var tile=MapBox.instance.GetTile(p.Key,p.Value);if(tile==null||square.Contains(p))continue;
     if(Construction.IsSite(tile.building)&&tile.building.getCity()==plan.City){Construction.RemoveSite(tile.building,true);return true;}
     if(Construction.OwnsTile(tile,plan.City)&&Walls.IsWall(tile)){Walls.Restore(tile);return true;}
    }
    foreach(var b in MapBox.instance.buildings.getSimpleList().ToArray())if(Gates.IsGate(b)&&b.isAlive()&&Gates.CityOf(b)==plan.City) {
     bool removing=Gates.Removing;Gates.Removing=true;
     try{if(b.isUnderConstruction())Construction.RemoveSite(b,true);else Gates.RemoveGate(b);}finally{Gates.Removing=removing;}return true;
    }
    foreach(var b in ((List<Building>)Buildings.GetValue(plan.City)).ToArray()) {var a=(BuildingAsset)Asset.GetValue(b);if(a!=null&&a.tower&&b.isAlive()&&b.getCity()==plan.City&&a.id!=TowerId){Remove.Invoke(b,null);return true;}}
    plan.City.data.set(Fortification.MaskKey,"");plan.City.data.set(Fortification.CheckedKey,true);plan.City.data.set(Fortification.VegetationKey,false);plan.City.data.set(Gates.DestroyedKey,0);plan.City.data.set(LayoutKey,true);plan.DwarfMigration=false;return true;
   }
   if(!plan.Ready)return false;
   foreach(var tile in plan.Rings[0]) {
    // Repaint inherited stone styles only. Wooden walls need a paid upgrade job.
    if(!CapitalStages.IsStone(tile)||tile.main_type.id.StartsWith(Prefix)||!Construction.OwnsTile(tile,plan.City))continue;
    var original=Walls.Original(tile);TileType wall;if(original==null||!Types.TryGetValue(original.id,out wall))continue;tile.setTileTypes(wall,null,true);Walls.Revision++;return true;
   }return false;
  }
 }
 public static class DwarfArt {
  static void Paint(byte[] p,int w,int x,int y,int r,int g,int b){int i=(y*w+x)*4;p[i]=(byte)r;p[i+1]=(byte)g;p[i+2]=(byte)b;p[i+3]=255;}
  public static byte[] WallPixels(bool construction,int variant) {
   var p=new byte[16*16*4];int top=construction?7:14;
   for(int y=0;y<=top;y++)for(int x=0;x<16;x++) {
    if(y==top&&x%4>=2)continue;int grain=(x*7+y*3+variant*5)%13;int r=104+grain,g=111+grain,b=126+grain;
    if(y%4==0||(x+(y/4%2)*4)%8==0){r=51;g=57;b=70;}
    if(y==1||y==top-1){r=164;g=172;b=184;}
    if(!construction&&y>=5&&y<=10&&Math.Abs(x-8)+Math.Abs(y-8)<=2){r=203;g=154;b=63;if(x==8){r=244;g=205;b=108;}}
    if(construction&&(x==2||x==13)){r=132;g=88;b=49;}
    Paint(p,16,x,y,r,g,b);
   }return p;
  }
  public static byte[] TowerPixels(bool construction=false) {
   const int w=32,h=36;var p=new byte[w*h*4];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
    if(x<3||x>28||y>30&&x%7>=4)continue;
    if(construction&&y>14&&x!=4&&x!=27&&y!=24)continue;
    int r=111+(x+y)%9,g=118+(x+y)%9,b=132+(x+y)%9;
    if(y%6==0||(x+(y/6%2)*5)%10==0){r=51;g=57;b=70;}
    if(y==2||y==22||y==29){r=197;g=153;b=73;}
    if(y>=5&&y<=13&&Math.Abs(x-15)<=3){r=28;g=33;b=43;if(x==15){r=210;g=168;b=80;}}
    if(y>=24&&y<=28&&x>=9&&x<=23){r=38;g=44;b=54;if(y==28){r=137;g=145;b=159;}if(x>=21){r=17;g=21;b=29;}}
    if(y>=16&&y<=20&&Math.Abs(x-15)+Math.Abs(y-18)<=3){r=214;g=158;b=63;}
    Paint(p,w,x,y,r,g,b);
   }return p;
  }
  public static byte[] GatePixels(bool construction) {
   const int w=68,h=32;var p=new byte[w*h*4];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
    if(y<4||y>27||y==27&&x%6>=4)continue;
    bool pillar=x<9||x>58;bool lintel=y>=23;
    if(construction&&!pillar&&!lintel&&x%10!=0&&y!=8&&y!=17)continue;
    int r=104+(x+y)%11,g=112+(x+y)%11,b=129+(x+y)%11;
    if(pillar||lintel){if(y%5==0||x%8==0){r=48;g=55;b=68;}if(y==5||y==24){r=197;g=149;b=62;}}
    else {r=43;g=50;b=65;if(x%5==0||x==33||x==34){r=19;g=24;b=33;}if(y==8||y==18){r=153;g=113;b=54;}if(x%8==3&&(y==8||y==18)){r=230;g=185;b=87;}}
    if(!construction&&Math.Abs(x-34)+Math.Abs(y-14)<=5){r=37;g=133;b=171;if(x<=34){r=92;g=217;b=230;}if(x==34&&y==17){r=209;g=252;b=251;}}
    Paint(p,w,x,y,r,g,b);
   }return p;
  }
 }
}

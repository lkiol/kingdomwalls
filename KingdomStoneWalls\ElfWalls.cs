using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
 public static class ElfLayout {
  public const int Radius=40, Margin=55, TowerCount=16;
  static readonly List<KeyValuePair<int,int>> offsets=CreateRing();
  static bool Disk(int x,int y) { return x*x+y*y<=(Radius+.5)*(Radius+.5); }
  static List<KeyValuePair<int,int>> CreateRing() {
   var points=new List<KeyValuePair<int,int>>();
   for(int x=-Radius;x<=Radius;x++) for(int y=-Radius;y<=Radius;y++) {
    if(!Disk(x,y) || Math.Abs(x)<=FixedLayout.Gate && Math.Abs(y)>=Radius-2 || Math.Abs(y)<=FixedLayout.Gate && Math.Abs(x)>=Radius-2) continue;
    bool edge=false;
    for(int dx=-1;dx<=1;dx++) for(int dy=-1;dy<=1;dy++) if(!Disk(x+dx,y+dy)) edge=true;
    if(edge) points.Add(new KeyValuePair<int,int>(x,y));
   }
   // A stable, connected digital circle. Eight-neighbor shoulders seal diagonal gaps.
   return points.OrderBy(p=>Math.Atan2(p.Value,p.Key)).ThenBy(p=>p.Key).ThenBy(p=>p.Value).ToList();
  }
  public static IEnumerable<KeyValuePair<int,int>> Points(int cx,int cy) {
   foreach(var p in offsets) yield return new KeyValuePair<int,int>(cx+p.Key,cy+p.Value);
  }
  public static bool IntersectsReserved(int cx,int cy,int x0,int y0,int x1,int y1) {
   // Every wall clearance and gate foundation lies within this square.
   if(x0>cx+Radius+FixedLayout.Clearance || x1<cx-Radius-FixedLayout.Clearance ||
      y0>cy+Radius+FixedLayout.Clearance || y1<cy-Radius-FixedLayout.Clearance) return false;
   foreach(var p in offsets) if(x0<=cx+p.Key+FixedLayout.Clearance && x1>=cx+p.Key-FixedLayout.Clearance && y0<=cy+p.Value+FixedLayout.Clearance && y1>=cy+p.Value-FixedLayout.Clearance) return true;
   // Reserve all four complete gate foundations, including the build approach.
   return Rect(x0,y0,x1,y1,cx-6,cy+Radius-2,cx+6,cy+Radius+2) || Rect(x0,y0,x1,y1,cx-6,cy-Radius-2,cx+6,cy-Radius+2) ||
    Rect(x0,y0,x1,y1,cx+Radius-2,cy-6,cx+Radius+2,cy+6) || Rect(x0,y0,x1,y1,cx-Radius-2,cy-6,cx-Radius+2,cy+6);
  }
  static bool Rect(int x0,int y0,int x1,int y1,int a,int b,int c,int d) { return x0<=c && x1>=a && y0<=d && y1>=b; }
  public static IEnumerable<KeyValuePair<int,int>> TowerPoints(int cx,int cy,BuildingFundament f) {
   // The nearest corner of every foundation stays beyond the full wall/work lane.
   int extent=Math.Max(Math.Max(f.left,f.right),Math.Max(f.top,f.bottom));
   double radius=Radius+FixedLayout.Clearance+2+Math.Sqrt(2)*extent;
   for(int i=0;i<TowerCount;i++) {
    double angle=(i+.5)*2*Math.PI/TowerCount;
    yield return new KeyValuePair<int,int>(cx+(int)Math.Round(radius*Math.Cos(angle)),cy+(int)Math.Round(radius*Math.Sin(angle)));
   }
  }
 }
 public static class ElfWalls {
  public const string Prefix="ksw_elf_",SiteId="ksw_elf_site",TowerId="ksw_elf_tower",HorizontalId="ksw_elf_gate_horizontal",VerticalId="ksw_elf_gate_vertical",LayoutKey="ksw_elf_circle_v1";
  public static BuildingAsset Site,Tower,HorizontalGate,VerticalGate;
  public static readonly Dictionary<string,TileType> Types=new Dictionary<string,TileType>();
  static readonly Dictionary<string,BuildingSprites> graphics=new Dictionary<string,BuildingSprites>();
  static readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
  static Sprite empty;
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset"),Data=AccessTools.Field(typeof(Building),"data"),Buildings=AccessTools.Field(typeof(City),"buildings");
  static readonly System.Reflection.FieldInfo LastSprite=AccessTools.Field(typeof(Building),"last_main_sprite");
  static readonly System.Reflection.MethodInfo Remove=AccessTools.Method(typeof(Building),"removeBuildingFinal");
  public static bool IsElfCity(City city) { var a=city?.getActorAsset(); return a!=null && (a.id=="elf" || a.base_asset_id=="elf"); }
  public static bool IsSite(Building b) { return b!=null && ((BuildingAsset)Asset.GetValue(b))?.id==SiteId; }
  public static bool IsGateId(string id) { return id==HorizontalId || id==VerticalId; }
  public static bool Reserved(Fortification plan,int x0,int y0,int x1,int y1) {
   return plan.IsTown?TownLayout.IntersectsReserved(plan.X,plan.Y,x0,y0,x1,y1):IsElfCity(plan.City)?ElfLayout.IntersectsReserved(plan.X,plan.Y,x0,y0,x1,y1):DwarfWalls.IsDwarfCity(plan.City)?DwarfLayout.IntersectsReserved(plan.X,plan.Y,x0,y0,x1,y1):FixedLayout.IntersectsReserved(plan.X,plan.Y,x0,y0,x1,y1);
  }
  public static Sprite MakeSprite(string kind,bool construction=false,int variant=0) {
   bool tower=kind=="tower"; int w=tower?24:16,h=tower?40:16;
   var texture=new Texture2D(w,h,TextureFormat.RGBA32,false); texture.filterMode=FilterMode.Point;
   texture.LoadRawTextureData(tower?ElfArt.TowerPixels():ElfArt.WallPixels(construction,variant)); texture.Apply();
   var sprite=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),construction?4:1);
   sprite.name="ksw_elf_"+kind+(construction?"_scaffold":""); return sprite;
  }
  static void Graphics(BuildingAsset asset,string kind) {
   var sprite=MakeSprite(kind); var scaffold=MakeSprite("wall",true);
   var frames=new BuildingAnimationData {main=new[]{sprite},main_disabled=new[]{sprite},spawn=new[]{sprite},ruins=new[]{sprite},special=new[]{sprite}};
   var data=new BuildingSprites {construction=scaffold,map_icon=new BuildingMapIcon(sprite)};data.animation_data.Add(frames);
   sprites[asset.id]=sprite;graphics[asset.id]=data;
   asset.has_kingdom_color=false;asset.random_flip=false;ProtectGraphics(asset);
   PreloadHelpers.all_preloaded_sprites_buildings.Add(sprite);PreloadHelpers.all_preloaded_sprites_buildings.Add(scaffold);
  }
  public static void Register() {
   var transparent=new Texture2D(1,1,TextureFormat.RGBA32,false); transparent.SetPixel(0,0,Color.clear); transparent.Apply();
   empty=Sprite.Create(transparent,new Rect(0,0,1,1),new Vector2(.5f,.5f),1);
   var terrainSprites=new TileSprites();for(int i=0;i<4;i++)terrainSprites.addVariation(MakeSprite("wall",false,i),"elf"+i);
   foreach(var pair in Walls.Types) {
    var wall=AssetManager.tiles.clone(Prefix+pair.Key,pair.Value.id);wall.sprites=terrainSprites;wall.color=new Color32(187,208,179,255);
    Types[pair.Key]=wall;Walls.Track(wall);
   }
   Site=AssetManager.buildings.clone(SiteId,Construction.SiteId);Site.atlas_asset=Construction.Site.atlas_asset;Site.base_stats=(BaseStats)Construction.Site.base_stats.Clone();Site.cost=new ConstructionCost {stone=Construction.WallCost};Graphics(Site,"wall");
   var native=AssetManager.buildings.list.FirstOrDefault(a=>a.tower && a.city_building && a.civ_kingdom=="elf");
   if(native==null) throw new InvalidOperationException("Native elven tower asset is unavailable");
   Tower=AssetManager.buildings.clone(TowerId,native.id);Tower.atlas_asset=native.atlas_asset;Tower.base_stats=(BaseStats)native.base_stats.Clone();
   Tower.ignored_by_cities=true;Tower.can_be_upgraded=false;Tower.upgrade_to=null;Graphics(Tower,"tower");
   Tower.has_ruin_state=false;Tower.has_ruins_graphics=false;Tower.has_sprites_ruin=false;Tower.auto_remove_ruin=true;Tower.remove_ruins=true;
   HorizontalGate=Gates.MakeAsset(HorizontalId,false,true);VerticalGate=Gates.MakeAsset(VerticalId,true,true);
   var harmony=new Harmony("custom.kingdom_stone_walls.elves");
   harmony.Patch(AccessTools.Method(typeof(BuildingAsset),"loadBuildingSprites"),prefix:new HarmonyMethod(typeof(ElfWalls),"ProtectGraphics"));
   harmony.Patch(AccessTools.Method(typeof(BuildingAsset),"checkSpritesAreLoaded"),prefix:new HarmonyMethod(typeof(ElfWalls),"ProtectGraphics"));
   foreach(var name in new[]{"calculateMainSprite","checkSpriteToRender","calculateColoredSprite","getLastColoredSprite"}) harmony.Patch(AccessTools.Method(typeof(Building),name),prefix:new HarmonyMethod(typeof(ElfWalls),"Render"));
   harmony.Patch(AccessTools.Method(typeof(Building),"isColoredSpriteNeedsCheck"),prefix:new HarmonyMethod(typeof(ElfWalls),"ColorCheck"));
  }
  public static bool ProtectGraphics(BuildingAsset __instance) {
   BuildingSprites data;if(!graphics.TryGetValue(__instance.id,out data))return true;
   __instance.building_sprites=data;__instance.sprites_are_initiated=true;return false;
  }
  public static bool Render(Building __instance,ref Sprite __result) {
   var asset=__instance==null?null:(BuildingAsset)Asset.GetValue(__instance);BuildingSprites data;
   if(asset==null || !graphics.TryGetValue(asset.id,out data))return true;
   var state=((BuildingData)Data.GetValue(__instance)).state;
   bool hidden=asset.id==TowerId && (state==BuildingState.Ruins || state==BuildingState.Removed || __instance.isOnRemove() || !__instance.isAlive() || !__instance.hasHealth());
   __result=hidden?empty:__instance.isUnderConstruction()?data.construction:sprites[asset.id];
   // Native nighttime lights hash this cache even when recoloring is bypassed.
   // Refresh it with the current frame so completion cannot retain a scaffold.
   LastSprite.SetValue(__instance,__result);return false;
  }
  public static bool ColorCheck(Building __instance,ref bool __result) {
   var asset=__instance==null?null:(BuildingAsset)Asset.GetValue(__instance);if(asset==null || !graphics.ContainsKey(asset.id))return true;
   __result=false;return false;
  }
  public static void Prepare(Fortification plan) {
   if(!IsElfCity(plan.City))return;
   plan.Rings=new IList<WorldTile>[] {new List<WorldTile>()};
   bool current=false;plan.City.data.get(LayoutKey,out current,false);plan.ElfMigration=!current;
  }
  public static bool Refresh(Fortification plan) {
   if(plan.IsTown || !Construction.IsCapital(plan.City) || !IsElfCity(plan.City))return false;
   // Remove interior towers, including towers saved by the prior square layout.
   foreach(var b in ((List<Building>)Buildings.GetValue(plan.City)).ToArray()) {
    var asset=(BuildingAsset)Asset.GetValue(b);var tile=b.current_tile;
    if(asset!=null && asset.tower && b.isAlive() && b.getCity()==plan.City && tile!=null && Inside(plan,tile,asset.fundament)) {Remove.Invoke(b,null);return true;}
   }
   if(plan.ElfMigration) {
    var circle=new HashSet<KeyValuePair<int,int>>(ElfLayout.Points(plan.X,plan.Y));
    foreach(var p in FixedLayout.OldPoints(plan.X,plan.Y).Concat(FixedLayout.Points(plan.X,plan.Y)).Distinct()) {
     var tile=MapBox.instance.GetTile(p.Key,p.Value);if(tile==null || circle.Contains(p))continue;
     if(Construction.IsSite(tile.building) && tile.building.getCity()==plan.City) {Construction.RemoveSite(tile.building,true);return true;}
     if(Construction.OwnsTile(tile,plan.City) && Walls.IsWall(tile)) {Walls.Restore(tile);return true;}
    }
    foreach(var b in MapBox.instance.buildings.getSimpleList().ToArray()) if(Gates.IsGate(b) && b.isAlive() && Gates.CityOf(b)==plan.City) {
     bool removing=Gates.Removing;Gates.Removing=true;
     try {if(b.isUnderConstruction())Construction.RemoveSite(b,true);else Gates.RemoveGate(b);}finally{Gates.Removing=removing;}
     return true;
    }
    plan.City.data.set(Fortification.MaskKey,"");plan.City.data.set(Fortification.CheckedKey,true);plan.City.data.set(Fortification.VegetationKey,false);plan.City.data.set(Gates.DestroyedKey,0);
    plan.City.data.set(LayoutKey,true);plan.ElfMigration=false;return true;
   }
   if(!plan.Ready)return false;
   foreach(var tile in plan.Rings[0]) {
    // Repaint inherited stone styles only. Wooden walls need a paid upgrade job.
    if(!CapitalStages.IsStone(tile) || tile.main_type.id.StartsWith(Prefix) || !Construction.OwnsTile(tile,plan.City))continue;
    var original=Walls.Original(tile);TileType wall;if(original==null||!Types.TryGetValue(original.id,out wall))continue;
    tile.setTileTypes(wall,null,true);Walls.Revision++;return true;
   }
   return false;
  }
  public static bool Inside(Fortification plan,WorldTile tile,BuildingFundament f) {
   int x=tile.x-plan.X,y=tile.y-plan.Y;
   int dx=x<0?Math.Min(0,x+(f?.right??0)):Math.Max(0,x-(f?.left??0));
   int dy=y<0?Math.Min(0,y+(f?.top??0)):Math.Max(0,y-(f?.bottom??0));
   return dx*dx+dy*dy<=(ElfLayout.Radius+1)*(ElfLayout.Radius+1);
  }
 }
 public static class ElfArt {
  static void Paint(byte[] p,int w,int x,int y,int r,int g,int b) {int i=(y*w+x)*4;p[i]=(byte)r;p[i+1]=(byte)g;p[i+2]=(byte)b;p[i+3]=255;}
  public static byte[] WallPixels(bool construction,int variant) {
   var p=new byte[1024];int height=construction?7:13;
   for(int y=0;y<=height;y++)for(int x=0;x<16;x++) {
    if(y==height && x%4>=2)continue;
    int grain=(x*5+y*3+variant*7)%11;int r=187+grain,g=204+grain,b=170+grain;
    if(y%4==0 || (x+(y/4%2)*4)%8==0){r=104;g=137;b=110;}
    if(y==height-1 || y==1){r=217;g=226;b=190;}
    if(!construction && x>=5 && x<=10 && y>=4 && y<=10 && (Math.Abs(x-8)+Math.Abs(y-7)<=3)){r=57;g=115;b=76;if(x==8){r=180;g=199;b=116;}}
    Paint(p,16,x,y,r,g,b);
   }return p;
  }
  public static byte[] TowerPixels() {
   const int w=24,h=40;var p=new byte[w*h*4];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
    int half=y<4?10:y<25?7:Math.Max(0,(39-y)*9/14);if(Math.Abs(x-12)>half)continue;
    int r=190,g=209,b=177;
    if(y>=25){r=37;g=99+(x%3)*10;b=69;if(x<12){r+=20;g+=24;b+=15;}if(y==25){r=213;g=216;b=153;}}
    else {
     if(x==12-half || y%6==0){r=109;g=144;b=118;}
     if((y>=5&&y<=12 || y>=17&&y<=22) && Math.Abs(x-12)<=2){r=32;g=62;b=48;if(x==12){r=191;g=193;b=116;}}
     if(y==3||y==14||y==24){r=219;g=226;b=185;}
    }
    Paint(p,w,x,y,r,g,b);
   }return p;
  }
  public static byte[] GatePixels(bool vertical,bool construction) {
   int w=vertical?24:36,h=vertical?36:24;var p=new byte[w*h*4];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
    int along=vertical?y:x,across=(vertical?x:y)-6;
    if(across<2||across>9)continue;
    int r=94,g=143,b=106;
    if(along<5||along>30){r=199;g=214;b=181;if(across==2||across==9){r=125;g=155;b=122;}}
    else {if(along%4==0){r=42;g=91;b=60;}if(across==3||across==8){r=192;g=193;b=114;}if(along==17||along==18){r=225;g=224;b=149;}}
    if(construction && along>=5&&along<=30){if(across>6)continue;r=123;g=155;b=99;}
    Paint(p,w,x,y,r,g,b);
   }return p;
  }
 }
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
 // A compact, single-ring timber fort; construction starts above 200 people.
 public static class TownLayout {
  public const int Radius=16, Margin=19;
  public static IEnumerable<KeyValuePair<int,int>> Points(int x,int y) {
   for(int offset=-Radius;offset<=Radius;offset++) {
    // North and south retain the capital-style nine-tile gate openings.
    if(Math.Abs(offset)>FixedLayout.Gate) {
     yield return new KeyValuePair<int,int>(x+offset,y-Radius);
     yield return new KeyValuePair<int,int>(x+offset,y+Radius);
    }
    if(Math.Abs(offset)==Radius) continue;
    yield return new KeyValuePair<int,int>(x-Radius,y+offset);
    yield return new KeyValuePair<int,int>(x+Radius,y+offset);
   }
  }
  public static bool IntersectsReserved(int x,int y,int x0,int y0,int x1,int y1) {
   // The wall shoulders join the two gate foundations into complete five-tile
   // work lanes. Reserve only those lanes, leaving the town interior buildable.
   int outer=Radius+FixedLayout.Clearance,inner=Radius-FixedLayout.Clearance;
   return x0<=x+outer && x1>=x-outer && y0<=y+outer && y1>=y-outer &&
    (x0<=x-inner || x1>=x+inner || y0<=y-inner || y1>=y+inner);
  }
 }
 public static class TownWalls {
  public const string Prefix="ksw_town_wood_",SiteId="ksw_town_wood_site",HorizontalId="ksw_town_wood_gate_horizontal",VerticalId="ksw_town_wood_gate_vertical",LayoutKey="ksw_town_wood_v2",OldLayoutKey="ksw_town_wood_v1";
  public static BuildingAsset Site,HorizontalGate,VerticalGate;
  public static readonly Dictionary<string,TileType> Types=new Dictionary<string,TileType>();
  static readonly System.Reflection.FieldInfo Asset=AccessTools.Field(typeof(Building),"asset");
  public static bool IsSite(Building b) { return b!=null && ((BuildingAsset)Asset.GetValue(b))?.id==SiteId; }
  public static bool IsGateId(string id) { return id==HorizontalId || id==VerticalId; }
  public static void Upgrade(City city) {
   bool current=false; city.data.get(LayoutKey,out current,false);
   if(current) return;
   bool old=false; city.data.get(OldLayoutKey,out old,false);
   if(old) {
    // Old plans had east and west gates. Clear them before the new wall mask is built.
    foreach(var gate in System.Linq.Enumerable.ToArray(Gates.All)) {
     if(gate==null || !Gates.IsGate(gate) || Gates.CityOf(gate)!=city ||
        !IsGateId(((BuildingAsset)Asset.GetValue(gate)).id)) continue;
     int slot=-1; ((BuildingData)AccessTools.Field(typeof(Building),"data").GetValue(gate)).get("ksw_gate_slot",out slot,-1);
     if(slot!=2 && slot!=3) continue;
     bool removing=Gates.Removing; Gates.Removing=true;
     try {
      if(gate.isUnderConstruction()) Construction.RemoveSite(gate,true);
      else Gates.RemoveGate(gate);
     } finally { Gates.Removing=removing; }
    }
    int destroyed=0; city.data.get(Gates.DestroyedKey,out destroyed,0);
    city.data.set(Gates.DestroyedKey,destroyed & 3);
   }
   city.data.set(LayoutKey,true);
  }
  public static void Register() {
   var sprites=new TileSprites();
   for(int i=0;i<4;i++) sprites.addVariation(OrcWalls.MakeSprite(false,i),"town_wood"+i);
   foreach(var pair in Walls.Types) {
    var wall=AssetManager.tiles.clone(Prefix+pair.Key,pair.Value.id);
    wall.sprites=sprites; wall.color=new Color32(128,87,48,255);
    Types[pair.Key]=wall; Walls.Track(wall);
   }
   // The native worker task already handles the orc log scaffold and wood payment.
   Site=OrcWalls.Site;
   HorizontalGate=Gates.MakeAsset(HorizontalId,false,false,false,true);
   VerticalGate=Gates.MakeAsset(VerticalId,true,false,false,true);
  }
 }
 public static class TownGateArt {
  public static Sprite MakeSprite(bool vertical,bool construction) {
   int width=vertical?24:36,height=vertical?36:24;
   var pixels=GateArt.Pixels(vertical,construction);
   for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
    int i=(y*width+x)*4;if(pixels[i+3]==0) continue;
    int across=vertical?y:x,up=vertical?x:y;
    if(across<5 || across>=31) {pixels[i]=112;pixels[i+1]=72;pixels[i+2]=38;}
    else if(up==6 || up==15) {pixels[i]=89;pixels[i+1]=53;pixels[i+2]=27;}
   }
   var texture=new Texture2D(width,height,TextureFormat.RGBA32,false) {filterMode=FilterMode.Point};
   texture.LoadRawTextureData(pixels);texture.Apply();
   var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),4);
   sprite.name=(vertical?TownWalls.VerticalId:TownWalls.HorizontalId)+(construction?"_construction":"");return sprite;
  }
 }
}

using System;
using System.Collections.Generic;

namespace KingdomStoneWalls {
  public static class CapitalStages {
   public static bool IsStone(WorldTile tile) {
    return Walls.IsWall(tile) && !tile.main_type.id.StartsWith(OrcWalls.Prefix) &&
     !tile.main_type.id.StartsWith(TownWalls.Prefix);
   }
   public static int WoodRing(Fortification plan) {
    return RingOrder.FirstIncomplete(plan.Rings,Walls.IsWall);
   }
   public static int StoneRing(Fortification plan) {
    return plan.IsTown || OrcWalls.IsOrcCity(plan.City)?-1:RingOrder.FirstIncomplete(plan.Rings,IsStone);
   }
   public static int ActiveRing(Fortification plan) {
    int wood=WoodRing(plan); return wood>=0?wood:StoneRing(plan);
   }
   public static bool Upgrading(Fortification plan) {
    return !plan.IsTown && WoodRing(plan)<0 && StoneRing(plan)>=0;
   }
   public static bool Complete(WorldTile tile,Fortification plan) {
    return Upgrading(plan)?IsStone(tile):Walls.IsWall(tile);
   }
   public static BuildingAsset SiteFor(Fortification plan) {
    if(plan.IsTown) return TownWalls.Site;
    if(!Upgrading(plan)) return OrcWalls.Site;
    if(ElfWalls.IsElfCity(plan.City)) return ElfWalls.Site;
    if(DwarfWalls.IsDwarfCity(plan.City)) return DwarfWalls.Site;
    return Construction.Site;
   }
   public static string MaterialFor(Fortification plan) {
    return plan.IsTown || !Upgrading(plan)?"wood":"stone";
   }
   public static bool TypeFor(Building site,WorldTile tile,City city,out TileType wall) {
    var original=Walls.Original(tile);
    if(original==null) {wall=null;return false;}
    if(OrcWalls.IsSite(site) || TownWalls.IsSite(site))
     return (TownWalls.IsSite(site)?TownWalls.Types:OrcWalls.Types).TryGetValue(original.id,out wall);
    if(ElfWalls.IsSite(site)) return ElfWalls.Types.TryGetValue(original.id,out wall);
    if(DwarfWalls.IsSite(site)) return DwarfWalls.Types.TryGetValue(original.id,out wall);
    return Walls.Types.TryGetValue(original.id,out wall);
   }
  }
 }

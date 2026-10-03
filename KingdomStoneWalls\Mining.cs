using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ai.behaviours;
using HarmonyLib;

namespace KingdomStoneWalls {
 public static class Mining {
  const string MineStoneKey="ksw_civilization_mine_stone";
  static readonly FieldInfo Target=AccessTools.Field(typeof(Actor),"beh_building_target");
  static readonly FieldInfo ActorCity=AccessTools.Field(typeof(Actor),"city");
  static readonly FieldInfo Data=AccessTools.Field(typeof(Building),"data");
  static readonly MethodInfo Spawn=AccessTools.Method(typeof(BuildingHelper),"tryToBuildNear",new[]{typeof(WorldTile),typeof(BuildingAsset)});
  static readonly MethodInfo AddBuilding=AccessTools.Method(typeof(BuildingManager),"addBuilding",new[]{typeof(BuildingAsset),typeof(WorldTile),typeof(bool),typeof(bool),typeof(BuildPlacingType)});
  static readonly MethodInfo AddInventory=AccessTools.Method(typeof(Actor),"addToInventory",new[]{typeof(string),typeof(int)});
  [ThreadStatic] static bool spawningMineStone;

  public static void Register() {
   var mine=AccessTools.Method(typeof(BehGetResourcesFromMine),"execute",new[]{typeof(Actor)});
   var extract=AccessTools.Method(typeof(BehExtractResourcesFromBuilding),"execute",new[]{typeof(Actor)});
   if(mine==null || extract==null || Spawn==null || AddBuilding==null || AddInventory==null || Target==null || ActorCity==null || Data==null)
    throw new InvalidOperationException("Kingdom Stone Walls: unsupported WorldBox mining layout.");
   var harmony=new Harmony("custom.kingdom_stone_walls.mining");
   harmony.Patch(mine,transpiler:new HarmonyMethod(typeof(Mining),nameof(MineSpawnTranspiler)));
   harmony.Patch(AddBuilding,postfix:new HarmonyMethod(typeof(Mining),nameof(MarkMineStone)));
   harmony.Patch(extract,transpiler:new HarmonyMethod(typeof(Mining),nameof(ExtractTranspiler)));
  }

  public static IEnumerable<CodeInstruction> MineSpawnTranspiler(IEnumerable<CodeInstruction> source) {
   var codes=source.ToList();
   int call=codes.FindIndex(c=>c.Calls(Spawn));
   if(call<0 || codes.Count(c=>c.Calls(Spawn))!=1) throw new InvalidOperationException("WorldBox mine spawn call changed.");
   codes.Insert(call,new CodeInstruction(OpCodes.Ldarg_1));
   codes[call+1].opcode=OpCodes.Call;
   codes[call+1].operand=AccessTools.Method(typeof(Mining),nameof(SpawnFromCivilizationMine));
   return codes;
  }

  public static bool SpawnFromCivilizationMine(WorldTile tile,BuildingAsset mineral,Actor miner) {
   if(mineral==null || mineral.building_type!=BuildingType.Building_Mineral || mineral.resources_given==null ||
      !mineral.resources_given.Any(r=>r.id=="stone")) return BuildingHelper.tryToBuildNear(tile,mineral);
   var mine=(Building)Target.GetValue(miner);
   var city=mine==null?null:mine.getCity();
   if(city==null) city=(City)ActorCity.GetValue(miner);
   var kingdom=city==null?null:Construction.Owner(city);
   if(city==null || !city.isAlive() || kingdom==null || !kingdom.isCiv())
    return BuildingHelper.tryToBuildNear(tile,mineral);
   bool previous=spawningMineStone;
   spawningMineStone=true;
   try { return BuildingHelper.tryToBuildNear(tile,mineral); }
   finally { spawningMineStone=previous; }
  }

  public static void MarkMineStone(Building __result) {
   if(!spawningMineStone || __result==null) return;
   var data=(BuildingData)Data.GetValue(__result);
   if(data!=null) data.set(MineStoneKey,true);
  }

  public static IEnumerable<CodeInstruction> ExtractTranspiler(IEnumerable<CodeInstruction> source) {
   var codes=source.ToList();
   int call=codes.FindIndex(c=>c.Calls(AddInventory));
   if(call<0 || codes.Count(c=>c.Calls(AddInventory))!=1) throw new InvalidOperationException("WorldBox resource extraction call changed.");
   codes[call].opcode=OpCodes.Call;
   codes[call].operand=AccessTools.Method(typeof(Mining),nameof(AddGatheredResource));
   return codes;
  }

  public static void AddGatheredResource(Actor actor,string resource,int amount) {
   if(resource=="stone") {
    var mineral=(Building)Target.GetValue(actor);
    var data=mineral==null?null:(BuildingData)Data.GetValue(mineral);
    bool fromMine=false;
    if(data!=null) data.get(MineStoneKey,out fromMine,false);
    if(fromMine) amount*=4;
   }
   actor.addToInventory(resource,amount);
  }
 }
}

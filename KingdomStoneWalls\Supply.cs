using System;
using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
 // A capital's supply runner uses the game's walking task and a real inventory.
 // Stock is removed at pickup and credited only after the runner returns home.
 public static class Supply {
  const string Task="ksw_fetch_wall_supplies";
  const int Load=12, DonorReserve=24;
  sealed class Run {
   public Actor Worker;
   public City Home, Donor;
   public Building Source, DropBuilding;
   public WorldTile Pickup, Drop;
   public string Resource;
   public int Amount;
   public int Price;
   public bool Returning, Trade;
   public double LastProgress;
   public int LastDistance;
  }
  static readonly Dictionary<City,Run> runs=new Dictionary<City,Run>();
  static readonly Dictionary<City,double> nextSearch=new Dictionary<City,double>();
  static readonly System.Reflection.FieldInfo Target=AccessTools.Field(typeof(Actor),"beh_tile_target"), Stockpiles=AccessTools.Field(typeof(City),"stockpiles"), StorageVersion=AccessTools.Field(typeof(City),"_storage_version"), Asset=AccessTools.Field(typeof(Building),"asset");
  static readonly System.Reflection.MethodInfo Extract=AccessTools.Method(typeof(Building),"extractResources");
  public static void Register() {
   var task=AssetManager.tasks_actor.clone(Task,"build_building");
   task.list.Clear();
   // Native checkHasRenderedItem compares with string.Empty; null counts as a tool
   // and makes the parallel renderer dereference a missing hand-tool asset.
   task.force_hand_tool=string.Empty;
   task.cached_hand_tool_asset=null;
   task.addBeh(new BehGoToTileTarget());
  }
  public static bool IsRunner(Actor actor) { return actor!=null && Construction.TaskId(actor)==Task; }
  public static void Reset() { runs.Clear(); nextSearch.Clear(); }
  public static void Stop(City city) {
   Run run;
   if(city==null || !runs.TryGetValue(city,out run)) return;
   // Resources already collected remain in the citizen's ordinary inventory.
   if(run.Worker!=null && run.Worker.isAlive() && IsRunner(run.Worker)) Construction.SetCrewTask(run.Worker,"nothing");
   runs.Remove(city);
  }
  static string Needed(Fortification plan) {
   var city=plan.City;
   if(CapitalStages.ActiveRing(plan)>=0) {
    string wall=CapitalStages.MaterialFor(plan);
    if(city.getResourcesAmount(wall)<Construction.MaterialReserve+Construction.WallCost+Load) return wall;
   }
   {
    string gate=Gates.ResourceNeeded(plan);
    if(gate!=null) {
    int cost=gate=="gems"?DwarfWalls.GemCost:Gates.IronCost;
    if(city.getResourcesAmount(gate)<cost) return gate;
    }
   }
   if(!plan.IsTown && plan.Rings.Length>1 && plan.Rings[0].Count>0 && plan.Rings[1].Count>0 && CapitalStages.StoneRing(plan)<0) {
    // Native watchtowers use both wood and stone. Give their foundation enough
    // stock once the wall and gate work is supplied.
    if(city.getResourcesAmount("stone")<12) return "stone";
    if(city.getResourcesAmount("wood")<12) return "wood";
   }
   return null;
  }
  static bool At(Actor actor,WorldTile tile) {
   return actor!=null && actor.current_tile!=null && tile!=null &&
    Math.Abs(actor.current_tile.x-tile.x)+Math.Abs(actor.current_tile.y-tile.y)<=2;
  }
  static void Walk(Run run,WorldTile target) {
   if(!IsRunner(run.Worker)) Construction.SetCrewTask(run.Worker,Task);
   Target.SetValue(run.Worker,target);
  }
  static int Distance(WorldTile a,WorldTile b) { return Math.Abs(a.x-b.x)+Math.Abs(a.y-b.y); }
  static WorldTile Approach(WorldTile source,WorldTile from) {
   WorldTile best=null; int nearest=int.MaxValue;
   foreach(var tile in source.neighbours) {
    if(tile==null || Math.Abs(tile.x-source.x)+Math.Abs(tile.y-source.y)!=1 ||
       !(Walls.Land(tile) || Walls.Road(tile)) || Walls.IsWall(tile) ||
       Construction.BlocksWall(tile.building) || !from.isSameIsland(tile)) continue;
    int distance=Distance(from,tile);
    if(distance<nearest) { best=tile; nearest=distance; }
   }
   return best;
  }
  static Building StockWith(City city,string resource) {
   Building best=null; int amount=0;
   foreach(var stock in (List<Building>)Stockpiles.GetValue(city)) if(stock!=null && stock.isUsable()) {
    int have=stock.getResourcesAmount(resource);
    if(have>amount) { best=stock; amount=have; }
   }
   return best;
  }
  static Building StockWithSpace(City city,string resource) {
   var asset=AssetManager.resources.get(resource);
   Building best=null; int mostRoom=0;
   foreach(var stock in (List<Building>)Stockpiles.GetValue(city))
    if(stock!=null && stock.isUsable() && stock.current_tile!=null && stock.hasSpaceForResource(asset)) {
     int room=Math.Max(0,Math.Min(asset.maximum,asset.storage_max)-stock.getResourcesAmount(resource));
     if(room>mostRoom) { best=stock; mostRoom=room; }
    }
   return best;
  }
  static bool IsStockpile(City city,Building stock) {
   return city!=null && stock!=null && ((List<Building>)Stockpiles.GetValue(city)).Contains(stock);
  }
  static int Store(City city,Building stock,string resource,int amount) {
   if(!IsStockpile(city,stock) || !stock.isUsable() || amount<=0) return 0;
   int before=stock.getResourcesAmount(resource);
   var asset=AssetManager.resources.get(resource);
   int room=Math.Max(0,Math.Min(asset.maximum,asset.storage_max)-before);
   if(room<=0) return 0;
   int delivered=Math.Min(amount,Math.Max(0,stock.addResources(resource,Math.Min(amount,room))-before));
   if(delivered>0) StorageVersion.SetValue(city,(int)StorageVersion.GetValue(city)+1);
   return delivered;
  }
  static bool Find(Fortification plan,string resource,double now,out Run run) {
   run=null; var home=plan.City; var origin=MapBox.instance.GetTile(plan.X,plan.Y);
   if(origin==null) return false;
   City donor=null; Building source=null; int nearest=int.MaxValue;
   // Surplus in another city of the same kingdom is the quickest source.
   foreach(var city in MapBox.instance.cities.list) {
    if(city==null || city==home || !city.isAlive() || Construction.Owner(city)!=Construction.Owner(home) || city.getResourcesAmount(resource)<=DonorReserve) continue;
    var stock=StockWith(city,resource);
    if(stock==null || stock.current_tile==null || !origin.isSameIsland(stock.current_tile)) continue;
    int distance=Distance(origin,stock.current_tile);
    if(distance<nearest) { donor=city; source=stock; nearest=distance; }
   }
   // Wild deposits and trees beyond the capital border can be harvested by its
   // own citizen. Do not harvest inside another kingdom's territory.
   if(donor==null) foreach(var building in MapBox.instance.buildings.getSimpleList()) {
    if(building==null || !building.isAlive() || building.current_tile==null || building.current_tile.zone_city==home || !building.hasResourcesToCollect()) continue;
    var asset=(BuildingAsset)Asset.GetValue(building);
    if(asset==null || !asset.hasResourceGiven(resource)) continue;
    var owner=building.current_tile.zone_city;
    if(owner!=null && Construction.Owner(owner)!=Construction.Owner(home)) continue;
    if(!origin.isSameIsland(building.current_tile)) continue;
    int distance=Distance(origin,building.current_tile);
    if(distance<nearest) { source=building; nearest=distance; }
   }
   // Foreign purchases are a fallback when no accessible source exists. Both
   // kingdoms must be at peace with one another, and payment uses stored gold.
   bool trade=false;
   if(source==null) foreach(var city in MapBox.instance.cities.list) {
    if(city==null || city==home || !city.isAlive()) continue;
    var seller=Construction.Owner(city); var buyer=Construction.Owner(home);
    if(seller==null || buyer==null || seller==buyer || buyer.isInWarWith(seller) || city.getResourcesAmount(resource)<=DonorReserve) continue;
    var stock=StockWith(city,resource);
    if(stock==null || stock.current_tile==null || !origin.isSameIsland(stock.current_tile)) continue;
    int distance=Distance(origin,stock.current_tile);
    if(distance<nearest) { donor=city; source=stock; nearest=distance; trade=true; }
   }
   if(source==null) return false;
   Actor worker=null; nearest=int.MaxValue;
   foreach(var actor in home.units) {
    if(!Construction.EligibleMason(actor) || plan.Crew.Contains(actor) || actor.current_tile==null || actor.isCarryingResources()) continue;
    int distance=Distance(actor.current_tile,source.current_tile);
    if(distance<nearest) { worker=actor; nearest=distance; }
   }
   if(worker==null) return false;
   int amount=donor==null?1:Math.Min(Load,Math.Min(donor.getResourcesAmount(resource)-DonorReserve,source.getResourcesAmount(resource)));
   int price=trade?Math.Max(1,AssetManager.resources.get(resource).trade_cost):0;
   if(trade) amount=Math.Min(amount,home.getResourcesAmount("gold")/price);
   if(amount<=0) return false;
   var drop=StockWithSpace(home,resource);
   if(drop==null || drop.current_tile==null) return false;
   var pickup=Approach(source.current_tile,worker.current_tile);
   var delivery=Approach(drop.current_tile,source.current_tile);
   if(pickup==null || delivery==null) return false;
   run=new Run {Worker=worker,Home=home,Donor=donor,Source=source,DropBuilding=drop,Pickup=pickup,Drop=delivery,Resource=resource,Amount=amount,Price=price,Trade=trade,LastProgress=now,LastDistance=Distance(worker.current_tile,pickup)};
   Walk(run,run.Pickup);
   return true;
  }
  static void Finish(Run run) {
   if(run.Worker!=null && run.Worker.isAlive() && IsRunner(run.Worker)) Construction.SetCrewTask(run.Worker,"nothing");
   runs.Remove(run.Home);
  }
  static bool LoadAtSource(Run run,double now) {
   if(run.Source==null || !run.Source.isAlive() || run.Source.current_tile==null) return false;
   if(run.Donor!=null) {
    if(!run.Donor.isAlive() || !IsStockpile(run.Donor,run.Source) || !run.Source.isUsable() ||
       run.Source.getResourcesAmount(run.Resource)<run.Amount || run.Donor.getResourcesAmount(run.Resource)-DonorReserve<run.Amount) return false;
    if(!run.Trade && (Construction.Owner(run.Home)==null || Construction.Owner(run.Donor)!=Construction.Owner(run.Home))) return false;
    if(run.Trade) {
     var buyer=Construction.Owner(run.Home); var seller=Construction.Owner(run.Donor);
     if(buyer==null || seller==null || buyer==seller || buyer.isInWarWith(seller)) return false;
     var paymentStock=StockWithSpace(run.Donor,"gold");
     if(paymentStock==null) return false;
     var gold=AssetManager.resources.get("gold");
     int paymentRoom=Math.Max(0,Math.Min(gold.maximum,gold.storage_max)-paymentStock.getResourcesAmount("gold"));
     run.Amount=Math.Min(run.Amount,paymentRoom/run.Price);
     if(run.Amount<=0) return false;
     int cost=run.Amount*run.Price;
     if(run.Home.getResourcesAmount("gold")<cost) return false;
     run.Home.takeResource("gold",cost);
     int paid=Store(run.Donor,paymentStock,"gold",cost);
     int units=paid/run.Price;
     int excess=paid-units*run.Price;
     if(excess>0) { paymentStock.takeResource("gold",excess); StorageVersion.SetValue(run.Donor,(int)StorageVersion.GetValue(run.Donor)+1); }
     if(units<run.Amount) Store(run.Home,StockWithSpace(run.Home,"gold"),"gold",cost-units*run.Price);
     if(units<=0) return false;
     run.Amount=units;
    }
    run.Source.takeResource(run.Resource,run.Amount);
    StorageVersion.SetValue(run.Donor,(int)StorageVersion.GetValue(run.Donor)+1);
   } else {
    var sourceAsset=(BuildingAsset)Asset.GetValue(run.Source);
    if(!run.Source.hasResourcesToCollect() || sourceAsset==null || !sourceAsset.hasResourceGiven(run.Resource)) return false;
    run.Amount=0;
    foreach(var yield in sourceAsset.resources_given) if(yield.id==run.Resource) run.Amount+=yield.amount;
    if(run.Amount<=0) return false;
    Extract.Invoke(run.Source,new object[]{run.Worker});
   }
   ActorBagExtensions.add(run.Worker.inventory,run.Resource,run.Amount);
   run.Returning=true;
   run.LastProgress=now;
   run.LastDistance=Distance(run.Worker.current_tile,run.Drop);
   Walk(run,run.Drop);
   return true;
  }
  public static void Tick(Fortification plan,double now) {
   if(!Construction.Enabled || Construction.Faulted || !Construction.IsFortifiedCity(plan.City) || !plan.Ready || !Construction.AtPeace(plan.City)) { Stop(plan.City); return; }
   Run run;
   if(runs.TryGetValue(plan.City,out run)) {
    if(run.Worker==null || !run.Worker.isAlive() || run.Worker.city!=plan.City || run.Worker.current_tile==null) { Finish(run); return; }
    if(run.Worker.isFighting() || run.Worker.isHungry()) { Finish(run); return; }
    var destination=run.Returning?run.Drop:run.Pickup;
    int remaining=Distance(run.Worker.current_tile,destination);
    if(remaining<run.LastDistance) { run.LastDistance=remaining; run.LastProgress=now; }
    // A trip across test 7's landmass takes longer than a fixed 90-second run.
    // Give up only when the courier has made no progress toward its current stop.
    if(now-run.LastProgress>90) { Finish(run); return; }
    if(!run.Returning && At(run.Worker,run.Pickup)) {
     if(!LoadAtSource(run,now)) Finish(run);
    } else if(run.Returning && At(run.Worker,run.Drop)) {
     var stock=IsStockpile(plan.City,run.DropBuilding) && run.DropBuilding.isUsable() && run.DropBuilding.hasSpaceForResource(AssetManager.resources.get(run.Resource))?run.DropBuilding:StockWithSpace(plan.City,run.Resource);
     int delivered=Store(plan.City,stock,run.Resource,run.Amount);
     if(delivered>0) ActorBagExtensions.remove(run.Worker.inventory,run.Resource,delivered);
     run.Amount-=delivered;
     if(run.Amount<=0) { Finish(run); return; }
     stock=StockWithSpace(plan.City,run.Resource);
     var nextDrop=stock==null?null:Approach(stock.current_tile,run.Worker.current_tile);
     if(nextDrop==null) { Finish(run); return; }
     run.DropBuilding=stock; run.Drop=nextDrop; run.LastDistance=Distance(run.Worker.current_tile,nextDrop); run.LastProgress=now; Walk(run,nextDrop);
    } else Walk(run,destination);
    return;
   }
   double next;
   if(nextSearch.TryGetValue(plan.City,out next) && now<next) return;
   nextSearch[plan.City]=now+8;
   var needed=Needed(plan);
   if(needed!=null && Find(plan,needed,now,out run)) runs[plan.City]=run;
  }
 }
}

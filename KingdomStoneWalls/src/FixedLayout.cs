using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace KingdomStoneWalls {
  public static class FixedLayout {
   public const int Radius=22, Gate=6, Clearance=2, Inner=18, Outer=22;
   public static IEnumerable<KeyValuePair<int,int>> Points(int cx,int cy) {
    for(int x=-Radius;x<=Radius;x++) for(int y=-Radius;y<=Radius;y++) {
     if(Math.Abs(x) <= Gate && Math.Abs(y) >= Radius-2 || Math.Abs(y) <= Gate && Math.Abs(x) >= Radius-2) continue;
     if(x*x+y*y <= (Radius+0.5)*(Radius+0.5)) yield return new KeyValuePair<int,int>(cx+x,cy+y);
    }
   }
   public static bool IntersectsReserved(int cx,int cy,int x0,int y0,int x1,int y1) {
    int outer=Radius+Clearance,inner=Radius-Clearance;
    return x0<=cx+outer && x1>=cx-outer && y0<=cy+outer && y1>=cy-outer &&
     (x0<=cx-inner || x1>=cx+inner || y0<=cy-inner || y1>=cy+inner);
   }
   public static IEnumerable<KeyValuePair<int,int>> OldPoints(int cx,int cy) {
    for(int x=-Radius;x<=Radius;x++) for(int y=-Radius;y<=Radius;y++) {
     if(Math.Abs(x) <= Gate && Math.Abs(y) >= Radius-2 || Math.Abs(y) <= Gate && Math.Abs(x) >= Radius-2) continue;
     if(x*x+y*y < (Radius+0.5)*(Radius+0.5)) yield return new KeyValuePair<int,int>(cx+x,cy+y);
    }
   }
  }
 }

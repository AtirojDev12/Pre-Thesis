using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ToiletZoneChecks
{
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int count;
 static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
 static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
 static void Check(bool ok,string n){if(!ok)throw new Exception(n);count++;Debug.Log("PASS "+n);}
 static void Tick(params ToiletLight[] rooms){foreach(var r in rooms)Call(r,"Update");}
 public static void Run(){try{
 EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
 var manager=UnityEngine.Object.FindFirstObjectByType<BuildingLightManager>();
 Call(manager.GetComponent<NetworkIdentity>(),"InitializeNetworkBehaviours");Call(manager,"Awake");manager.RefreshLights();
 var rooms=UnityEngine.Object.FindObjectsByType<ToiletLight>(FindObjectsSortMode.None);
 Check(rooms.Length==2,"Two room controllers");
 foreach(var r in rooms){Call(r.GetComponent<NetworkIdentity>(),"InitializeNetworkBehaviours");Call(r.GetComponent<RuleInteractable>(),"Awake");Call(r,"Awake");Call(r,"Start");}
 var men=rooms.Single(r=>(string)Get(r,"buildingZoneId")=="MaleToilet");
 var women=rooms.Single(r=>(string)Get(r,"buildingZoneId")=="FemaleToilet");
 var mz=manager.Zones.Single(z=>z.zoneId=="MaleToilet"); var wz=manager.Zones.Single(z=>z.zoneId=="FemaleToilet");
 var ml=mz.lightRoots.SelectMany(t=>t.GetComponentsInChildren<Light>(true)).ToArray();
 var wl=wz.lightRoots.SelectMany(t=>t.GetComponentsInChildren<Light>(true)).ToArray();
 Check(ml.Length>0&&wl.Length>0&&!ml.Intersect(wl).Any(),"Separate authored fixture groups");
 Check(ml.All(l=>l.transform.position.x < -25)&&wl.All(l=>l.transform.position.x < -25),"Room zones point to the real toilets, not hallway fixtures");
 var mm=((GameObject[])Get(men,"lamps")).SelectMany(o=>o.GetComponentsInChildren<Light>(true)).ToArray();
 var wm=((GameObject[])Get(women,"lamps")).SelectMany(o=>o.GetComponentsInChildren<Light>(true)).ToArray();
 Check(mm.Length==5&&wm.Length==5,"All mood lamps resolve");
 Check(mm.Concat(wm).All(l=>l.GetComponentInParent<IndependentLight>(true)!=null),"Mood lamps keep independent flicker ownership");
 men.ServerSet(false);Tick(rooms);
 Check(men.IsDark&&!women.IsDark,"Men switch changes only men's darkness state");
 Check(ml.All(l=>!l.enabled)&&mm.All(l=>!l.enabled),"Men switch turns off fixtures and mood lamps");
 Check(wl.All(l=>l.enabled),"Women's fixtures stay on");
 Check(!manager.LightsOnAt(mz.areaCenter)&&manager.LightsOnAt(wz.areaCenter),"Position-based darkness remains separate");
 Check(men.GetComponent<RuleInteractable>().GetInteractionPrompt().Contains("Turn light on"),"Off switch offers turn on");
 women.ServerSet(false);Tick(rooms);
 Check(wl.All(l=>!l.enabled)&&wm.All(l=>!l.enabled),"Women switch turns off its fixtures and mood lamps");
 men.ServerSet(true);Tick(rooms);
 Check(!men.IsDark&&women.IsDark&&ml.All(l=>l.enabled)&&wl.All(l=>!l.enabled),"Turning men on does not restore women");
 manager.SetZoneLights("MaleToilet",false);Tick(rooms);
 Check(men.IsDark&&mm.All(l=>!l.enabled),"External zone changes also update room rules and mood lamps");
 manager.SetZoneLights("HallwayMaleToilet",false);manager.SetZoneLights("HallwayFemaleToilet",false);
 men.ServerSet(true);women.ServerSet(true);Tick(rooms);
 Check(!men.IsDark&&!women.IsDark,"Hallway switches do not affect the two room switches");
 manager.SetLights(false);Tick(rooms);
 Check(mm.Concat(wm).All(l=>!l.enabled),"Building power off also darkens linked mood lamps");
 Check(!men.IsDark&&!women.IsDark,"Building outage preserves both room switch positions");
 manager.SetLights(true);Tick(rooms);
 Check(ml.Concat(wl).All(l=>l.enabled),"Restoring building power respects the room switches");
 Call(men,"OnPressed",(object)null);Tick(rooms);
 Check(men.IsDark&&!women.IsDark,"E action toggles its own room");
 // Validate the late-join path using Mirror's woven initial serializer.
 typeof(NetworkServer).GetProperty("active").SetValue(null,true);
 typeof(NetworkIdentity).GetProperty("isServer").SetValue(manager.netIdentity,true);
 manager.SetZoneLights("MaleToilet",false);manager.SetZoneLights("FemaleToilet",true);
 var writer=new NetworkWriter();manager.OnSerialize(writer,true);
 var guest=new GameObject("Guest zone state",typeof(NetworkIdentity),typeof(BuildingLightManager)).GetComponent<BuildingLightManager>();
 Call(guest.GetComponent<NetworkIdentity>(),"InitializeNetworkBehaviours");
 guest.GetType().GetField("zones",F).SetValue(guest,manager.Zones.ToArray());
 guest.OnDeserialize(new NetworkReader(writer.ToArraySegment()),true);
 typeof(NetworkServer).GetProperty("active").SetValue(null,false);
 typeof(NetworkClient).GetField("connectState",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,ConnectState.Connected);
 Check(!guest.ZoneLightsOn("MaleToilet")&&guest.ZoneLightsOn("FemaleToilet"),"Late join preserves separate room states");
 Check(!guest.SetZoneLights("MaleToilet",true),"Guest cannot directly change authoritative room state");
 typeof(NetworkClient).GetField("connectState",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,ConnectState.None);
 File.WriteAllText("toilet-zone-results.txt","PASS "+count+" checks\n");EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText("toilet-zone-results.txt","FAIL "+e+"\n");Debug.LogException(e);EditorApplication.Exit(1);}}
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;
using Object = UnityEngine.Object;
[InitializeOnLoad]
public static class TicketWallMotionRunner
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> log = new List<string>();
    static int phase, frames;
    static float started;
    static TicketGhostSpawner spawner;
    static TicketPunisherGhost ghost;
    static TicketMinigame game;
    static PlayerHealth seller;
    static Vector3 hidden, first;
    static bool moving;
    static TicketWallMotionRunner()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("WallMotion", false))
            { EditorApplication.update += Tick; EditorApplication.update += EditorApplication.Step; }
        };
    }
    static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static void Set(object o, string name, object v) => o.GetType().GetField(name, Flags).SetValue(o, v);
    static void Check(bool pass, string label) { log.Add((pass ? "PASS " : "FAIL ") + label); }
    public static void Build()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        SessionState.SetBool("WallOffline", false);
        SessionState.SetBool("WallMotion", true);
        EditorApplication.EnterPlaymode();
    }
    public static void BuildOffline()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        SessionState.SetBool("WallOffline", true);
        SessionState.SetBool("WallMotion", true);
        EditorApplication.EnterPlaymode();
    }
    static void SellWrong(int round)
    {
        seller.transform.position = ((TicketSellButton)Get(game,"humanButton")).transform.position;
        Set(game,"state",new TicketCustomerState { stage=TicketCustomerStage.Waiting, round=round,
            ghost=false, hasMovie=true, movieIndex=0, requestedMovieIndex=0 });
        int events=0;
        Action<PlayerHealth,bool> onSale=(who,correct)=>{events++;log.Add("INFO event seller="+who.name+" correct="+correct);};
        game.SaleResolved+=onSale;
        log.Add("INFO sale before active="+game.isActiveAndEnabled+" dead="+seller.IsDead+" down="+seller.IsDowned+" huntable="+PlayerRegistry.IsHuntable(seller)+" untargetable="+seller.DevUntargetable+" ghostServer="+ghost.isServer+" stage="+game.State.stage);
        game.ResolveSale(true, round, seller);
        log.Add("INFO sale after events="+events+" stage="+game.State.stage+" count="+Get(ghost,"currentWrongCount"));
        game.SaleResolved-=onSale;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (++frames > 1500) throw new Exception("Motion test timed out");
            if (phase == 0)
            {
                if (frames < 8) return;
                // Start a real Mirror server so scene identities and the actual spawner follow their shipping lifecycle.
                if (SessionState.GetBool("WallOffline", false)) { phase=1; frames=0; return; }
                var transport = new GameObject("Motion test transport").AddComponent<TelepathyTransport>();
                Transport.active = transport;
                NetworkServer.Listen(4);
                NetworkServer.SpawnObjects();
                phase=1; frames=0; return;
            }
            if (phase == 1)
            {
                if (frames < 12) return;
                spawner=Object.FindObjectsByType<TicketGhostSpawner>(FindObjectsInactive.Include).First();
                game=(TicketMinigame)Get(spawner,"minigame");
                ghost=(TicketPunisherGhost)Get(spawner,"currentGhost");
                Check((NetworkMode.IsOffline || spawner.isServer) && (bool)Get(spawner,"subscribed"), "Actual scene spawner subscribed with authority");
                Check(ghost != null, "Actual scene spawner created its ghost");
                if (ghost == null) { Finish(); return; }
                var playerObject=new GameObject("Motion test seller", typeof(NetworkIdentity), typeof(PlayerHealth));
                seller=playerObject.GetComponent<PlayerHealth>();
                foreach(var field in typeof(PlayerHealth).GetFields())
                    if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType))
                        field.SetValue(seller,Activator.CreateInstance(field.FieldType));
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Flags)
                    .Invoke(playerObject.GetComponent<NetworkIdentity>(), null);
                if (NetworkServer.active) NetworkServer.Spawn(playerObject);
                hidden=ghost.transform.position;
                log.Add("INFO hidden="+hidden+" scale="+Time.timeScale+" active="+ghost.isActiveAndEnabled);
                phase=2; frames=0; return;
            }
            if (phase == 2)
            {
                if(frames<3)return;
                if (SessionState.GetBool("WallOffline", false))
                {
                    seller.DevGodMode=true;
                    seller.ServerDevSetInfiniteHealth(true);
                    seller.DevUntargetable=true;
                }
                SellWrong(10001);
                Check((int)Get(ghost,"currentWrongCount")==1,"Real accepted wrong sale reaches ghost");
                started=Time.time; phase=3; return;
            }
            if (phase == 3)
            {
                float elapsed=Time.time-started;
                if(elapsed>0.05f && elapsed<0.7f && Vector3.Distance(hidden,ghost.transform.position)>0.001f) moving=true;
                if(elapsed<1.1f)return;
                first=ghost.transform.position;
                log.Add("INFO afterFirst="+first+" elapsed="+elapsed+" moving="+moving);
                Check(moving,"Ghost really moves across frames with default duration");
                Check(first.x < hidden.x-0.2f,"Wrong sale moves ghost toward booth");
                var wall=(Collider)Get(spawner,"appearanceWall");
                Check(ghost.GetComponent<Renderer>().bounds.min.x < wall.bounds.min.x,"Live motion crosses Cube (5) inner surface");
                SellWrong(10002); started=Time.time; phase=4; return;
            }
            if(phase==4)
            {
                if(Time.time-started<1.1f)return;
                Check(ghost.transform.position.x<first.x-0.2f,"Second real wrong sale continues emergence");
                log.Add("INFO afterSecond="+ghost.transform.position);
                // Repeat with invulnerability/untargetability used during development.
                seller.DevUntargetable=true;
                SellWrong(10003);
                Check((int)Get(ghost,"currentWrongCount")==3,"God Mode still advances appearance on real wrong sales");
                Check(seller.CurrentHealth==100f && !(bool)Get(ghost,"isAttackTriggered"),"God Mode still blocks attack and damage");
                Finish();
            }
        }
        catch(Exception e){log.Add("FAIL "+e);Finish();}
    }
    static void Finish()
    {
        SessionState.SetBool("WallMotion",false);
        File.WriteAllLines(SessionState.GetBool("WallOffline",false) ? "ticket-wall-offline-results.txt" : "ticket-wall-motion-results.txt",log);
        EditorApplication.update-=Tick;
        EditorApplication.Exit(log.Any(x=>x.StartsWith("FAIL "))?1:0);
    }
}

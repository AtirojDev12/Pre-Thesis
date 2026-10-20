using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class GhostFavorTrailChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static Camera camera;
    static Material visualMaterial;
    static RenderTexture target;
    static int ticks;
    static void Check(bool pass, string message)
    {
        results.Add((pass ? "PASS " : "FAIL ") + message);
        Debug.Log(results[results.Count - 1]);
        if (!pass) throw new Exception(message);
    }
    static object Call(object who, string method, params object[] args) => who.GetType().GetMethod(method, Private).Invoke(who, args);
    static Transform Point(string name, Vector3 position)
    {
        var go = new GameObject(name); go.transform.position = position; return go.transform;
    }
    static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.position = position; go.transform.localScale = size; return go;
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Use the isolated test project.");
        try
        {
            NetworkMode.SessionEnding = false;
            SyntheticChecks();
            SceneChecks();
            BeginRenderChecks();
        }
        catch (Exception error) { Finish(error); }
    }
    static void SyntheticChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Vector3 origin = new Vector3(2000, 0, 2000);
        GameObject floor = Box("Floor", origin - Vector3.up * .1f, new Vector3(22, .2f, 22));
        GameObject wall = Box("Blocking wall", origin + Vector3.up, new Vector3(1, 2, 8));
        Physics.SyncTransforms();
        var sources = new List<NavMeshBuildSource>();
        foreach (GameObject go in new[] { floor, wall }) sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box, size = Vector3.one,
            transform = go.transform.localToWorldMatrix, area = 0
        });
        var build = NavMesh.GetSettingsByID(0);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(build, sources, new Bounds(origin, new Vector3(28, 8, 28)), Vector3.zero, Quaternion.identity);
        Check(data != null, "Synthetic walkable NavMesh builds");
        NavMeshDataInstance handle = NavMesh.AddNavMeshData(data);
        try
        {
            var settings = new GhostFavorTrailSettings();
            Vector3 home = origin + new Vector3(-6, .6f, 0), destination = origin + new Vector3(6, .6f, 0);
            Check(GhostFavorTrailPath.TryCalculate(home, destination, null, null, null, settings, out Vector3[] route),
                "Complete route is found around a wall");
            Check(route.Length > 2 && GhostFavorTrailPath.Length(route) > 13,
                "Route detours instead of painting a straight line through the wall");
            Check(!GhostFavorTrailPath.TryCalculate(home, origin + new Vector3(100, .6f, 0), null, null, null, settings, out _),
                "Unreachable destination is rejected");
            Check(!GhostFavorTrailPath.TryFloor(origin + Vector3.up * 4, null, settings, out _, out _),
                "Floor sampling does not project onto a distant lower floor");

            GameObject system = new GameObject("Recovery + trail");
            var trail = system.AddComponent<GhostFavorTrail>();
            var recovery = system.AddComponent<GhostFavorRecovery>();
            Transform item = Point("Ghost Flavor", home); item.gameObject.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            Transform good = Point("Reachable", destination), bad = Point("Unreachable", origin + Vector3.right * 100);
            recovery.Configure(item, new[] { bad, null, good });
            recovery.ServerOnGhostAppeared();
            Check(!recovery.IsHome && (recovery.VisibleState.position - destination).sqrMagnitude < .01f,
                "Existing relocation list selects the reachable point and skips null/unreachable points");
            GhostFavorTrailSnapshot first = trail.VisibleSnapshot;
            Check(first.active && first.corners.Length > 1, "Relocation publishes a route immediately");
            recovery.ServerOnGhostAppeared();
            Check(trail.VisibleSnapshot.revision == first.revision, "Repeated ghost arrival does not duplicate an unfinished trail");

            var player = new GameObject("Carrier"); player.AddComponent<NetworkIdentity>();
            var health = player.AddComponent<PlayerHealth>(); player.transform.position = destination;
            recovery.ServerInteract(health, false);
            Call(trail, "UpdateAuthority", 10d);
            Check(recovery.IsCarried && recovery.VisibleState.carrierId == 0 && !trail.VisibleSnapshot.active,
                "Offline carrier (netId zero) correctly pauses the trail");
            Call(trail, "UpdateVisuals", 10d);
            var pausedMaterial = (Material)typeof(GhostFavorTrail).GetField("localMaterial", Private).GetValue(trail);
            Check(pausedMaterial != null && pausedMaterial.GetFloat("_SweepIntensity") == 0,
                "Picking up stops the light sweep immediately while old blood fades");
            recovery.ServerInteract(health, true);
            Vector3 dropped = destination + Vector3.forward * 3;
            item.position = dropped;
            var body = item.GetComponent<Rigidbody>(); body.position = dropped; body.linearVelocity = Vector3.zero; body.Sleep();
            Physics.SyncTransforms();
            Call(recovery, "Update");
            Call(trail, "UpdateAuthority", 11d);
            Call(trail, "UpdateAuthority", 11.2d);
            Check(!trail.VisibleSnapshot.active, "Dropped bottle waits for the settle delay");
            Call(trail, "UpdateAuthority", 11.8d);
            GhostFavorTrailSnapshot afterDrop = trail.VisibleSnapshot;
            Check(afterDrop.active && afterDrop.revision > first.revision &&
                Vector3.Distance(afterDrop.corners[afterDrop.corners.Length - 1], dropped) <= trail.Settings.pickupReach,
                "Settled drop replaces the route with the new endpoint");

            // Simulate a navigation update closing the corridor, then reopening it.
            var closedSources = new List<NavMeshBuildSource>(sources);
            closedSources[1] = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(1,2,28), transform = Matrix4x4.TRS(origin + Vector3.up, Quaternion.identity, Vector3.one), area = 0 };
            NavMeshData closedData = NavMeshBuilder.BuildNavMeshData(build, closedSources,
                new Bounds(origin, new Vector3(28,8,28)), Vector3.zero, Quaternion.identity);
            handle.Remove(); handle = NavMesh.AddNavMeshData(closedData);
            try
            {
                Call(trail, "UpdateAuthority", 14d);
                Check(!trail.VisibleSnapshot.active, "A blocked NavMesh route hides the old guide");
            }
            finally { handle.Remove(); Object.DestroyImmediate(closedData); handle = NavMesh.AddNavMeshData(data); }
            Call(trail, "UpdateAuthority", 17d);
            Check(trail.VisibleSnapshot.active, "Periodic retry restores the guide when the corridor reopens");

            var wire = new GameObject("Server snapshot"); wire.AddComponent<NetworkIdentity>();
            var sync = wire.AddComponent<PopcornNetSync>(); sync.PublishGhostTrail(afterDrop);
            var writer = new NetworkWriter(); sync.OnSerialize(writer, true);
            var guestGo = new GameObject("Late joiner"); guestGo.AddComponent<NetworkIdentity>();
            var guest = guestGo.AddComponent<PopcornNetSync>(); guest.OnDeserialize(new NetworkReader(writer.ToArraySegment()), true);
            Check(guest.GhostTrail.active && guest.GhostTrail.revision == afterDrop.revision &&
                guest.GhostTrail.corners.Length == afterDrop.corners.Length && guest.GhostTrail.pulseEpoch == afterDrop.pulseEpoch,
                "Mirror initial snapshot preserves route, activity and sweep clock for a late joiner");
            Object.DestroyImmediate(guestGo); Object.DestroyImmediate(wire);

            trail.Settings.maximumMarks = 32; trail.Settings.marksPerMesh = 8; trail.Settings.spacing = .3f;
            Call(trail, "BuildMeshes", first);
            int pool = trail.PooledChunkCount;
            Check(trail.VisibleMarkCount > 0 && trail.VisibleMarkCount <= 32 && pool <= 4,
                "Mark count and mesh chunk budget are bounded");
            Call(trail, "BuildMeshes", afterDrop);
            Check(trail.PooledChunkCount <= 4 && trail.VisibleMarkCount <= 32, "Rebuilding reuses pooled mesh chunks");

            player.transform.position = dropped; recovery.ServerInteract(health, false);
            player.transform.position = home; Call(recovery, "Update"); Call(trail, "UpdateAuthority", 13d);
            Check(recovery.IsHome && !trail.VisibleSnapshot.active && trail.VisibleSnapshot.corners == null,
                "Returning home clears the shared route");
            Call(recovery, "LateUpdate");
            recovery.Configure(item, new[] { bad }); recovery.ServerOnGhostAppeared();
            Check(recovery.IsHome, "When all destinations are unreachable the item stays home");
            Object.DestroyImmediate(system); Object.DestroyImmediate(player); Object.DestroyImmediate(item.gameObject);
        }
        finally { handle.Remove(); Object.DestroyImmediate(data); }
    }
    static void SceneChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        foreach (NavMeshSurface surface in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include)) surface.AddData();
        var bootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var trail = bootstrap.GetComponent<GhostFavorTrail>();
        Check(trail != null, "Cinema scene exposes Ghost Favor Trail settings on Popcorn Minigame Systems");
        Transform home = (Transform)typeof(PopcornMinigameBootstrap).GetField("ghostStation", Private).GetValue(bootstrap);
        Transform[] points = (Transform[])typeof(PopcornMinigameBootstrap).GetField("ghostFavorRelocationPoints", Private).GetValue(bootstrap);
        var recovery = bootstrap.gameObject.AddComponent<GhostFavorRecovery>(); recovery.Configure(home, points);
        int reachable = 0;
        foreach (Transform point in points)
        {
            if (point == null) continue;
            bool found = trail.TryPlanDestination(point, out Vector3[] route);
            Debug.Log($"Scene route {home.position} -> {point.position}: {found}");
            if (found) { reachable++; Call(trail, "BuildMeshes", new GhostFavorTrailSnapshot { corners = route, seed = 4 }); }
        }
        Check(reachable > 0, "At least one existing authored relocation point is reachable on the baked Cinema NavMesh");
        Check(trail.VisibleMarkCount > 0, "Authored Cinema floors accept blood marks");
        Shader shader = Shader.Find("PreThesis/VFX/Ghost Favor Blood Trail");
        Check(shader != null && !ShaderUtil.ShaderHasError(shader), "URP blood/sweep shader imports without errors");
    }
    static void BeginRenderChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/GhostTrailTestRenderer.asset");
        if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, "Assets/GhostTrailTestRenderer.asset"); }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/GhostTrailTestPipeline.asset");
        bool newPipeline = pipeline == null;
        if (newPipeline) pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_RendererDataList").arraySize = 1;
        serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (newPipeline) AssetDatabase.CreateAsset(pipeline, "Assets/GhostTrailTestPipeline.asset");
        pipeline.msaaSampleCount = 1; GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        camera = new GameObject("Render Camera").AddComponent<Camera>();
        camera.transform.position = Vector3.up * 3; camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        camera.orthographic = true; camera.orthographicSize = .7f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f,.06f,.06f,1);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        target = new RenderTexture(256, 256, 24); target.Create();
        var quad = new GameObject("Blood stamp", typeof(MeshFilter), typeof(MeshRenderer));
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(.5f,0,.5f), new Vector3(-.5f,0,.5f) },
            normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
            uv2 = new[] { new Vector2(.5f,.4f), new Vector2(.5f,.4f), new Vector2(.5f,.4f), new Vector2(.5f,.4f) },
            triangles = new[] { 0,2,1,0,3,2 }
        };
        quad.GetComponent<MeshFilter>().sharedMesh = mesh;
        visualMaterial = new Material(Resources.Load<Material>("GhostFavorTrail"));
        visualMaterial.SetFloat("_RouteLength", 10); visualMaterial.SetFloat("_Opacity", 1);
        quad.GetComponent<MeshRenderer>().sharedMaterial = visualMaterial;
        EditorApplication.update += RenderTick;
    }
    static Color32[] Capture(string file)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(256,256,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,256,256),0,0); image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../ghost-trail-" + file + ".png"), image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32(); Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
    }
    static void RenderTick()
    {
        if (++ticks < 12) return;
        EditorApplication.update -= RenderTick;
        try
        {
            visualMaterial.SetFloat("_TrailTime",5); Color32[] rest = Capture("rest");
            visualMaterial.SetFloat("_TrailTime",.75f); Color32[] sweep = Capture("sweep");
            int changed = 0, blood = 0;
            for (int i = 0; i < rest.Length; i++) { if (sweep[i].r > rest[i].r + 20) changed++; if (rest[i].r > rest[i].g + 5) blood++; }
            Check(blood > 200 && changed > 200, "Blood remains visible at rest and automatic sweep brightens its surface");
            var blocker = Box("Occluder", Vector3.up * .5f, new Vector3(1.3f,.1f,1.3f));
            var blockMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); blockMaterial.SetColor("_BaseColor", Color.gray);
            blocker.GetComponent<Renderer>().sharedMaterial = blockMaterial;
            visualMaterial.SetFloat("_TrailTime",5); Color32[] hiddenRest = Capture("occluded-rest");
            visualMaterial.SetFloat("_TrailTime",.75f); Color32[] hiddenSweep = Capture("occluded-sweep");
            changed = 0;
            for (int i = 0; i < rest.Length; i++) if (Math.Abs(hiddenRest[i].r - hiddenSweep[i].r) > 5) changed++;
            Check(changed < 20, "Sweep is depth-tested and does not shine through solid geometry");
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        EditorApplication.update -= RenderTick;
        if (error != null) { results.Add("FAIL " + error); Debug.LogException(error); }
        File.WriteAllLines(Path.GetFullPath("../ghost-trail-results.txt"), results);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}

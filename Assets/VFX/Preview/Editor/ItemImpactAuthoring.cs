using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PreThesis.Vfx;
using Object = UnityEngine.Object;

/// <summary>Explicit authoring entry point. Run in an isolated copy, then review/copy the new assets.</summary>
[InitializeOnLoad]
public static class ItemImpactAuthoring
{
    private const string Root = "Assets/Resources/ItemImpact";
    private const string Preview = "Assets/VFX/Preview/Prefabs/ItemImpactPreview.prefab";
    private static readonly System.Collections.Generic.HashSet<ItemImpactVfx> observed = new System.Collections.Generic.HashSet<ItemImpactVfx>();
    private static int runtimePhase;
    private static float phaseStarted;
    private static bool runtimeStarted;
    private static int offlineCount;
    private static int firstDropCount;
    private static float normalImpactSize;
    private static bool largeImpactSeen;
    private static float firstImpactAt = -1;
    private static bool capturedContact;
    private static Mirror.NetworkManager testNetwork;
    private static double runtimeDeadline;

    static ItemImpactAuthoring()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("ItemImpactChecks", false)) return;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = new System.Collections.Generic.List<UnityEngine.LowLevel.PlayerLoopSystem>(loop.subSystemList);
            systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(ItemImpactAuthoring), updateDelegate = RuntimeTick });
            loop.subSystemList = systems.ToArray();
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += EditorApplication.Step;
        };
    }

    public static void Build()
    {
        try
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/ItemImpact.prefab") != null)
                throw new InvalidOperationException("Impact assets already exist; refusing to overwrite.");
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var shader = Shader.Find("PreThesis/VFX/Item Impact");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Impact shader failed");
            var materials = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                materials[i] = new Material(shader) { name = new[] { "ContactDust", "ShortStreak", "BrokenAirRing" }[i] };
                materials[i].SetFloat("_Shape", i);
                materials[i].SetFloat("_Distortion", i == 2 ? .001f : 0);
                AssetDatabase.CreateAsset(materials[i], Root + "/" + materials[i].name + ".mat");
            }
            var effect = new GameObject("ItemImpact", typeof(MeshFilter), typeof(MeshRenderer));
            effect.GetComponent<MeshRenderer>().sharedMaterials = materials;
            effect.AddComponent<ItemImpactVfx>();
            PrefabUtility.SaveAsPrefabAsset(effect, Root + "/ItemImpact.prefab");
            Object.DestroyImmediate(effect);
            MakePreview();
            foreach (string scenePath in new[] { "Assets/VFX/VFX_Preview.unity", "Assets/VFX/GhostManifestation/VFX_Preview.unity" })
            {
                var scene = EditorSceneManager.OpenScene(scenePath);
                var switcher = Object.FindAnyObjectByType<VfxPreviewSwitcher>();
                if (switcher == null) throw new Exception("Missing switcher: " + scenePath);
                var stage = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Preview));
                switcher.impactStage = stage;
                switcher.impact = stage.GetComponent<ItemImpactPreview>();
                stage.SetActive(false);
                EditorUtility.SetDirty(switcher);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            VerifyBatch();
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("item-impact-results.txt", "FAIL " + e); EditorApplication.Exit(1); }
    }

    private static void MakePreview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Item drop impact preview");
        var controls = root.AddComponent<ItemImpactPreview>();
        var dark = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "ImpactFloorDark" };
        dark.SetColor("_BaseColor", new Color(.12f, .14f, .15f)); dark.SetFloat("_Smoothness", .15f);
        AssetDatabase.CreateAsset(dark, Root + "/ImpactFloorDark.mat");
        var light = new Material(dark) { name = "ImpactFloorLight" };
        light.SetColor("_BaseColor", new Color(.24f, .25f, .26f));
        AssetDatabase.CreateAsset(light, Root + "/ImpactFloorLight.mat");
        for (int x = -5; x < 5; x++) for (int z = -5; z < 5; z++)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "Dry floor tile";
            tile.transform.SetParent(root.transform, false);
            tile.transform.localPosition = new Vector3((x + .5f) * .2f, -.025f, (z + .5f) * .2f);
            tile.transform.localScale = new Vector3(.2f, .05f, .2f);
            tile.GetComponent<MeshRenderer>().sharedMaterial = (x + z) % 2 == 0 ? dark : light;
            Object.DestroyImmediate(tile.GetComponent<Collider>());
        }
        var floor = root.AddComponent<BoxCollider>(); floor.center = new Vector3(0, -.025f, 0); floor.size = new Vector3(2, .05f, 2);
        controls.itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Items/WorldFlashlight.prefab");
        var cameraObject = new GameObject("Impact preview camera", typeof(Camera));
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(.65f, .55f, -.85f);
        cameraObject.transform.LookAt(new Vector3(0, .08f, 0));
        var camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView = 42; camera.nearClipPlane = .01f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .03f, .04f);
        camera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
        var lamp = new GameObject("Impact preview light", typeof(Light));
        lamp.transform.SetParent(root.transform, false); lamp.transform.rotation = Quaternion.Euler(48, -32, 0);
        lamp.GetComponent<Light>().type = LightType.Directional; lamp.GetComponent<Light>().intensity = 1.3f;
        PrefabUtility.SaveAsPrefabAsset(root, Preview);
        Object.DestroyImmediate(root);
    }

    public static void VerifyBatch()
    {
        try
        {
            Verify();
            SessionState.SetBool("ItemImpactChecks", true);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ItemImpactTest.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("item-impact-results.txt", "FAIL " + e); EditorApplication.Exit(1); }
    }

    public static void Verify()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Preview));
        var controls = root.GetComponent<ItemImpactPreview>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/ItemImpact.prefab");
        var fx = Object.Instantiate(prefab).GetComponent<ItemImpactVfx>();
        fx.Play(1, 42); fx.manualTime = true;
        var mesh = fx.GetComponent<MeshFilter>().sharedMesh;
        Directory.CreateDirectory("ImpactRenders");
        foreach (float t in new[] { 0f, .055f, .12f, .18f, .27f, .35f, .5f })
        {
            fx.Sample(t);
            var colors = mesh.colors;
            if (t >= .12f) foreach (int i in mesh.GetTriangles(1)) Require(colors[i].a == 0, "Streak remained after .12s");
            if (t >= .27f) foreach (int i in mesh.GetTriangles(2)) Require(colors[i].a == 0, "Ring remained after .27s");
            if (t == .35f) Require(Array.Exists(colors, color => color.a > .01f), "Dust faded before the extended tail");
            if (t >= .5f) foreach (var color in colors) Require(color.a == 0, "Effect remained after .5s");
            Capture(root.GetComponentInChildren<Camera>(), "ImpactRenders/impact-" + t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ".png");
        }
        fx.Sample(.18f);
        Require(mesh.bounds.size.y > .18f, "Smoke volume has insufficient vertical depth");
        var camera = root.GetComponentInChildren<Camera>();
        var positions = new[] { new Vector3(.65f, .2f, -.85f), new Vector3(-.85f, .35f, .4f), new Vector3(.01f, 1, -.2f) };
        for (int view = 0; view < positions.Length; view++)
        {
            camera.transform.position = positions[view];
            camera.transform.LookAt(new Vector3(0, .08f, 0));
            Capture(camera, "ImpactRenders/smoke-view-" + view + ".png");
        }
        fx.transform.rotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0, 1, 1).normalized);
        fx.Sample(.055f);
        Require(Vector3.Dot(fx.transform.up, new Vector3(0, 1, 1).normalized) > .999f, "Surface alignment failed");
        Object.DestroyImmediate(fx.gameObject);
        // Physical callback checks run in Play mode, where Unity dispatches normal MonoBehaviour contacts.
        controls.autoReplay = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(controls);
        foreach (var material in AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/ItemImpact.prefab").GetComponent<MeshRenderer>().sharedMaterials)
            Require(!ShaderUtil.ShaderHasError(material.shader), "Shader compile error");
        foreach (string scenePath in new[] { "Assets/VFX/VFX_Preview.unity", "Assets/VFX/GhostManifestation/VFX_Preview.unity" })
        {
            EditorSceneManager.OpenScene(scenePath);
            var switcher = Object.FindAnyObjectByType<VfxPreviewSwitcher>();
            Require(switcher != null && switcher.impactStage != null && switcher.impact != null, "Missing impact stage: " + scenePath);
            Require(switcher.impact.itemPrefab != null, "Missing physical preview item");
            Require(switcher.impactStage.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length == 0, "Scene identity would prevent offline preview");
            switcher.ShowImpact();
            Require(switcher.impactStage.activeSelf && !switcher.foodStage.activeSelf && !switcher.ghostStage.activeSelf && !switcher.favorStage.activeSelf,
                "Stage switching failed");
            switcher.impact.autoReplay = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(switcher.impact);
            PrefabUtility.RecordPrefabInstancePropertyModifications(switcher.impactStage);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("item-impact-results.txt", "PASS timeline, shader and slope alignment; runtime checks pending.\n");
    }

    private static void RuntimeTick()
    {
        if (!SessionState.GetBool("ItemImpactChecks", false) || !EditorApplication.isPlaying || !Application.isPlaying) return;
        try
        {
            if (!runtimeStarted) { runtimeStarted = true; phaseStarted = Time.time; runtimeDeadline = EditorApplication.timeSinceStartup + 45; }
            Require(EditorApplication.timeSinceStartup < runtimeDeadline, "Runtime checks timed out");
            var rig = Object.FindAnyObjectByType<ItemImpactPreview>();
            if (runtimePhase == 4)
            {
                // Batch stepping advances game time faster than Mirror's wall-clock send interval.
                // Wait for the observer to be ready before replaying the host collision.
                if (!Mirror.NetworkClient.isConnected || Mirror.NetworkServer.localConnection == null ||
                    !Mirror.NetworkServer.localConnection.isReady) return;
                Mirror.NetworkServer.RebuildObservers(rig.item.GetComponent<Mirror.NetworkIdentity>(), true);
                Require(rig.item.GetComponent<Mirror.NetworkIdentity>().observers.Count == 1, "Host observer missing");
                rig.Replay();
                runtimePhase = 5; phaseStarted = Time.time;
            }
            foreach (var impact in Object.FindObjectsByType<ItemImpactVfx>())
            {
                if (observed.Add(impact))
                {
                    Require(runtimePhase == 0 || runtimePhase == 2 || runtimePhase == 5, "Resting or water contact emitted an impact in phase " + runtimePhase);
                    Require(Mathf.Abs(impact.transform.position.y - .003f) < .025f, "Impact missed the real contact surface");
                    Vector3 itemCenter = rig.item.GetComponent<Collider>().bounds.center;
                    Require(Vector3.ProjectOnPlane(impact.transform.position - itemCenter, Vector3.up).magnitude < .08f,
                        "Impact was not centred beneath the item");
                    if (runtimePhase == 0) normalImpactSize = impact.size;
                    if (runtimePhase == 2)
                    {
                        Require(impact.size > normalImpactSize * 1.4f, "Large item did not enlarge the effect");
                        largeImpactSeen = true;
                        Capture(rig.GetComponentInChildren<Camera>(), "ImpactRenders/large-item-contact.png");
                    }
                    if (firstImpactAt < 0) firstImpactAt = Time.time;
                }
            }
            if (!capturedContact && firstImpactAt >= 0 && Time.time - firstImpactAt >= .05f)
            {
                Capture(rig.GetComponentInChildren<Camera>(), "ImpactRenders/physical-contact.png");
                capturedContact = true;
            }
            if (Time.time - phaseStarted < 1.5f) return;
            if (runtimePhase == 0)
            {
                Require(observed.Count > 0, "Actual flashlight floor collision did not emit");
                firstDropCount = observed.Count;
                Require(Object.FindObjectsByType<ItemImpactVfx>().Length == 0, "Effect outlived .5 seconds");
                runtimePhase = 1; phaseStarted = Time.time;
            }
            else if (runtimePhase == 1)
            {
                rig.item.transform.localScale *= 2.5f;
                rig.Replay();
                runtimePhase = 2; phaseStarted = Time.time;
            }
            else if (runtimePhase == 2)
            {
                Require(largeImpactSeen, "Large item drop did not emit");
                rig.item.transform.localScale /= 2.5f;
                rig.gameObject.layer = 4; // Same physical drop onto Water must emit nothing.
                rig.Replay();
                runtimePhase = 3; phaseStarted = Time.time;
            }
            else if (runtimePhase == 3)
            {
                offlineCount = observed.Count;
                rig.gameObject.layer = 0;
                var network = new GameObject("Impact host check", typeof(Mirror.TelepathyTransport), typeof(Mirror.NetworkManager));
                testNetwork = network.GetComponent<Mirror.NetworkManager>();
                testNetwork.transport = network.GetComponent<Mirror.TelepathyTransport>();
                network.GetComponent<Mirror.TelepathyTransport>().port = 47825;
                testNetwork.autoCreatePlayer = false;
                rig.item.isKinematic = true;
                testNetwork.StartHost();
                Mirror.NetworkServer.Spawn(rig.item.gameObject);
                runtimePhase = 4; phaseStarted = Time.time;
            }
            else
            {
                Require(Mirror.NetworkClient.isConnected, "Host client did not connect");
                if (observed.Count == offlineCount) return; // Allow the real-time network batch to arrive.
                Require(observed.Count - offlineCount == firstDropCount, "Host RPC count mismatch: baseline=" + firstDropCount + ", host=" + (observed.Count - offlineCount));
                testNetwork.StopHost();
                SessionState.SetBool("ItemImpactChecks", false);
                File.WriteAllText("item-impact-results.txt", "PASS .5s timeline, shader, slope alignment, centred origin, automatic large-item scaling; Play-mode physical drops, cleanup, resting suppression, Water exclusion and real Mirror host RPC without duplicate effects. Physical impacts: " + observed.Count + "\n");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            SessionState.SetBool("ItemImpactChecks", false);
            Debug.LogException(e); File.WriteAllText("item-impact-results.txt", "FAIL " + e); EditorApplication.Exit(1);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void Capture(Camera camera, string path)
    {
        var target = new RenderTexture(960, 640, 24);
        camera.targetTexture = target; camera.Render(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous;
        Object.DestroyImmediate(image); Object.DestroyImmediate(target);
    }
}

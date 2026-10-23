using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering.Universal;
using PreThesis.Vfx;
using Object = UnityEngine.Object;

public static class GhostFavorPreviewAuthoring
{
    [Serializable] private class Bindings
    {
        public string guid;
        public long root;
        public long transform;
        public long script;
    }

    public static void Build()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/.utmp/"))
            throw new Exception("Use the isolated authoring project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var bootstrap = Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var original = (Transform)typeof(PopcornMinigameBootstrap).GetField("ghostStation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
        var jar = Object.Instantiate(original.gameObject);
        if (PrefabUtility.IsPartOfPrefabInstance(jar)) PrefabUtility.UnpackPrefabInstance(jar, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        jar.transform.SetParent(null, true);
        var stage = new GameObject("Ghost Favor Preview Stage");
        jar.transform.SetParent(stage.transform, true);
        foreach (var script in jar.GetComponentsInChildren<MonoBehaviour>()) Object.DestroyImmediate(script);
        foreach (var collider in jar.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
        foreach (var body in jar.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(body);
        Bounds bounds = BoundsOf(jar);
        jar.transform.localScale *= .6f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .001f);
        bounds = BoundsOf(jar);
        jar.transform.position += new Vector3(0, 1.02f - bounds.min.y, 0) - new Vector3(bounds.center.x, 0, bounds.center.z);
        bounds = BoundsOf(jar);
        var rig = stage.AddComponent<GhostFavorVfxPreview>();
        rig.jarRenderers = jar.GetComponentsInChildren<Renderer>();
        var effect = new GameObject("Ghost Favor - VFX Graph"); effect.transform.SetParent(stage.transform, false);
        effect.transform.position = bounds.center;
        effect.transform.localScale = Vector3.one * .6f;
        rig.smoke = effect.AddComponent<VisualEffect>();
        rig.smoke.initialEventName = "Idle";
        rig.smoke.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Resources/GhostFavorDisappear.vfx");
        rig.smoke.startSeed = 71; rig.smoke.resetSeedOnPlay = false;
        Box(stage.transform, "Seasoning counter", new Vector3(0,.5f,0), new Vector3(3.2f,1,1.8f), "Counter");
        Box(stage.transform, "Studio floor", new Vector3(0,-.08f,0), new Vector3(16,.1f,16), "StudioFloor");
        var camera = new GameObject("Ghost Favor Preview Camera").AddComponent<Camera>();
        camera.transform.SetParent(stage.transform, false);
        camera.tag = "MainCamera"; camera.transform.position = new Vector3(1.65f,2.2f,-3.1f);
        camera.transform.LookAt(bounds.center + Vector3.up * .12f);
        camera.fieldOfView = 43; camera.nearClipPlane = .03f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f,.19f,.21f);
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        var light = new GameObject("Ghost Favor key light").AddComponent<Light>();
        light.transform.SetParent(stage.transform, false);
        light.type = LightType.Directional; light.intensity = 2; light.transform.eulerAngles = new Vector3(35,-35,0);
        const string path = "Assets/VFX/Preview/Prefabs/GhostFavorPreview.prefab";
        PrefabUtility.SaveAsPrefabAsset(stage, path); AssetDatabase.SaveAssets();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var ids = new Bindings { guid = AssetDatabase.AssetPathToGUID(path) };
        string unused;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out unused, out ids.root);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform, out unused, out ids.transform);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.GetComponent<GhostFavorVfxPreview>(), out unused, out ids.script);
        File.WriteAllText(Path.GetFullPath("../ghost-favor-preview-bindings.json"), JsonUtility.ToJson(ids, true));
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static void Box(Transform parent, string name, Vector3 position, Vector3 scale, string material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/VFX/Preview/Materials/" + material + ".mat");
    }
}

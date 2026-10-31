using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using PreThesis.Vfx;

public static class GhostPoolAuthoring
{
    const string Root = "Assets/VFX/GhostPool";
    const string Ghost = "Assets/ModelThesis/Ghost Model/LO Ghost/Ghost Walk Anim.fbx";

    [MenuItem("Tools/Pre-Thesis/VFX/Open Black Water Preview")]
    public static void Open()
    {
        if (!EditorApplication.isPlaying && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Root + "/BlackWater_Preview.unity");
    }

    [MenuItem("Tools/Pre-Thesis/VFX/Build Black Water Preview")]
    public static void Build()
    {
        try
        {
            bool isolatedBatch=Application.isBatchMode && Application.dataPath.Replace('\\','/').Contains("/Temp/GhostPoolAuthoring/");
            if (File.Exists(Root + "/BlackWater_Preview.unity") && !isolatedBatch) throw new InvalidOperationException("Preview already exists; open the existing scene.");
            Directory.CreateDirectory(Root + "/Graphs");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            MakeGraph(false);
            MakeGraph(true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Materials/PreviewRenderer.asset");
                if(renderer==null) { renderer=ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer,Root + "/Materials/PreviewRenderer.asset"); }
                var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root + "/Materials/PreviewPipeline.asset");
                if(pipeline==null) { pipeline=UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline,Root + "/Materials/PreviewPipeline.asset"); }
                GraphicsSettings.defaultRenderPipeline=pipeline;
                QualitySettings.renderPipeline=pipeline;
            }
            var shader = Shader.Find("PreThesis/VFX/Black Water");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Black water shader failed to compile.");
            var water = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/BlackWater.mat");
            if(water==null) { water=new Material(shader) { name="BlackWater" }; AssetDatabase.CreateAsset(water,Root + "/Materials/BlackWater.mat"); }
            // Keep any unsaved gameplay scene open while authoring this separate preview.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.24f,.3f,.35f);
            RenderSettings.ambientEquatorColor = new Color(.12f,.14f,.17f);
            RenderSettings.ambientGroundColor = new Color(.025f,.025f,.03f);
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/PreviewFloor.mat");
            if(floorMat==null) { floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(floorMat,Root + "/Materials/PreviewFloor.mat"); }
            floorMat.SetColor("_BaseColor", new Color(.19f,.21f,.22f));
            floorMat.SetFloat("_Smoothness", .08f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview floor";
            floor.transform.position = new Vector3(0,-.13f,0);
            floor.transform.localScale = new Vector3(24,.2f,24);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            var rig = new GameObject("Black water preview controls").AddComponent<GhostPoolPreview>();
            rig.pools = new Renderer[2]; rig.bodies = new Transform[2]; rig.splashes = new VisualEffect[2];
            rig.ghostModels = new GameObject[2];
            rig.walk = AssetDatabase.LoadAllAssetsAtPath(Ghost).OfType<AnimationClip>().First(c=>c.name=="Walk");
            rig.entry = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath("2f24184795e946e449913a0ce6e62989"))
                .OfType<AnimationClip>().First(c => c.name == "Entry");
            for (int i = 0; i < 2; i++)
            {
                var stage = new GameObject(i == 0 ? "01 Emerge" : "02 Submerge");
                stage.transform.position = new Vector3(i == 0 ? -1.9f : 1.9f,0,0);
                var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pool.name = "Glossy black water";
                UnityEngine.Object.DestroyImmediate(pool.GetComponent<Collider>());
                pool.transform.SetParent(stage.transform,false);
                pool.transform.localPosition = new Vector3(0,.012f,-.35f);
                pool.transform.localRotation = Quaternion.Euler(90,0,0);
                // Quad local Y is the second surface axis.
                pool.GetComponent<Renderer>().sharedMaterial = water;
                rig.pools[i] = pool.GetComponent<Renderer>();
                var holder = new GameObject("Ghost visual (preview only)");
                holder.transform.SetParent(stage.transform,false);
                var ghost = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Ghost));
                PrefabUtility.UnpackPrefabInstance(ghost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ghost.transform.SetParent(holder.transform,false);
                ghost.transform.localPosition = Vector3.zero;
                ghost.transform.localRotation = Quaternion.Euler(0,180,0);
                ghost.transform.localScale=Vector3.one*1.3536f;
                var ghostMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/ModelThesis/Ghost Model/LO Ghost/GhostLightOut_mat.mat");
                foreach (var visual in ghost.GetComponentsInChildren<Renderer>(true)) visual.sharedMaterial=ghostMaterial;
                rig.ghostModels[i]=ghost;
                // Strip gameplay from this preview instance before Play mode can run it.
                foreach (var c in ghost.GetComponentsInChildren<MonoBehaviour>(true).Reverse()) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in ghost.GetComponentsInChildren<NavMeshAgent>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in ghost.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in ghost.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var animator in ghost.GetComponentsInChildren<Animator>(true))
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.enabled = false;
                }
                rig.bodies[i] = holder.transform;
                rig.splashes[i] = Effect("Black water splash - VFX Graph", "Ghost_BlackWater_Splash", stage.transform, new Vector3(0,.08f,-.35f));
                if (i == 1) rig.smoke = Effect("Submerge smoke - VFX Graph", "Ghost_Submerge_Smoke", stage.transform, new Vector3(0,.65f,0));
            }
            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0,4.3f,-9);
            camera.transform.LookAt(new Vector3(0,.55f,0));
            camera.fieldOfView = 43;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f,.03f,.04f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            Light("Cool key", LightType.Directional, new Vector3(25,180,0), new Color(.7f,.82f,1), 1.7f, Vector3.zero);
            Light("Water highlight", LightType.Point, Vector3.zero, new Color(.7f,.85f,1), 4, new Vector3(0,3,2));
            var reflection = new GameObject("Water reflection").AddComponent<ReflectionProbe>();
            reflection.transform.position = new Vector3(0,1,0);
            reflection.size = new Vector3(18,10,18);
            reflection.mode = ReflectionProbeMode.Realtime;
            reflection.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            reflection.resolution = 128;
            rig.Sample(0);
            EditorSceneManager.SaveScene(scene,Root + "/BlackWater_Preview.unity");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Root + "/BuildResult.txt", "PASS: Two Visual Effect Graph assets, water material, GPU effect prefabs and real ghost preview scene generated. Gameplay is not connected yet.\n");
            Debug.Log("[GhostPool] Preview built successfully.");
        }
        catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); throw; }
    }

    static void Light(string name, LightType type, Vector3 rotation, Color color, float intensity, Vector3 position)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type=type; light.color=color; light.intensity=intensity; light.range=12;
        light.transform.position=position; light.transform.eulerAngles=rotation;
    }

    static VisualEffect Effect(string name, string graph, Transform parent, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent,false); go.transform.localPosition=position;
        var effect=go.AddComponent<VisualEffect>();
        effect.visualEffectAsset=AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Root + "/Graphs/" + graph + ".vfx");
        if (effect.visualEffectAsset == null) throw new Exception("Graph failed to import: " + graph);
        effect.startSeed=71; effect.resetSeedOnPlay=false;
        string path=Root + "/Prefabs/" + graph + ".prefab";
        if (!File.Exists(path)) PrefabUtility.SaveAsPrefabAsset(go,path);
        return effect;
    }

    // Batch verification runs only in the isolated authoring project.
    public static void Capture()
    {
        EditorSceneManager.OpenScene(Root + "/BlackWater_Preview.unity");
        var rig=UnityEngine.Object.FindAnyObjectByType<GhostPoolPreview>();
        var camera=Camera.main;
        foreach(var effect in rig.splashes) { effect.Reinit(); effect.Stop(); effect.pause=true; }
        rig.smoke.Reinit(); rig.smoke.Play(); rig.smoke.pause=true;
        int frame=0;
        Directory.CreateDirectory("captures");
        EditorApplication.CallbackFunction tick=null;
        tick=()=>
        {
            try
            {
                float age=frame/60f;
                rig.Sample(age);
                if(frame==54) foreach(var effect in rig.splashes) effect.Play();
                if(frame>=54) foreach(var effect in rig.splashes) effect.Simulate(1f/60,1);
                rig.smoke.Simulate(1f/60,1);
                var target=RenderTexture.GetTemporary(1280,720,24);
                camera.targetTexture=target;
                camera.Render();
                if(frame%4==0 || frame==30 || frame==75 || frame==144 || frame==324)
                {
                    RenderTexture.active=target;
                    var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                    File.WriteAllBytes("captures/black-water-"+frame+".png",image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    RenderTexture.active=null;
                    if(frame==30 || frame==75 || frame==144 || frame==324) Debug.Log("[GhostPool] Captured frame " + frame + "; splash alive="+rig.splashes[0].aliveParticleCount+"; smoke alive="+rig.smoke.aliveParticleCount);
                }
                camera.targetTexture=null;
                RenderTexture.ReleaseTemporary(target);
                frame++;
                if(frame>420)
                {
                    EditorApplication.update-=tick;
                    Debug.Log("[GhostPool] Capture verification finished.");
                    EditorApplication.Exit(0);
                }
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.update-=tick; EditorApplication.Exit(1); }
        };
        EditorApplication.update+=tick;
    }

    static void MakeGraph(bool smoke)
    {
        string name = smoke ? "Ghost_Submerge_Smoke" : "Ghost_BlackWater_Splash";
        string template = Directory.GetFiles("Library/PackageCache", "Simple_Burst.vfx", SearchOption.AllDirectories).First(p => p.Contains("visualeffectgraph") && p.Contains("Templates"));
        string yaml=File.ReadAllText(template).Replace("03_Simple_Burst",name).Replace("Simple_Burst",name)
            .Replace("  loopDuration: 1", "  loopDuration: 0")
            .Replace("m_InitialEventName: OnPlay", "m_InitialEventName: Idle")
            .Replace("attribute: size\n  Composition: 2", "attribute: size\n  Composition: 0")
            .Replace("attribute: size\r\n  Composition: 2", "attribute: size\r\n  Composition: 0");
        if (!smoke) yaml = yaml.Replace("  capacity: 128", "  capacity: 512");
        var texture = new Texture2D(128,128,TextureFormat.RGBA32,false);
        for (int y=0;y<128;y++) for (int x=0;x<128;x++)
        {
            float px=(x+.5f)/64-1, py=(y+.5f)/64-1;
            float r=Mathf.Sqrt(px*px+py*py);
            float alpha=smoke ? Mathf.Pow(Mathf.Clamp01(1-r),1.2f)*Mathf.Lerp(.3f,1,Mathf.PerlinNoise(x/17f,y/17f)) : 1-Mathf.SmoothStep(.62f,.98f,r);
            float highlight=smoke ? 1 : Mathf.Exp(-((px+.28f)*(px+.28f)+(py-.28f)*(py-.28f))*28);
            texture.SetPixel(x,y,smoke ? new Color(1,1,1,alpha) : new Color(.035f+highlight*.55f,.045f+highlight*.62f,.055f+highlight*.7f,alpha));
        }
        texture.Apply();
        string texPath=Root + "/Materials/" + name + ".png";
        File.WriteAllBytes(texPath,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(texPath,ImportAssetOptions.ForceSynchronousImport);
        string textureGuid=AssetDatabase.AssetPathToGUID(texPath);
        yaml=Regex.Replace(yaml,@"(?m)^--- [\s\S]*?(?=^--- |\z)", match =>
        {
            string block=match.Value;
            var property=Regex.Match(block,@"(?m)^    name: (.+)\r?$");
            if (!property.Success) return block;
            string slot=property.Groups[1].Value.Trim(), value=null;
            switch(slot)
            {
                case "Count": value=smoke ? "85" : "210"; break;
                case "A": value=smoke ? "1.4" : "0.65"; break;
                case "B": value=smoke ? "2.5" : "1.05"; break;
                case "MinSpeed": value=smoke ? "0.15" : "2.4"; break;
                case "MaxSpeed": value=smoke ? "0.65" : "4.2"; break;
                case "DirectionBlend": value=smoke ? "0.85" : "0.78"; break;
                case "dragCoefficient": value=smoke ? null : "0.18"; break;
                case "mainTexture": value="'{\"obj\":{\"fileID\":2800000,\"guid\":\""+textureGuid+"\",\"type\":3}}'"; break;
                case "Force": value=smoke ? "'{\"vector\":{\"x\":0,\"y\":0.12,\"z\":0}}'" : null; break;
                case "Color":
                    value=smoke
                        ? "'{\"colorKeys\":[{\"color\":{\"r\":0.045,\"g\":0.052,\"b\":0.06,\"a\":1},\"time\":0},{\"color\":{\"r\":0.10,\"g\":0.115,\"b\":0.13,\"a\":1},\"time\":1}],\"alphaKeys\":[{\"alpha\":0,\"time\":0},{\"alpha\":0.8,\"time\":0.12},{\"alpha\":0,\"time\":1}],\"gradientMode\":0}'"
                        : "'{\"colorKeys\":[{\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1},\"time\":0}],\"alphaKeys\":[{\"alpha\":1,\"time\":0},{\"alpha\":1,\"time\":0.65},{\"alpha\":0,\"time\":1}],\"gradientMode\":0}'";
                    break;
                case "Size":
                    value=smoke
                        ? "'{\"frames\":[{\"time\":0,\"value\":0.35,\"inTangent\":0,\"outTangent\":1},{\"time\":1,\"value\":1.3,\"inTangent\":0,\"outTangent\":0}],\"preWrapMode\":8,\"postWrapMode\":8,\"version\":1}'"
                        : "'{\"frames\":[{\"time\":0,\"value\":0.11,\"inTangent\":0,\"outTangent\":0},{\"time\":1,\"value\":0.035,\"inTangent\":0,\"outTangent\":0}],\"preWrapMode\":8,\"postWrapMode\":8,\"version\":1}'";
                    break;
                case "arcSphere": value=smoke
                    ? "'{\"sphere\":{\"transform\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"angles\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1.5,\"y\":0.05,\"z\":1.3}},\"radius\":0.6},\"arc\":6.283185}'"
                    : "'{\"sphere\":{\"transform\":{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"angles\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":0.08,\"z\":1}},\"radius\":0.55},\"arc\":6.283185}'"; break;
            }
            return value==null ? block : Regex.Replace(block,@"(?m)^      m_SerializableObject:.*$",_ => "      m_SerializableObject: " + value);
        },RegexOptions.Multiline);
        File.WriteAllText(Root + "/Graphs/" + name + ".vfx",yaml);
    }
}

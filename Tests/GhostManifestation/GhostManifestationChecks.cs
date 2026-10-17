#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class GhostManifestationChecks
{
    const string Key = "GhostManifestationChecks.Running";
    const string Prefab = "Assets/ModelThesis/Ghost Model/LO Ghost/Prefab/Ghost Walk Anim.prefab";
    static GameObject ghost;
    static GhostManifestation presentation;
    static double started, despawned;
    static bool initialized, walked;
    static NavMeshDataInstance navMesh;
    static readonly List<string> results = new List<string>();

    static GhostManifestationChecks()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Pre-Thesis/Tests/Ghost Manifestation")]
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show();
        EditorApplication.isPlaying = true;
    }

    static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
        results.Add("PASS " + label);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (!initialized)
            {
                initialized = true;
                EditorApplication.isPaused = false;
                Time.timeScale = 1f;
                Application.runInBackground = true;
                NetworkMode.SessionEnding = false;
                var settings = NavMesh.GetSettingsByIndex(0);
                var source = new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Box, size = new Vector3(40, 1, 40),
                    transform = Matrix4x4.TRS(new Vector3(0, -0.5f, 0), Quaternion.identity, Vector3.one), area = 0
                };
                var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source },
                    new Bounds(Vector3.zero, new Vector3(50, 10, 50)), Vector3.zero, Quaternion.identity);
                Check(data != null, "Test navigation surface builds");
                navMesh = NavMesh.AddNavMeshData(data);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var serialized = new SerializedObject(prefab.GetComponent<GhostManifestation>());
                Check(serialized.FindProperty("appearPrefab").objectReferenceValue != null &&
                    serialized.FindProperty("disappearPrefab").objectReferenceValue != null, "Both stage effects assigned");
                Check(serialized.FindProperty("spawnClip").objectReferenceValue.name == "GhostSpawn_1", "Spawn sound assigned");
                var clips = serialized.FindProperty("walkClips");
                Check(clips.arraySize == 4, "Four walk clips assigned");
                for (int i = 0; i < 4; i++) Check(clips.GetArrayElementAtIndex(i).objectReferenceValue.name ==
                    "Ghost_Carpet_Walk_2." + (i + 1), "Walk clip " + (i + 1) + " resolves");
                var controller = (AnimatorController)prefab.GetComponent<Animator>().runtimeAnimatorController;
                var transition = controller.layers[0].stateMachine.defaultState.transitions[0];
                Check(transition.hasExitTime && transition.exitTime == 1f && transition.duration == 0f,
                    "Entry finishes fully before Walk");
                ghost = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                presentation = ghost.GetComponent<GhostManifestation>();
                started = EditorApplication.timeSinceStartup;
                return;
            }
            double age = EditorApplication.timeSinceStartup - started;
            if (age < 0.1) return; // Allow Start and the first animation evaluation.
            if (age > 25) throw new Exception("Ghost lifecycle timed out: game time=" + Time.time +
                ", normalized=" + ghost.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).normalizedTime);
            if (!walked)
            {
                if (presentation.BlocksMovement)
                {
                    Check(ghost.transform.position.sqrMagnitude < 0.001f, "No translation during Entry");
                    Check(!ghost.transform.Find("Ghost Footsteps").GetComponent<AudioSource>().isPlaying,
                        "No footsteps during Entry");
                    return;
                }
                Check(age > 1, "Spawn gate waits for animation");
                if (ghost.transform.position.sqrMagnitude < 0.02f) return;
                if (!ghost.transform.Find("Ghost Footsteps").GetComponent<AudioSource>().isPlaying) return;
                Check(true, "Ghost walks and plays footsteps after Entry");
                Check(GameObject.Find("Ghost_Appear(Clone)") != null || age >= 5,
                    "Appear effect lifetime independent of ghost");
                presentation.Despawn();
                Check(presentation.BlocksMovement, "Despawn immediately blocks AI");
                Check(!ghost.GetComponentInChildren<Renderer>().enabled, "Despawn hides the model");
                walked = true;
                despawned = EditorApplication.timeSinceStartup;
                return;
            }
            if (EditorApplication.timeSinceStartup - despawned < 0.7) return;
            Check(ghost == null, "Ghost removed after disappearance begins");
            Check(GameObject.Find("Ghost_Disappear(Clone)") != null, "Disappear smoke survives ghost removal");
            Finish(0);
        }
        catch (Exception error)
        {
            results.Add("FAIL " + error);
            Finish(1);
        }
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        navMesh.Remove();
        File.WriteAllLines("ghost-manifestation-results.txt", new HashSet<string>(results));
        EditorApplication.Exit(code);
    }
}
#endif

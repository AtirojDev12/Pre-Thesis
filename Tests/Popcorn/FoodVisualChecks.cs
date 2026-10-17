// Copy to Assets/Editor in an isolated project; execute FoodVisualChecks.Run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FoodVisualChecks
{
    const string Key = "FoodVisualChecks.Run";
    static readonly List<string> results = new List<string>();
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static FoodVisualChecks()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += WaitForPlay;
    }

    static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= WaitForPlay;
        CheckAll();
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
        results.Add("PASS " + label);
    }

    static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ModelThesis/N_model/" + path + ".prefab");

    static void CheckAll()
    {
        int exit = 0;
        try
        {
            NetworkMode.SessionEnding = false;
            GameObject empty = Load("PopCornEmpty/PopcornBucket_Empty"), plain = Load("Popcorntaste/NotastePopcorn"),
                bbq = Load("Popcorntaste/BBQPopcorn"), cheese = Load("Popcorntaste/CheesePopcorn"),
                paprika = Load("Popcorntaste/PapricaPopcorn"), ghost = Load("Popcorntaste/GhostFaverPopcorn"),
                emptyCup = Load("EmptyCup/EmptyCup"), fullCup = Load("FullCup/FullCup"), ghostCup = Load("FullCupGhost/FullCupGhost");
            var player = new GameObject("Local fixture", typeof(NetworkIdentity), typeof(PlayerHealth), typeof(PlayerHeldItems));
            var health = player.GetComponent<PlayerHealth>();
            health.OnStartLocalPlayer();
            new GameObject("Hand camera", typeof(Camera)).transform.SetParent(player.transform, false);
            var owner = player.GetComponent<PlayerHeldItems>();
            var holder = new GameObject("Preparation fixture").AddComponent<PopcornItemHoldingSystem>();
            holder.Configure(plain, new Vector3(-.3f, -.24f, .65f), new Vector3(-10, 0, -8));
            holder.ConfigureContainers(empty, emptyCup, fullCup, ghostCup);
            holder.ConfigurePopcornFlavors(bbq, cheese, paprika, ghost);
            var observer = new GameObject("Observer fixture", typeof(NetworkIdentity), typeof(PlayerHeldItems)).GetComponent<PlayerHeldItems>();
            var lateJoiner = new GameObject("Late join fixture", typeof(NetworkIdentity), typeof(PlayerHeldItems)).GetComponent<PlayerHeldItems>();
            bool observerInitialized = false;

            void Verify(GameObject expected, string label)
            {
                Check(holder.Holding.CurrentPrefab == expected, label + " local model");
                Check(holder.PrefabFor(holder.State) == expected, label + " shared model mapping");
                var writer = new NetworkWriter();
                owner.OnSerialize(writer, true);
                lateJoiner.OnDeserialize(new NetworkReader(writer.ToArraySegment()), true);
                Check(holder.PrefabFor(lateJoiner.TaskItem) == expected, label + " late join snapshot model");
                var delta = new NetworkWriter();
                owner.OnSerialize(delta, !observerInitialized);
                observer.OnDeserialize(new NetworkReader(delta.ToArraySegment()), !observerInitialized);
                observerInitialized = true;
                TaskHeldState received = observer.TaskItem;
                Check(received.flavor == holder.HeldFlavor && received.ghostMixed == holder.GhostMixed &&
                    received.hasPopcorn == holder.HasPopcorn && received.isCup == holder.IsCup,
                    label + " Mirror snapshot preserves recipe state");
                Check(holder.PrefabFor(received) == expected, label + " observer model");
                owner.ClearAllDirtyBits();
                var remote = (GameObject)typeof(PlayerHeldItems).GetMethod("CreateVisual", Private).Invoke(observer,
                    new object[] { expected, "Remote check", .24f });
                var bucket = remote.GetComponentInChildren<PopcornBucketVisual>();
                if (bucket != null)
                {
                    float height = new SerializedObject(bucket).FindProperty("referenceBodyHeight").floatValue;
                    Check(Mathf.Abs(bucket.transform.TransformVector(Vector3.up * height).magnitude - .24f) < .0001f,
                        label + " observer bucket body remains 24 cm");
                    var local = holder.Holding.Visual.GetComponentInChildren<PopcornBucketVisual>();
                    Check(Mathf.Abs(local.transform.TransformVector(Vector3.up * height).magnitude - .24f) < .0001f,
                        label + " local bucket body remains 24 cm");
                }
                else
                {
                    var cup = remote.GetComponentInChildren<DrinkCupVisual>();
                    Check(cup != null && Mathf.Abs(Vector3.Distance(cup.BasePosition, cup.RimPosition) - .224f) < .0001f,
                        label + " observer cup body remains 22.4 cm");
                }
                PopcornItemHoldingSystem.TintVisual(remote, received.flavor, received.ghostMixed);
                foreach (Renderer renderer in remote.GetComponentsInChildren<Renderer>())
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Check(properties.isEmpty, label + " keeps authored materials");
                }
                UnityEngine.Object.Destroy(remote);
            }

            foreach (var flavor in new[] { PopcornFlavor.BBQ, PopcornFlavor.Cheese, PopcornFlavor.Paprika })
            {
                Check(holder.PickUp(false), "Pick up empty bucket"); Verify(empty, "Empty bucket");
                Check(!holder.Hold(flavor), "Cannot season an empty bucket");
                Check(holder.Scoop(), "Fill popcorn"); Verify(plain, "Plain popcorn");
                Check(holder.Hold(flavor), "Season " + flavor);
                Verify(flavor == PopcornFlavor.BBQ ? bbq : flavor == PopcornFlavor.Cheese ? cheese : paprika, flavor.ToString());
                Check(holder.MixGhost(), "Mix ghost popcorn"); Verify(ghost, "Ghost " + flavor);
                Check(holder.HeldFlavor == flavor, "Ghost seasoning preserves base flavor");
                Check(!holder.MixGhost(), "Cannot mix ghost twice");
                holder.Consume();
            }
            foreach (var flavor in new[] { PopcornFlavor.Drink, PopcornFlavor.Pepsi, PopcornFlavor.Fanta, PopcornFlavor.OrangeJuice })
            {
                Check(holder.PickUp(true), "Pick up empty cup"); Verify(emptyCup, "Empty cup"); holder.Consume();
                Check(holder.ReceiveMachineBottle(flavor), "Receive completed drink"); Verify(fullCup, flavor.ToString());
                Check(holder.MixGhost(), "Mix ghost drink"); Verify(ghostCup, "Ghost " + flavor);
                Check(holder.HeldFlavor == flavor, "Ghost drink preserves ordered flavor");
                holder.Consume();
            }
            // A joining observer must also receive the final mixed model from the initial snapshot.
            Check(results.Count > 100, "All preparation stages and observer snapshots covered");
        }
        catch (Exception error) { results.Add("FAIL " + error); exit = 1; }
        SessionState.SetBool(Key, false);
        File.WriteAllLines("food-visual-results.txt", results);
        EditorApplication.Exit(exit);
    }
}

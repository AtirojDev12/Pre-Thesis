// Copied into Assets/Editor in an isolated project by Run-PopcornChecks.ps1.
// Exercises the authored Z1 scene, player/HUD prefabs, input actions and raycasts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PopcornRegressionRunner
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int failures;
    static int ticks;
    static int stage;
    static double deadline;
    static Camera camera;
    static PlayerHealth player;
    static PlayerInteractor interactor;
    static PopcornGameManager manager;
    static ItemHoldingSystem holder;
    static CounterSlot slot;
    static InputSystemUIInputModule module;
    static Mouse mouse;
    static Keyboard keyboard;
    static PopcornCustomer servedCustomer;
    static GameObject blocker;

    static PopcornRegressionRunner()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("PopcornRegression", false)) return;
            deadline = EditorApplication.timeSinceStartup + 90;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList.ToList();
            systems.Add(new UnityEngine.LowLevel.PlayerLoopSystem { type = typeof(PopcornRegressionRunner), updateDelegate = Tick });
            loop.subSystemList = systems.ToArray();
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.update += EditorApplication.Step;
        };
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run these checks in an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Z1_Gameplay.unity");
        SessionState.SetBool("PopcornRegression", true);
        EditorApplication.EnterPlaymode();
    }

    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Check(bool pass, string message)
    {
        results.Add((pass ? "PASS " : "FAIL ") + message);
        if (!pass) failures++;
        Debug.Log(results[results.Count - 1]);
    }
    static Button Button(string name) => GameObject.Find(name).GetComponent<Button>();
    static int Score => (int)Get(manager, "score");
    static void Aim(Transform target)
    {
        camera.transform.LookAt(target.position);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        Physics.SyncTransforms();
    }
    static void MouseState(Vector2 position, bool pressed)
    {
        var state = new UnityEngine.InputSystem.LowLevel.MouseState { position = position };
        if (pressed) state = state.WithButton(MouseButton.Left);
        UpdateInput();
        InputState.Change(mouse, state, InputUpdateType.Dynamic);
        module.Process();
    }
    static void Click(Button target, bool locked)
    {
        Aim(target.transform);
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        var point = (Vector2)camera.WorldToScreenPoint(target.transform.position);
        MouseState(point, false);
        MouseState(point, true);
        MouseState(point, false);
    }
    static void PressE()
    {
        UpdateInput();
        InputState.Change(keyboard, new KeyboardState(Key.E), InputUpdateType.Dynamic);
        Call(interactor, "Update");
        UpdateInput();
        InputState.Change(keyboard, new KeyboardState(), InputUpdateType.Dynamic);
    }
    static void AimCustomer(PopcornCustomer customer)
    {
        player.transform.position = new Vector3(.885f, 1f, 1.2f);
        camera.transform.LookAt(customer.transform.position + Vector3.up * .65f);
        Physics.SyncTransforms();
        Call(interactor, "Update");
    }
    static void Make(PopcornFlavor flavor)
    {
        player.transform.position = new Vector3(-.728f, 1f, 2f);
        Click(Button(flavor == PopcornFlavor.Cheese ? "CHEESE Button" : flavor == PopcornFlavor.BBQ ? "BBQ Button" : "GHOST FLAVOR Button"), false);
        Click(Button("Make Button"), false);
    }
    static void SpawnCase(PopcornCustomerType type, PopcornFlavor order)
    {
        // Choose customer type deterministically after verifying the natural queue transition.
        if (slot.ActiveCustomer != null)
        {
            var previous = slot.ActiveCustomer;
            slot.Vacate(previous);
            previous.gameObject.SetActive(false);
            Object.Destroy(previous.gameObject);
        }
        // When the authored scene includes net sync, deterministic fixtures must
        // change the authoritative order too, not only replace its local visual.
        var sync = PopcornNetSync.Instance;
        if (sync != null)
        {
            typeof(PopcornNetSync).GetField("currentCustomerType", Private).SetValue(sync, type);
            typeof(PopcornNetSync).GetField("currentOrder", Private).SetValue(sync, order);
            typeof(PopcornNetSync).GetField("customerWaiting", Private).SetValue(sync, true);
            Call(manager, "OnSyncOrderChanged");
        }
        else slot.Occupy(manager, type, order);
    }
    // Editor callbacks otherwise update the editor's separate device state in batch mode.
    static void UpdateInput()
    {
        var inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        inputManager.GetType().GetMethod("OnBeforeUpdate", Private).Invoke(inputManager, new object[] { InputUpdateType.Dynamic });
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out at stage " + stage);
            if (++ticks < 12) return; // Render and initialize the actual scene before querying graphic depth.
            if (stage == 0)
            {
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                player = PlayerHealth.LocalInstance;
                manager = Object.FindAnyObjectByType<PopcornGameManager>();
                if (player == null || manager == null) throw new Exception("Authored scene did not initialize player/minigame.");
                camera = player.GetComponentInChildren<Camera>();
                interactor = player.GetComponent<PlayerInteractor>();
                holder = Object.FindAnyObjectByType<ItemHoldingSystem>();
                slot = Object.FindAnyObjectByType<CounterSlot>();
                module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse.MakeCurrent();
                keyboard.MakeCurrent();
                // Freeze locomotion during deterministic aim/input tests, but retain actual colliders and UI.
                player.GetComponent<PlayerMovement>().enabled = false;
                player.GetComponentInChildren<FirstPersonCamera>().enabled = false;
                player.GetComponent<Rigidbody>().isKinematic = true;
                interactor.enabled = false;
                module.ActivateModule();
                player.transform.position = new Vector3(-.728f, 1f, 2f);
                var cheese = Button("CHEESE Button");
                Aim(cheese.transform);
                Check(module.point.action.enabled && module.leftClick.action.enabled, "UI mouse actions are enabled");
                Check((Key)Get(interactor, "interactKey") == Key.E, "Player prefab binds interaction to Input System E");
                Check(cheese.GetComponentInParent<Canvas>().worldCamera == camera, "World canvas is bound to local camera");
                Check(camera.enabled && camera.targetTexture == null, "Local camera renders to the game display");
                Click(Button("Make Button"), false);
                Check(!holder.HasItem, "Make without a flavor leaves inventory empty");
                Aim(cheese.transform);
                var point = (Vector2)camera.WorldToScreenPoint(cheese.transform.position);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Check(hits.Count > 0 && hits[0].gameObject == cheese.gameObject,
                    "Flavor is first UI raycast hit; hits=" + string.Join(",", hits.Select(hit => hit.gameObject.name)) + "; depth=" + cheese.GetComponent<Graphic>().depth);
                Click(cheese, false);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.Cheese, "Unlocked mouse input selects Cheese");
                Click(Button("BBQ Button"), true);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.BBQ, "Locked crosshair mouse input selects BBQ");
                Click(Button("GHOST FLAVOR Button"), false);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.Ghost, "Unlocked mouse input selects Ghost");
                Click(Button("Make Button"), false);
                Check(holder.HasItem && holder.HeldFlavor == PopcornFlavor.Ghost && Get(holder, "heldVisual") != null, "Mouse click on Make creates held Ghost popcorn");
                Check(Button("GHOST FLAVOR Button").GetComponent<Outline>().enabled, "Selected flavor remains outlined while clicking Make");
                var oldVisual = (GameObject)Get(holder, "heldVisual");
                Click(Button("Make Button"), false);
                Check(!oldVisual.activeSelf && Get(holder, "heldVisual") != oldVisual, "Making again replaces the previous visible bucket");
                Aim(cheese.transform);
                PressE();
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.Cheese, "E input ray selects Cheese in authored scene");
                // Check that menu clicks cannot bypass the player interaction range.
                player.transform.position = new Vector3(-.728f, 1f, 8f);
                Click(Button("BBQ Button"), false);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.Cheese, "Distant mouse click is rejected");
                player.transform.position = new Vector3(-.728f, 1f, 2f);
                Aim(Button("BBQ Button").transform);
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = Vector3.Lerp(camera.transform.position, Button("BBQ Button").transform.position, .5f);
                blocker.transform.localScale = Vector3.one * .5f;
                Physics.SyncTransforms();
                Click(Button("BBQ Button"), false);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.Cheese, "Occluded mouse click is rejected");
                blocker.SetActive(false);
                Object.Destroy(blocker);
                // A collider belonging to the player's body must not eat their own interaction ray.
                var ownCollider = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ownCollider.transform.SetParent(camera.transform, false);
                ownCollider.transform.localPosition = Vector3.forward * .3f;
                ownCollider.transform.localScale = Vector3.one * .1f;
                Click(Button("BBQ Button"), false);
                Check((PopcornFlavor)Get(manager, "selectedFlavor") == PopcornFlavor.BBQ, "Player-owned collider does not block menu input");
                ownCollider.SetActive(false);
                Object.Destroy(ownCollider);
                holder.Consume();
                stage = 1;
            }
            else if (stage == 1 && slot.ActiveCustomer != null && slot.ActiveCustomer.CanInteract())
            {
                var customer = slot.ActiveCustomer;
                player.transform.position = new Vector3(.885f, 1f, 1.2f);
                // Aim at the upper body, above the counter geometry.
                camera.transform.LookAt(customer.transform.position + Vector3.up * .65f);
                Physics.SyncTransforms();
                Call(interactor, "Update");
                Check(customer.GetComponentInChildren<Canvas>().enabled, "Customer E prompt appears over the counter");
                camera.transform.LookAt(camera.transform.position + Vector3.up);
                Call(interactor, "Update");
                Check(!customer.GetComponentInChildren<Canvas>().enabled, "Customer prompt hides when looking away");
                AimCustomer(customer);
                PressE();
                Check(customer.CanInteract() && Score == 0, "Empty-handed E keeps the order active");
                player.transform.position = new Vector3(-.728f, 1f, 2f);
                string flavorButton = customer.Order == PopcornFlavor.Cheese ? "CHEESE Button" : customer.Order == PopcornFlavor.BBQ ? "BBQ Button" : "GHOST FLAVOR Button";
                Click(Button(flavorButton), false);
                Click(Button("Make Button"), false);
                player.transform.position = new Vector3(.885f, 1f, 1.2f);
                camera.transform.LookAt(customer.transform.position + Vector3.up * .65f);
                Physics.SyncTransforms();
                PressE();
                Check(!holder.HasItem && !customer.CanInteract() && Score == 1, "Mouse-made order submits using E and scores once");
                Check(!customer.GetComponentInChildren<Canvas>().enabled, "Prompt hides immediately on submission");
                manager.TryServe(customer, player.gameObject);
                Check(Score == 1, "Repeated submission cannot score twice");
                servedCustomer = customer;
                if (Score != 1) { Finish(); return; }
                stage = 2;
            }
            else if (stage == 2 && slot.ActiveCustomer != null && slot.ActiveCustomer != servedCustomer && slot.ActiveCustomer.CanInteract())
            {
                Check(true, "Served customer exits and next order arrives");
                SpawnCase(PopcornCustomerType.Human, PopcornFlavor.Cheese);
                stage = 3;
            }
            else if (stage >= 3 && stage <= 5 && slot.ActiveCustomer != null && slot.ActiveCustomer.CanInteract())
            {
                var customer = slot.ActiveCustomer;
                var healthBefore = player.CurrentHealth;
                var scoreBefore = Score;
                Make(stage == 5 ? PopcornFlavor.Ghost : PopcornFlavor.BBQ);
                AimCustomer(customer);
                Check(customer.GetComponentInChildren<Canvas>().enabled, customer.CustomerType + " has a visible E prompt");
                PressE();
                Check(!holder.HasItem && !customer.CanInteract(), "Case " + stage + " consumes the item and releases the customer");
                Check(Score == scoreBefore + (stage == 5 ? 1 : 0), "Case " + stage + " applies correct score");
                Check(Mathf.Approximately(player.CurrentHealth, healthBefore - (stage == 4 ? 10 : 0)), "Case " + stage + " applies correct damage");
                if (stage < 5)
                {
                    SpawnCase(PopcornCustomerType.Ghost, PopcornFlavor.Ghost);
                    stage++;
                    return;
                }
                Make(PopcornFlavor.Cheese);
                player.OnStartLocalPlayer();
                Check(holder.HasItem, "Rebinding the same local player preserves their item");
                player.OnStopLocalPlayer();
                Check(!holder.HasItem && Get(holder, "heldVisual") == null, "Losing local player clears held inventory and visual");
                player.OnStartLocalPlayer();
                Finish();
            }
        }
        catch (Exception exception)
        {
            Check(false, exception.ToString());
            Finish();
        }
    }
    static void Finish()
    {
        SessionState.SetBool("PopcornRegression", false);
        File.WriteAllLines("popcorn-results.txt", results);
        Debug.Log("Popcorn regression: " + (results.Count - failures) + " passed; " + failures + " failed.");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}

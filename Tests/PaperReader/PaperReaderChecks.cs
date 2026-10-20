// Copy into an isolated Unity project's Assets/Editor and run PaperReaderChecks.Run.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PaperReaderChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Flags).GetValue(target);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void ResetOverlay() => typeof(OverlayPanels).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    public static void Run()
    {
        string result = Path.GetFullPath("../paper-reader-results.txt");
        try
        {
            ResetOverlay();
            GameplayInput.Blocked = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var owner = new GameObject("Reader test player");
            var root = new GameObject("Reader", typeof(RectTransform));
            root.transform.SetParent(owner.transform, false);
            var reader = root.AddComponent<PaperReaderUI>();
            var source = new GameObject("Paper").AddComponent<ReadablePaper>();
            var portrait = new Texture2D(600, 1000);
            var landscape = new Texture2D(1600, 900);
            Set(source, "pages", new[] { portrait, null, landscape });
            Set(source, "closeDuration", 0f);
            int closed = 0;
            Get<UnityEvent>(source, "onClosed").AddListener(() => closed++);
            Check(source.CanInteract(), "Valid paper unavailable");
            Check(reader.Open(source, owner), "Cannot open");
            Check(GameplayInput.Blocked && OverlayPanels.AnyOpen, "Reading does not block gameplay");
            Check(!reader.Open(source, owner), "Duplicate opening allowed");
            RectTransform sheet = Get<RectTransform>(reader, "paper");
            RectTransform screen = Get<RectTransform>(reader, "screen");
            Check(sheet.anchoredPosition.y + sheet.rect.height * .5f < -screen.rect.height * .5f,
                "Initial sheet is not entirely below viewport");
            Set(reader, "progress", .5f);
            Call(reader, "Layout");
            Check(sheet.anchoredPosition.y < 0, "Midpoint does not move towards center");
            Set(reader, "progress", 1f);
            Call(reader, "Layout");
            Check(sheet.anchoredPosition.sqrMagnitude < .01f, "Sheet does not finish centered");
            Check(Mathf.Abs(sheet.rect.width / sheet.rect.height - .6f) < .001f, "Portrait is stretched");
            Check(sheet.rect.height <= screen.rect.height && sheet.rect.width <= screen.rect.width, "Sheet exceeds viewport");
            Call(reader, "ChangePage", 1);
            Check(Get<RawImage>(reader, "image").texture == landscape, "Page navigation does not skip empty slot");
            Call(reader, "ChangePage", -1);
            Check(Get<RawImage>(reader, "image").texture == portrait, "Previous page failed");
            reader.Close();
            Call(reader, "Update");
            Check(!reader.IsOpen && !GameplayInput.Blocked && !OverlayPanels.AnyOpen && closed == 1,
                "Close did not restore controls exactly once");
            Check(OverlayPanels.BlocksEscape && !source.CanInteract(), "Close frame can reopen paper or open pause");
            ResetOverlay();
            Check(reader.Open(source, owner), "Cannot reopen reader");
            // A configured exit animation retains the modal until its final frame.
            Set(source, "closeDuration", 10f);
            Set(reader, "progress", 1f);
            reader.Close();
            Call(reader, "Update");
            Check(reader.IsOpen && GameplayInput.Blocked, "Controls released before exit animation finishes");
            owner.SetActive(false);
            // Non-ExecuteAlways MonoBehaviours do not receive runtime callbacks in Edit mode.
            Call(reader, "OnDisable");
            Check(!reader.IsOpen && !OverlayPanels.AnyOpen && !GameplayInput.Blocked && closed == 2,
                "Disabled player leaked modal/input lock");
            owner.SetActive(true);
            ResetOverlay();
            Check(reader.Open(source, owner), "Cannot reopen after disable");
            source.enabled = false;
            Call(reader, "Update");
            Check(!reader.IsOpen && !GameplayInput.Blocked && closed == 3, "Disabled paper leaked input lock");
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(source.gameObject);
            Object.DestroyImmediate(portrait);
            Object.DestroyImmediate(landscape);

            // Verify the actual authored scene and imported image, beyond synthetic UI tests.
            ResetOverlay();
            EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
            ReadablePaper authored = null;
            foreach (var candidate in Object.FindObjectsByType<ReadablePaper>(FindObjectsInactive.Include))
                if (candidate.name == "Paper") authored = candidate;
            Check(authored != null && authored.GetPage(0) != null, "Scene Paper or instruction texture is missing");
            Check(authored.GetComponent<Collider>() != null && !authored.GetComponent<Collider>().isTrigger,
                "Paper lacks interaction collider");
            Check(authored.CloseKey == UnityEngine.InputSystem.Key.E, "Incorrect default close key");
            Check(authored.SlideCurve != null && authored.SlideCurve.length >= 2, "Scene animation curve did not deserialize");
            File.WriteAllText(result, "PASS: offscreen start, eased movement, centered aspect fit, page skipping, duplicate open, animated close lock, close-frame guard, disable cleanup, scene Paper/texture/collider/defaults.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(result, "FAIL: " + exception + "\n");
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}

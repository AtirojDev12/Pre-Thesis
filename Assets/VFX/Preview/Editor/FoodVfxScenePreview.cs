using UnityEditor;
using UnityEngine;
using PreThesis.Vfx;

/// <summary>Transient editor simulation confined to the two authored preview scenes.</summary>
[InitializeOnLoad]
public static class FoodVfxScenePreview
{
    private static FoodVfxPreview current;
    private static double previousTime;
    private static float age;

    public static void Frame(Transform target)
    {
        if (SceneView.lastActiveSceneView == null) return;
        var camera = Object.FindAnyObjectByType<Camera>();
        Quaternion rotation = camera != null ? camera.transform.rotation : Quaternion.Euler(16, 0, 0);
        Vector3 center = target.root.position + new Vector3(0, 1.15f, 0);
        SceneView.lastActiveSceneView.LookAt(center, rotation, 3.2f, true, true);
    }

    static FoodVfxScenePreview()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += _ => Stop();
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }

    private static void Stop()
    {
        if (current != null && !Application.isPlaying) current.CancelAll();
        current = null;
        age = 0;
    }

    private static void Update()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Application.isBatchMode) return;
        var rig = Object.FindAnyObjectByType<FoodVfxPreview>();
        if (rig == null || !rig.previewInEditMode || !rig.isActiveAndEnabled ||
            !rig.gameObject.scene.path.StartsWith("Assets/VFX/") || rig.popcorn == null || rig.drink == null)
        {
            Stop();
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        if (current != rig)
        {
            Stop();
            current = rig;
            previousTime = now;
            rig.popcorn.BeginPreparation();
            rig.drink.BeginDispensing();
            Frame(rig.transform);
        }
        float delta = Mathf.Clamp((float)(now - previousTime), 0, .05f);
        if (delta < 1f / 60f) return;
        previousTime = now;
        age += delta;
        foreach (var particle in rig.popcorn.GetComponentsInChildren<ParticleSystem>())
            particle.Simulate(delta, false, false, false);
        foreach (var particle in rig.drink.GetComponentsInChildren<ParticleSystem>())
            particle.Simulate(delta, false, false, false);
        // Keep this inspection preview running continuously; runtime uses the timed cycle.
        if (age > 30)
        {
            rig.popcorn.Cancel();
            rig.drink.Cancel();
            rig.popcorn.BeginPreparation();
            rig.drink.BeginDispensing();
            age = 0;
        }
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }
}

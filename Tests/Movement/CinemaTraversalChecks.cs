using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Copy into Assets/Editor to run. No test objects are saved in the gameplay scene.
[InitializeOnLoad]
public static class CinemaTraversalChecks
{
    const string Request = "Tests/Movement/traversal.request";
    const string Output = ".utmp/cinema-walkability-current";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static Scene source, test;
    static PhysicsScene physics;
    static GameObject actor, fixture;
    static Rigidbody body;
    static CapsuleCollider capsule;
    static PlayerMovement motor;
    static readonly MethodInfo apply = typeof(PlayerMovement).GetMethod("ApplyMovement", Private);
    static readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
    static readonly Dictionary<Collider, Collider> originals = new Dictionary<Collider, Collider>();
    static readonly List<Route> routes = new List<Route>();
    static readonly List<Failure> failures = new List<Failure>();
    static readonly List<Route> invalid = new List<Route>();
    static readonly List<string> checks = new List<string>();
    static readonly List<ColliderPatch> patches = new List<ColliderPatch>();
    static readonly RaycastHit[] hits = new RaycastHit[64];
    static readonly Collider[] overlaps = new Collider[64];
    static int cursor, passed, crouched, warmup, triangleCount;
    static float dt;
    static bool candidate;

    [Serializable] public class Route { public Vector3 from, to, via; public bool useVia, sprint; public string label; }
    [Serializable] public class Failure
    {
        public Route route;
        public string colliderPath, support, obstruction, landing, clearance;
        public long colliderId;
        public Vector3 stopped, contact, normal;
        public float bottom, remaining;
    }
    [Serializable] public class Report
    {
        public string status;
        public bool candidateRamp;
        public int navigationTriangles, copiedColliders, totalRoutes, tested, passed, crouched;
        public string[] fixtureChecks;
        public Failure[] failures;
        public Route[] invalidRoutes;
        public ColliderPatch[] colliderPatches;
    }
    [Serializable] public class ColliderPatch { public long id; public string path; public Vector3 centre, size, oldCentre, oldSize; }

    static CinemaTraversalChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Traversal.Pending", false))
            { SessionState.SetBool("Traversal.Pending", false); warmup = 0; EditorApplication.update += Wait; }
        };
        if (!File.Exists(Request)) return;
        string request = File.ReadAllText(Request);
        if (request.Trim() == "apply")
        { File.Delete(Request); EditorApplication.delayCall += ApplyValidatedRepair; return; }
        candidate = request.Contains("candidate");
        SessionState.SetBool("Traversal.Candidate", candidate); File.Delete(Request);
        EditorApplication.delayCall += Queue;
    }
    [MenuItem("Tools/Pre-Thesis/Player/Verify Cinema Traversal")]
    public static void Queue()
    {
        SessionState.SetBool("Traversal.ExitAfter", !EditorApplication.isPlaying);
        if (EditorApplication.isPlaying) { warmup = 0; EditorApplication.update += Wait; }
        else { SessionState.SetBool("Traversal.Pending", true); EditorApplication.EnterPlaymode(); }
    }
    static void Wait()
    {
        if (++warmup < 25) return;
        EditorApplication.update -= Wait;
        try { Begin(); } catch (Exception e) { Finish("ERROR " + e); }
    }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    static long IdOf(UnityEngine.Object o) => unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId);
    static void Set(string name, object value) => typeof(PlayerMovement).GetField(name, Private).SetValue(motor, value);
    static Transform CopyTransform(Transform original)
    {
        if (copies.TryGetValue(original, out Transform copy)) return copy;
        copy = new GameObject(original.name).transform; SceneManager.MoveGameObjectToScene(copy.gameObject, test);
        if (original.parent != null) copy.SetParent(CopyTransform(original.parent), false);
        copy.localPosition = original.localPosition; copy.localRotation = original.localRotation; copy.localScale = original.localScale;
        copy.gameObject.layer = original.gameObject.layer; copies[original] = copy; return copy;
    }
    static void Begin()
    {
        source = SceneManager.GetSceneByPath("Assets/Scenes/Map/Cinema_GamePlay.unity");
        if (!source.IsValid() || !source.isLoaded) throw new Exception("Open Cinema_GamePlay first.");
        Directory.CreateDirectory(Output); dt = Time.fixedDeltaTime;
        candidate = SessionState.GetBool("Traversal.Candidate", false);
        test = SceneManager.CreateScene("Traversal verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        physics = test.GetPhysicsScene(); copies.Clear(); originals.Clear(); routes.Clear(); failures.Clear(); invalid.Clear(); checks.Clear();
        cursor = passed = crouched = 0; patches.Clear();
        SpawnActor(); RunFixtures();
        foreach (var root in source.GetRootGameObjects()) foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger || c.GetComponentInParent<PlayerMovement>() != null ||
                c.GetComponentInParent<NavMeshAgent>() != null || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) ||
                Physics.GetIgnoreLayerCollision(actor.layer, c.gameObject.layer)) continue;
            var go = CopyTransform(c.transform).gameObject; Collider copy = null;
            if (c is BoxCollider b) { var n = go.AddComponent<BoxCollider>(); n.center = b.center; n.size = b.size; copy = n; }
            else if (c is MeshCollider m) { var n = go.AddComponent<MeshCollider>(); n.sharedMesh = m.sharedMesh; n.convex = m.convex; n.cookingOptions = m.cookingOptions; copy = n; }
            else if (c is CapsuleCollider p) { var n = go.AddComponent<CapsuleCollider>(); n.center = p.center; n.height = p.height; n.radius = p.radius; n.direction = p.direction; copy = n; }
            else if (c is SphereCollider s) { var n = go.AddComponent<SphereCollider>(); n.center = s.center; n.radius = s.radius; copy = n; }
            if (copy == null) continue;
            if (candidate && copy is BoxCollider ramp && c.transform.parent != null && c.transform.parent.name == "InvisibleStair")
            {
                var baseTransform = c.transform.parent.parent.Find("ChairBase");
                var chair = baseTransform == null ? null : baseTransform.GetComponent<MeshCollider>();
                if (chair != null)
                {
                    if (IdOf(c) == 1384262459) ramp.center += ramp.transform.InverseTransformVector(new Vector3(-.22f, 0, .00177f));
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    foreach (Vector3 vertex in chair.sharedMesh.vertices)
                    {
                        float z = ramp.transform.InverseTransformPoint(chair.transform.TransformPoint(vertex)).z;
                        min = Mathf.Min(min, z); max = Mathf.Max(max, z);
                    }
                    Vector3 centre = ramp.center, size = ramp.size;
                    centre.z = (min + max) * .5f; size.z = max - min;
                    ramp.center = centre; ramp.size = size;
                    var old = (BoxCollider)c;
                    patches.Add(new ColliderPatch { id = IdOf(c), path = PathOf(c.transform), centre = centre, size = size, oldCentre = old.center, oldSize = old.size });
                }
            }
            copy.sharedMaterial = c.sharedMaterial; originals[copy] = c;
        }
        Physics.SyncTransforms();
        var nav = NavMesh.CalculateTriangulation(); triangleCount = nav.indices.Length / 3;
        if (triangleCount == 0) throw new Exception("Navigation geometry is not loaded.");
        var edges = new Dictionary<string, Vector3>();
        for (int i = 0; i < nav.indices.Length; i += 3)
        {
            Vector3 a = nav.vertices[nav.indices[i]], b = nav.vertices[nav.indices[i + 1]], c = nav.vertices[nav.indices[i + 2]];
            Vector3 centre = (a + b + c) / 3;
            // Roof navigation polygons are not player walking space. Include
            // separate ground-floor regions, even behind currently closed doors.
            if (centre.y > 7.5f) continue;
            foreach (var edge in new[] { new[] { a, b }, new[] { b, c }, new[] { c, a } })
            {
                string ka = Key(edge[0]), kb = Key(edge[1]); string key = string.CompareOrdinal(ka, kb) < 0 ? ka + "/" + kb : kb + "/" + ka;
                if (edges.TryGetValue(key, out Vector3 other))
                { if (Vector3.Distance(centre, other) > .12f) Add(other, centre, "Navigation", (edge[0] + edge[1]) * .5f); }
                else edges[key] = centre;
            }
        }
        foreach (float z in new[] { 74.7f, 75.73f, 76.75f }) Add(new Vector3(27.5f, 0, z), new Vector3(32.5f, 0, z), "Staff doorway");
        Write("RUNNING"); EditorApplication.update += Tick;
    }
    static void SpawnActor()
    {
        actor = new GameObject("Production traversal motor"); SceneManager.MoveGameObjectToScene(actor, test);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        var p = prefab.GetComponent<CapsuleCollider>(); var r = prefab.GetComponent<Rigidbody>();
        actor.layer = prefab.layer;
        body = actor.AddComponent<Rigidbody>(); body.mass = r.mass; body.constraints = r.constraints;
        body.linearDamping = r.linearDamping; body.angularDamping = r.angularDamping; body.useGravity = r.useGravity;
        body.collisionDetectionMode = r.collisionDetectionMode;
        capsule = actor.AddComponent<CapsuleCollider>(); capsule.radius = p.radius; capsule.height = p.height; capsule.center = p.center; capsule.sharedMaterial = p.sharedMaterial;
        actor.AddComponent<Mirror.NetworkIdentity>(); motor = actor.AddComponent<PlayerMovement>(); motor.enabled = false;
        actor.GetComponent<PlayerStamina>().enabled = false; Set("rb", body); Set("bodyCollider", capsule);
        Set("localDirection", Vector2.up); Set("animationDirection", Vector2.up);
    }
    static void Place(Vector3 floor, bool sprint, bool crouch = false)
    {
        capsule.height = crouch ? 1.2f : 1.55f; capsule.center = new Vector3(0, crouch ? -.175f : 0, .01f);
        Set("localCrouch", crouch); Set("networkCrouch", crouch); Set("isSprinting", sprint);
        actor.transform.position = floor + Vector3.up * (.795f); body.position = actor.transform.position;
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        Set("wasMovementGrounded", false); Set("groundReleaseTime", 0f); Set("movement", Vector3.zero);
        Physics.SyncTransforms(); for (int i = 0; i < 15; i++) Step();
    }
    static void Step() { apply.Invoke(motor, null); physics.Simulate(dt); }
    static string Key(Vector3 v) => Mathf.RoundToInt(v.x * 1000) + "," + Mathf.RoundToInt(v.y * 1000) + "," + Mathf.RoundToInt(v.z * 1000);
    static void Add(Vector3 a, Vector3 b, string label, Vector3? via = null)
    {
        if (Vector3.Distance(a, b) > 35) return;
        foreach (bool sprint in new[] { false, true })
        { routes.Add(new Route { from = a, to = b, via = via.GetValueOrDefault(), useVia = via.HasValue, sprint = sprint, label = label });
          routes.Add(new Route { from = b, to = a, via = via.GetValueOrDefault(), useVia = via.HasValue, sprint = sprint, label = label + " reverse" }); }
    }
    static void Tick()
    {
        try
        {
            for (int i = 0; i < 4 && cursor < routes.Count; i++, cursor++) TestRoute(routes[cursor]);
            if (cursor % 80 == 0) Write("RUNNING");
            if (cursor == routes.Count) Finish("COMPLETE");
        } catch (Exception e) { Finish("ERROR " + e); }
    }
    static void TestRoute(Route route)
    {
        capsule.height = 1.55f;
        bool crouch = false;
        if (!Project(route.from, out Vector3 from) || !Project(route.to, out Vector3 to) || (route.useVia && !Project(route.via, out _)))
        {
            capsule.height = 1.2f; crouch = true;
            if (!Project(route.from, out from) || !Project(route.to, out to) || (route.useVia && !Project(route.via, out _))) { invalid.Add(route); return; }
        }
        Place(from, route.sprint && !crouch, crouch);
        bool reachedVia = !route.useVia; Vector3 previous = body.position; int stalled = 0;
        float length = route.useVia ? Vector3.Distance(route.from, route.via) + Vector3.Distance(route.via, route.to) : Vector3.Distance(from, to);
        int ticks = Mathf.Clamp(Mathf.CeilToInt(length / motor.CurrentMovementSpeed / dt * 3) + 100, 100, 2400);
        for (int i = 0; i < ticks; i++)
        {
            Vector3 direction = (reachedVia ? to : route.via) - body.position; direction.y = 0;
            if (direction.magnitude < .14f) { if (!reachedVia) { reachedVia = true; continue; } passed++; if (crouch) crouched++; return; }
            Set("movement", direction.normalized); Step();
            stalled = Vector3.Distance(previous, body.position) < .003f ? stalled + 1 : 0; previous = body.position;
            if (stalled > 20 || body.position.y < from.y - 3) break;
        }
        var f = new Failure { route = route, stopped = body.position, bottom = capsule.bounds.min.y,
            remaining = Vector2.Distance(new Vector2(to.x, to.z), new Vector2(body.position.x, body.position.z)) };
        Vector3 toward = ((Vector3)typeof(PlayerMovement).GetField("movement", Private).GetValue(motor)).normalized;
        Bounds bounds = capsule.bounds; float radius = capsule.radius;
        Vector3 lower = bounds.center - Vector3.up * (bounds.extents.y - radius), upper = bounds.center + Vector3.up * (bounds.extents.y - radius);
        int count = physics.CapsuleCast(lower, upper, radius - .005f, toward, hits, .35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++) if (hits[i].distance < nearest && originals.TryGetValue(hits[i].collider, out Collider c) && -Vector3.Dot(hits[i].normal, toward) > .05f)
        { nearest = hits[i].distance; f.colliderPath = PathOf(c.transform); f.colliderId = IdOf(c); f.contact = hits[i].point; f.normal = hits[i].normal; }
        object[] args = { default(RaycastHit), 0f };
        bool supported = (bool)typeof(PlayerMovement).GetMethod("TryMovementGround", Private).Invoke(motor, args);
        var support = (RaycastHit)args[0]; f.support = supported + " " + support.point.ToString("F5") + " " + support.normal.ToString("F3");
        object[] obstructionArgs = { lower, upper, radius - .001f, toward, motor.CurrentMovementSpeed * dt + .025f, true, default(RaycastHit) };
        bool blocked = (bool)typeof(PlayerMovement).GetMethod("FindCapsuleObstacle", Private).Invoke(motor, obstructionArgs);
        var obstruction = (RaycastHit)obstructionArgs[6]; f.obstruction = blocked + " " + obstruction.point.ToString("F5") + " d=" + obstruction.distance;
        f.support += " velocity=" + body.linearVelocity.ToString("F5") + " previousGrounded=" + typeof(PlayerMovement).GetField("wasMovementGrounded", Private).GetValue(motor);
        object[] stepArgs = { toward * motor.CurrentMovementSpeed, default(RaycastHit) };
        bool canStep = (bool)typeof(PlayerMovement).GetMethod("TryStepUp", Private).Invoke(motor, stepArgs);
        var landing = (RaycastHit)stepArgs[1];
        f.landing = canStep + " " + (landing.collider == null ? "null" : landing.collider.name) + " " + landing.point.ToString("F5") + " n=" + landing.normal.ToString("F4") + " rise=" + (landing.point.y - bounds.min.y);
        Vector3 riseVector = Vector3.up * (landing.point.y - bounds.min.y + .008f);
        foreach (bool up in new[] { true, false })
        {
            object[] clearanceArgs = { up ? upper : lower + riseVector, upper + (up ? Vector3.zero : riseVector), radius - .01f,
                up ? Vector3.up : toward, up ? riseVector.y : motor.CurrentMovementSpeed * dt + .025f, !up, default(RaycastHit) };
            bool collision = (bool)typeof(PlayerMovement).GetMethod("FindCapsuleObstacle", Private).Invoke(motor, clearanceArgs);
            var obstacle = (RaycastHit)clearanceArgs[6];
            f.clearance += " up=" + up + " blocked=" + collision + " " + (obstacle.collider == null ? "null" : obstacle.collider.name) + " d=" + obstacle.distance + " n=" + obstacle.normal.ToString("F4");
        }
        failures.Add(f);
    }
    static bool Project(Vector3 expected, out Vector3 point)
    {
        point = expected; float nearest = float.PositiveInfinity;
        int count = physics.Raycast(expected + Vector3.up * 3.5f, Vector3.down, hits, 7, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i]; float delta = Mathf.Abs(hit.point.y - expected.y);
            if (hit.normal.y < .643f || !originals.ContainsKey(hit.collider) || delta >= nearest) continue;
            for (float lift = 0; lift <= .401f; lift += .05f)
            {
                Vector3 lower = hit.point + Vector3.up * (capsule.radius + .025f + lift), upper = hit.point + Vector3.up * (capsule.height - capsule.radius + .025f + lift);
                int n = physics.OverlapCapsule(lower, upper, capsule.radius - .005f, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool clear = true; for (int j = 0; j < n; j++) if (overlaps[j] != capsule) { clear = false; break; }
                if (!clear) continue;
                nearest = delta; point = hit.point + Vector3.up * lift; break;
            }
        }
        return nearest < float.PositiveInfinity;
    }
    static void Box(Vector3 position, Vector3 size, float angle = 0)
    {
        var go = new GameObject("Fixture"); go.transform.SetParent(fixture.transform, false);
        go.transform.position = position; go.transform.rotation = Quaternion.Euler(0, 0, angle); go.transform.localScale = size;
        go.AddComponent<BoxCollider>().sharedMaterial = capsule.sharedMaterial;
    }
    static void ResetFixture()
    {
        if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
        fixture = new GameObject("Fixtures"); SceneManager.MoveGameObjectToScene(fixture, test);
    }
    static void Require(bool success, string label) { if (!success) throw new Exception("Fixture failed: " + label); checks.Add("PASS " + label); }
    static void RunFixtures()
    {
        Vector3 gravity = Physics.gravity;
        foreach (float height in new[] { .008f, .08f, .18f, .23f, .29f }) foreach (bool sprint in new[] { false, true })
        {
            ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5)); Box(new Vector3(2, height * .5f, 0), new Vector3(1, height, 3));
            Place(Vector3.zero, sprint); Set("movement", Vector3.right);
            for (int i = 0; i < 180 && body.position.x < 4; i++) Step();
            Require(body.position.x >= 4, (sprint ? "Sprint" : "Walk") + " threshold " + height);
            Set("movement", Vector3.zero); for (int i = 0; i < 50; i++) Step(); Require(Mathf.Abs(capsule.bounds.min.y) < .03f, "Settles after threshold");
        }
        foreach (float width in new[] { .02f, .06f }) foreach (bool sprint in new[] { false, true })
        {
            ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5)); Box(new Vector3(2, .09f, 0), new Vector3(width, .18f, 3));
            Place(Vector3.zero, sprint); Set("movement", Vector3.right);
            for (int i = 0; i < 180 && body.position.x < 4; i++) Step();
            Require(body.position.x >= 4, (sprint ? "Sprint" : "Walk") + " thin trim " + width);
        }
        ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5)); Box(new Vector3(2, .115f, 0), new Vector3(1, .23f, 3));
        Place(Vector3.zero, false, true); Set("movement", Vector3.right);
        for (int i = 0; i < 200 && body.position.x < 4; i++) Step(); Require(body.position.x >= 4, "Crouched threshold");
        foreach (bool sprint in new[] { false, true })
        {
            ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5));
            for (int i = 0; i < 4; i++) Box(new Vector3(2 + i * .18f, .07f * (i + 1), 0), new Vector3(.18f, .14f * (i + 1), 3));
            Place(Vector3.zero, sprint); Set("movement", Vector3.right);
            for (int i = 0; i < 180 && body.position.x < 4; i++) Step(); Require(body.position.x >= 4, (sprint ? "Sprint" : "Walk") + " short staircase");
        }
        ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5)); Box(new Vector3(2, .25f, 0), new Vector3(1, .5f, 3));
        Place(Vector3.zero, true); Set("movement", Vector3.right); for (int i = 0; i < 100; i++) Step(); Require(body.position.x < 1.5f, "Tall wall remains solid");
        ResetFixture(); Box(new Vector3(0, -.1f, 0), new Vector3(30, .2f, 5)); Box(new Vector3(2, .115f, 0), new Vector3(1, .23f, 3)); Box(new Vector3(2, 1.75f, 0), new Vector3(3, .2f, 3));
        Place(Vector3.zero, true); Set("movement", Vector3.right); for (int i = 0; i < 100; i++) Step(); Require(body.position.x < 1.5f && capsule.bounds.max.y < 1.67f, "Step head clearance");
        foreach (float angle in new[] { 20f, 30f, 45f })
        {
            ResetFixture(); float a = angle * Mathf.Deg2Rad; float end = 4 * Mathf.Cos(a) - .1f * Mathf.Sin(a), top = 2 + 4 * Mathf.Sin(a) + .1f * Mathf.Cos(a);
            Box(new Vector3(0, 2, 0), new Vector3(8, .2f, 3), angle); Box(new Vector3(end + 3.99f, top - .1f, 0), new Vector3(8, .2f, 3));
            Place(new Vector3(-2, 2 + .1f / Mathf.Cos(a) - 2 * Mathf.Tan(a), 0), true); Set("movement", Vector3.right);
            float lift = 0; for (int i = 0; i < 240 && body.position.x < end + 3; i++) { Step(); if (body.position.x > end + .25f) lift = Mathf.Max(lift, capsule.bounds.min.y - top); }
            Require(body.position.x > end + 2 && lift < .09f, "Crest " + angle + " lift=" + lift.ToString("F3"));
        }
        ResetFixture(); Box(new Vector3(0, 2, 0), new Vector3(8, .2f, 3), 30); Place(new Vector3(-2, .96077f, 0), true); Set("movement", Vector3.right);
        bool fell = false, noLaunch = true; for (int i = 0; i < 140 && body.position.x < 6; i++) { Step(); if (body.position.x > 3.67f) { noLaunch &= body.linearVelocity.y <= .05f; fell = true; } }
        Require(fell && noLaunch, "Leaves real ledge without upward launch");
        ResetFixture(); Box(new Vector3(0, 2, 0), new Vector3(8, .2f, 3), 30); Place(new Vector3(2.8f, 2.11547f + 2.8f * .57735f, 0), true); Set("movement", Vector3.left);
        float maxGap = 0; for (int i = 0; i < 120 && body.position.x > -2.8f; i++) { Step(); maxGap = Mathf.Max(maxGap, capsule.bounds.min.y - (2.11547f + body.position.x * .57735f)); }
        Require(body.position.x < -2.8f && maxGap < .09f, "Downhill follows support");
        ResetFixture(); Place(new Vector3(0, 5, 0), false); float initial = body.position.y; for (int i = 0; i < 25; i++) Step(); Require(initial - body.position.y > 1 && body.linearVelocity.y < -4, "Free fall");
        ResetFixture(); Box(new Vector3(0, 2, 0), new Vector3(8, .2f, 3), 30); Place(new Vector3(0, 2.11547f, 0), false); Vector3 start = body.position;
        for (int i = 0; i < 100; i++) Step(); Require(Vector3.Distance(start, body.position) < .03f, "Idle slope stability");
        Require(Physics.gravity == gravity, "Global gravity unchanged"); UnityEngine.Object.DestroyImmediate(fixture);
    }
    static void Write(string status)
    {
        var report = new Report { status = status, candidateRamp = candidate, navigationTriangles = triangleCount, copiedColliders = originals.Count,
            totalRoutes = routes.Count, tested = cursor, passed = passed, crouched = crouched, fixtureChecks = checks.ToArray(), failures = failures.ToArray(), invalidRoutes = invalid.ToArray(), colliderPatches = patches.ToArray() };
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(report, true));
    }
    static void Finish(string status)
    {
        EditorApplication.update -= Tick; Write(status);
        if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
        if (test.IsValid()) SceneManager.UnloadSceneAsync(test);
        if (source.IsValid()) SceneManager.SetActiveScene(source);
        Debug.Log("[Cinema traversal] " + status + " passed=" + passed + " failures=" + failures.Count + " invalid=" + invalid.Count);
        if (SessionState.GetBool("Traversal.ExitAfter", false)) EditorApplication.ExitPlaymode();
        SessionState.SetBool("Traversal.ExitAfter", false);
    }

    [MenuItem("Tools/Pre-Thesis/Player/Apply Validated Cinema Ramp Repair")]
    public static void ApplyValidatedRepair()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode before applying the verified repair.");
        var report = JsonUtility.FromJson<Report>(File.ReadAllText(Output + "/report.json"));
        if (report.status != "COMPLETE" || !report.candidateRamp || report.failures.Length != 0 || report.invalidRoutes.Length != 0 || report.colliderPatches.Length != 20)
            throw new Exception("A complete passing candidate audit with all 20 ramp patches is required.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var boxes = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoxCollider>(true)).ToArray();
        string file = File.ReadAllText(scene.path), updated = file;
        var targets = new List<BoxCollider>();
        foreach (ColliderPatch patch in report.colliderPatches)
        {
            BoxCollider box = boxes.Single(c => IdOf(c) == patch.id);
            if (box.center != patch.oldCentre || box.size != patch.oldSize || PathOf(box.transform) != patch.path)
                throw new Exception("Collider changed since validation: " + patch.path);
            string marker = "--- !u!65 &" + patch.id + "\r\n";
            if (!updated.Contains(marker)) marker = marker.Replace("\r\n", "\n");
            int begin = updated.IndexOf(marker, StringComparison.Ordinal), end = updated.IndexOf("--- !u!", begin + marker.Length, StringComparison.Ordinal);
            if (begin < 0 || end < 0) throw new Exception("Cannot locate exact collider record: " + patch.id);
            string block = updated.Substring(begin, end - begin);
            string replacement = Regex.Replace(block, @"(?m)^  m_Size: \{[^\r\n]+\}", "  m_Size: " + Yaml(patch.size));
            replacement = Regex.Replace(replacement, @"(?m)^  m_Center: \{[^\r\n]+\}", "  m_Center: " + Yaml(patch.centre));
            updated = updated.Substring(0, begin) + replacement + updated.Substring(end);
            targets.Add(box);
        }
        // Persist only these collider fields, preserving every other on-disk
        // scene value and any independent unsaved edits in the open Editor.
        File.WriteAllText(scene.path, updated, new UTF8Encoding(false));
        for (int i = 0; i < targets.Count; i++)
        {
            Undo.RecordObject(targets[i], "Repair cinema traversal ramps");
            targets[i].center = report.colliderPatches[i].centre; targets[i].size = report.colliderPatches[i].size;
            EditorUtility.SetDirty(targets[i]);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText(Output + "/repair-applied.txt", "20 validated BoxColliders updated in the saved scene and open Editor. Other scene fields preserved.");
    }
    static string Yaml(Vector3 v) => "{x: " + v.x.ToString("R", CultureInfo.InvariantCulture) + ", y: " + v.y.ToString("R", CultureInfo.InvariantCulture) + ", z: " + v.z.ToString("R", CultureInfo.InvariantCulture) + "}";
}

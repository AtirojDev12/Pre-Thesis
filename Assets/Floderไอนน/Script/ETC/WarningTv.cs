using Mirror;
using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(AudioSource), typeof(VideoPlayer), typeof(NetworkIdentity))]
public class WarningTV : InteractableBase
{
    private AudioSource audioSource;
    private VideoPlayer videoPlayer;
    [SyncVar] private double startedAt;
    [SyncVar(hook = nameof(OnPlaybackChanged))] private bool playing;
    private double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    protected override void Awake()
    {
        base.Awake();
        audioSource = GetComponent<AudioSource>();
        videoPlayer = GetComponent<VideoPlayer>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        // The separate narration is the only audio for this TV.
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        if (videoPlayer.targetMaterialRenderer == null)
            videoPlayer.targetMaterialRenderer = GetComponentInChildren<MeshRenderer>();
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialProperty = "_BaseMap";

        // Imported TV meshes have no collider; interaction raycasts need one.
        if (GetComponentInChildren<Collider>() == null)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = new Bounds(transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
                foreach (Renderer renderer in renderers)
                {
                    Bounds world = renderer.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = world.center + Vector3.Scale(world.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        bounds.Encapsulate(transform.InverseTransformPoint(corner));
                    }
                }
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPlayback();
    }

    public override string GetInteractionPrompt() => playing ? "Press E to Stop Media" : "Press E to Play Media";

    protected override void OnInteracted(GameObject interactor)
    {
        if (playing) playing = false;
        else if (audioSource.clip != null)
        {
            startedAt = Now;
            playing = true;
        }
        // Also handles offline play and a dedicated server (which has no client hook).
        ApplyPlayback();
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && playing &&
            (audioSource.clip == null || Now - startedAt >= audioSource.clip.length))
        {
            playing = false;
            ApplyPlayback();
        }
    }

    private void OnPlaybackChanged(bool previous, bool current) => ApplyPlayback();

    private void ApplyPlayback()
    {
        if (audioSource == null || videoPlayer == null) return;
        if (!playing)
        {
            audioSource.Stop();
            videoPlayer.Stop();
            return;
        }
        if (audioSource.clip == null || audioSource.isPlaying) return;
        double elapsed = System.Math.Max(0, Now - startedAt);
        if (elapsed >= audioSource.clip.length) return;
        audioSource.time = (float)elapsed;
        audioSource.Play();
        if (videoPlayer.clip != null || !string.IsNullOrEmpty(videoPlayer.url)) videoPlayer.Play();
    }

    private void OnDisable()
    {
        if (audioSource != null) audioSource.Stop();
        if (videoPlayer != null) videoPlayer.Stop();
    }
}

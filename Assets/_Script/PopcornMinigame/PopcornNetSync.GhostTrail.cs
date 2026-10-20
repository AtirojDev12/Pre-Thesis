using Mirror;

public partial class PopcornNetSync
{
    [SyncVar] private GhostFavorTrailSnapshot ghostTrail;
    public GhostFavorTrailSnapshot GhostTrail => ghostTrail;

    public void PublishGhostTrail(GhostFavorTrailSnapshot snapshot)
    {
        if (HasAuthority) ghostTrail = snapshot;
    }
}

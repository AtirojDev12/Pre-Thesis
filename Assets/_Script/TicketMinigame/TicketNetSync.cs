using Mirror;
using UnityEngine;

// Separate from the always-active TicketSell group: Mirror disables scene identities offline.
[RequireComponent(typeof(NetworkIdentity))]
public sealed class TicketNetSync : NetworkBehaviour
{
    [SerializeField] private TicketMinigame minigame;
    [SyncVar] private TicketCustomerState state;
    public TicketCustomerState State => state;
    // Scene identities are inactive in a player build until Mirror spawns them.
    // NetworkBehaviour.isClient/isServer dereference netIdentity, which is not
    // assigned until NetworkIdentity.Awake. The always-active counter can run first.
    public bool IsClientReady => netIdentity != null && isClient;
    public bool IsServerReady => netIdentity != null && isServer;

    public void Publish(TicketCustomerState value)
    {
        if (IsServerReady) state = value;
    }

    public void RequestSale(bool ghostTicket, int round)
    {
        if (IsClientReady) CmdSell(ghostTicket, round);
    }

    public void RequestMovie(int movieIndex, int round)
    {
        if (IsClientReady) CmdSelectMovie(movieIndex, round);
    }

    [Command(requiresAuthority = false)]
    private void CmdSelectMovie(int movieIndex, int round, NetworkConnectionToClient sender = null)
    {
        if (minigame == null || sender == null || sender.identity == null) return;
        minigame.ResolveMovie(movieIndex, round, sender.identity.GetComponent<PlayerHealth>());
    }

    [Command(requiresAuthority = false)]
    private void CmdSell(bool ghostTicket, int round, NetworkConnectionToClient sender = null)
    {
        if (minigame == null || sender == null || sender.identity == null) return;
        minigame.ResolveSale(ghostTicket, round, sender.identity.GetComponent<PlayerHealth>());
    }
}

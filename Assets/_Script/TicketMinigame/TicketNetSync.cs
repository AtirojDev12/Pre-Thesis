using Mirror;
using UnityEngine;

// Separate from the always-active TicketSell group: Mirror disables scene identities offline.
[RequireComponent(typeof(NetworkIdentity))]
public sealed class TicketNetSync : NetworkBehaviour
{
    [SerializeField] private TicketMinigame minigame;
    [SyncVar] private TicketCustomerState state;
    public TicketCustomerState State => state;

    public void Publish(TicketCustomerState value)
    {
        if (isServer) state = value;
    }

    public void RequestSale(bool ghostTicket, int round) => CmdSell(ghostTicket, round);

    public void RequestMovie(int movieIndex, int round) => CmdSelectMovie(movieIndex, round);

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

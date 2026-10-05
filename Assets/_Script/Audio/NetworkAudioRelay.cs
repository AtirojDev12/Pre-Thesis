using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct WorldSoundMessage : NetworkMessage
{
    public string id;
    public Vector3 position;
    public int clipIndex;
}

/// <summary>Only authoritative gameplay calls Play. No client request handler exists.</summary>
public static class NetworkAudioRelay
{
    public static void RegisterClient() => NetworkClient.RegisterHandler<WorldSoundMessage>(Receive);
    public static void UnregisterClient() => NetworkClient.UnregisterHandler<WorldSoundMessage>();

    public static void Play(string id, Vector3 position, int clipIndex = 0)
    {
        if (NetworkMode.IsOffline)
        {
            AudioManager.Instance?.Play(id, position, SceneManager.GetActiveScene(), clipIndex);
            return;
        }
        if (!NetworkServer.active || NetworkMode.SessionEnding) return;
        // Reliable delivery; host playback happens through this same message exactly once.
        var message = new WorldSoundMessage { id = id, position = position, clipIndex = clipIndex };
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            if (connection != null && connection.isReady) connection.Send(message);
    }

    private static void Receive(WorldSoundMessage message)
    {
        if (NetworkMode.SessionEnding) return;
        AudioManager.Instance?.Play(message.id, message.position,
            SceneManager.GetActiveScene(), message.clipIndex);
    }
}

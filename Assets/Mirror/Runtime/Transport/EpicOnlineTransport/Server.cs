using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpicTransport {
    public class Server : Common {
        private event Action<int> OnConnected;
        private event Action<int, byte[], int> OnReceivedData;
        private event Action<int> OnDisconnected;
        private event Action<int, Exception> OnReceivedError;

        private BidirectionalDictionary<ProductUserId, int> epicToMirrorIds;
        private Dictionary<ProductUserId, SocketId> epicToSocketIds;
        private int maxConnections;
        private int nextConnectionID;
        // 13RoH (2 Oct): users we just disconnected (kick). Their last packets
        // still arrive for a moment; ignore them quietly instead of red errors.
        private readonly HashSet<ProductUserId> recentlyDisconnected = new HashSet<ProductUserId>();

        public static Server CreateServer(EosTransport transport, int maxConnections) {
            Server s = new Server(transport, maxConnections);

            s.OnConnected += (id) => transport.OnServerConnected.Invoke(id);
            s.OnDisconnected += (id) => transport.OnServerDisconnected.Invoke(id);
            s.OnReceivedData += (id, data, channel) => transport.OnServerDataReceived.Invoke(id, new ArraySegment<byte>(data), channel);
            s.OnReceivedError += (id, exception) => transport.OnServerError.Invoke(id, TransportError.Unexpected, exception.ToString());
            if (!EOSSDKComponent.Initialized) {
                Debug.LogError("EOS not initialized.");
            }

            return s;
        }

        private Server(EosTransport transport, int maxConnections) : base(transport) {
            this.maxConnections = maxConnections;
            epicToMirrorIds = new BidirectionalDictionary<ProductUserId, int>();
            epicToSocketIds = new Dictionary<ProductUserId, SocketId>();
            nextConnectionID = 1;
        }

        protected override void OnNewConnection(OnIncomingConnectionRequestInfo result) {
            if (ignoreAllMessages) {
                return;
            }

            if (deadSockets.Contains(result.SocketId.SocketName)) {
                Debug.LogError("Received incoming connection request from dead socket");
                return;
            }

            EOSSDKComponent.GetP2PInterface().AcceptConnection(
                new AcceptConnectionOptions() {
                LocalUserId = EOSSDKComponent.LocalUserProductId,
                RemoteUserId = result.RemoteUserId,
                SocketId = result.SocketId
                });
        }

        protected override void OnReceiveInternalData(InternalMessages type, ProductUserId clientUserId, SocketId socketId) {
            if (ignoreAllMessages) {
                return;
            }

            switch (type) {
                case InternalMessages.CONNECT:
                    if (epicToMirrorIds.Count >= maxConnections) {
                        Debug.LogError("Reached max connections");
                        //CloseP2PSessionWithUser(clientUserId, socketId);
                        SendInternal(clientUserId, socketId, InternalMessages.DISCONNECT);
                        return;
                    }

                    SendInternal(clientUserId, socketId, InternalMessages.ACCEPT_CONNECT);

                    recentlyDisconnected.Remove(clientUserId);
                    int connectionId = nextConnectionID++;
                    epicToMirrorIds.Add(clientUserId, connectionId);
                    epicToSocketIds.Add(clientUserId, socketId);
                    OnConnected.Invoke(connectionId);

                    string clientUserIdString;
                    clientUserId.ToString(out clientUserIdString);
                    Debug.Log($"Client with Product User ID {clientUserIdString} connected. Assigning connection id {connectionId}");
                    break;
                case InternalMessages.DISCONNECT:
                    if (epicToMirrorIds.TryGetValue(clientUserId, out int connId)) {
                        OnDisconnected.Invoke(connId);
                        //CloseP2PSessionWithUser(clientUserId, socketId);
                        epicToMirrorIds.Remove(clientUserId);
                        epicToSocketIds.Remove(clientUserId);
                        Debug.Log($"Client with Product User ID {clientUserId} disconnected.");
                    } else {
                        OnReceivedError.Invoke(-1, new Exception("ERROR Unknown Product User ID"));
                    }

                    break;
                default:
                    Debug.Log("Received unknown message type");
                    break;
            }
        }

        protected override void OnReceiveData(byte[] data, ProductUserId clientUserId, int channel) {
            if (ignoreAllMessages) {
                return;
            }

            if (epicToMirrorIds.TryGetValue(clientUserId, out int connectionId)) {
                OnReceivedData.Invoke(connectionId, data, channel);
            } else if (recentlyDisconnected.Contains(clientUserId)) {
                // Leftover packets from a player we just disconnected: drop them.
            } else {
                SocketId socketId;
                epicToSocketIds.TryGetValue(clientUserId, out socketId);
                CloseP2PSessionWithUser(clientUserId, socketId);

                string productId;
                clientUserId.ToString(out productId);

                Debug.LogError("Data received from epic client thats not known " + productId);
                OnReceivedError.Invoke(-1, new Exception("ERROR Unknown product ID"));
            }
        }

        // 13RoH fix (2 Oct): a server-side disconnect (kick, wrong password, room full)
        // never told Mirror the connection was gone, so Mirror kept the player,
        // its lobby body and seat alive forever ("Trying to send on unknown
        // connection"). Now Mirror is told, like every other transport does.
        public void Disconnect(int connectionId) => Disconnect(connectionId, true);

        private void Disconnect(int connectionId, bool notifyMirror) {
            if (epicToMirrorIds.TryGetValue(connectionId, out ProductUserId userId)) {
                SocketId socketId;
                epicToSocketIds.TryGetValue(userId, out socketId);
                SendInternal(userId, socketId, InternalMessages.DISCONNECT);
                epicToMirrorIds.Remove(userId);
                epicToSocketIds.Remove(userId);
                recentlyDisconnected.Add(userId);
                if (notifyMirror) OnDisconnected?.Invoke(connectionId);
            } else {
                Debug.LogWarning("Trying to disconnect unknown connection id: " + connectionId);
            }
        }

        public void Shutdown() {
            // Copy first: Disconnect removes entries while we walk the list.
            foreach (ProductUserId userId in new List<ProductUserId>(epicToMirrorIds.FirstTypes)) {
                if (!epicToMirrorIds.TryGetValue(userId, out int connectionId)) continue;
                Disconnect(connectionId, false);
                SocketId socketId;
                epicToSocketIds.TryGetValue(userId, out socketId);
                WaitForClose(userId, socketId);
            }

            ignoreAllMessages = true;
            ReceiveData();

            Dispose();
        }

        public void SendAll(int connectionId, byte[] data, int channelId) {
            if (epicToMirrorIds.TryGetValue(connectionId, out ProductUserId userId)) {
                SocketId socketId;
                epicToSocketIds.TryGetValue(userId, out socketId);
                Send(userId, socketId, data, (byte)channelId);
            } else {
                Debug.LogError("Trying to send on unknown connection: " + connectionId);
                OnReceivedError.Invoke(connectionId, new Exception("ERROR Unknown Connection"));
            }

        }

        public string ServerGetClientAddress(int connectionId) {
            if (epicToMirrorIds.TryGetValue(connectionId, out ProductUserId userId)) {
                string userIdString;
                userId.ToString(out userIdString);
                return userIdString;
            } else {
                Debug.LogError("Trying to get info on unknown connection: " + connectionId);
                OnReceivedError.Invoke(connectionId, new Exception("ERROR Unknown Connection"));
                return string.Empty;
            }
        }

        protected override void OnConnectionFailed(ProductUserId remoteId) {
            if (ignoreAllMessages) {
                return;
            }

            int connectionId = epicToMirrorIds.TryGetValue(remoteId, out int connId) ? connId : nextConnectionID++;
            OnDisconnected.Invoke(connectionId);

            Debug.LogError("Connection Failed, removing user");
            epicToMirrorIds.Remove(remoteId);
            epicToSocketIds.Remove(remoteId);
        }
    }
}
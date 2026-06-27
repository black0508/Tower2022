using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 自定义网络管理器：覆写连接回调以提供连接状态的日志反馈。
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        // ========== Server 回调 ==========

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[Server] Client connected: connId={conn.connectionId}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[Server] Client disconnected: connId={conn.connectionId}");
        }

        // ========== Client 回调 ==========

        public override void OnClientConnect()
        {
            Debug.Log("[Client] Connected to server!");
        }

        public override void OnClientDisconnect()
        {
            Debug.Log("[Client] Disconnected from server.");
        }

        public override void OnClientError(TransportError error, string reason)
        {
            Debug.LogError($"[Client] Connection error: {error} — {reason}");
        }
    }
}
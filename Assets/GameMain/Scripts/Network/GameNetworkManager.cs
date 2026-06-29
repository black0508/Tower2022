using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 自定义网络管理器：覆写连接回调以提供连接状态的日志反馈。
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("[Server] Server started");

            var spawner = FindObjectOfType<EnemySpawner>();
            if (spawner != null)
                spawner.StartSpawning();
            else
                Debug.LogWarning("[Server] No EnemySpawner found in scene.");
        }

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
            // 必须调用 base：内部会 NetworkClient.Ready()，否则客户端收不到 Spawn 消息
            base.OnClientConnect();
            Debug.Log("[Client] Connected to server! (ready=" + NetworkClient.ready + ")");
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
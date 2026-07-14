using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    /// <summary>
    /// 玩家管理（双端）：玩家列表、Spawn、Ready 判断、加入补同步。
    /// 从 GameState 拆出来，避免 GameState 单类过重。
    /// </summary>
    public class PlayerManagerComponent : GameFrameworkComponent
    {
        readonly List<GamePlayer> m_Players = new();
        GamePlayer m_LocalPlayer;

        [SerializeField] GameObject gamePlayerPrefab;

        public IReadOnlyList<GamePlayer> Players => m_Players;

        public GamePlayer GetLocalPlayer() => m_LocalPlayer;

        public void RegisterPlayer(GamePlayer player)
        {
            if (player == null) return;
            if (!m_Players.Contains(player))
                m_Players.Add(player);
            if (player.isLocalPlayer)
                m_LocalPlayer = player;
        }

        public void UnregisterPlayer(GamePlayer player)
        {
            if (player == null) return;
            m_Players.Remove(player);
            if (m_LocalPlayer == player)
                m_LocalPlayer = null;
        }

        public bool AreAllPlayersReady()
        {
            if (m_Players.Count == 0) return false;
            foreach (var p in m_Players)
                if (!p.isReady) return false;
            return true;
        }

        /// <summary>仅服务端：为连接创建 GamePlayer 并补同步战斗信息。</summary>
        public void SpawnPlayer(NetworkConnectionToClient conn)
        {
            if (conn.identity != null)
            {
                Log.Warning($"[Server] Player already spawned for conn {conn.connectionId}, skip.");
                return;
            }
            if (gamePlayerPrefab == null)
            {
                Log.Error("[Server] gamePlayerPrefab not assigned on PlayerManagerComponent.");
                return;
            }

            var go = Instantiate(gamePlayerPrefab);
            go.name = $"{gamePlayerPrefab.name} [connId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, go);

            // TargetRpc 必须走 NetworkBehaviour，委托给 GameState
            GameEntry.State?.SendBattleInfoTo(conn);

            Log.Info($"[Server] Player added for connection {conn.connectionId}");
        }
    }
}

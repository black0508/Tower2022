using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 自定义网络管理器：Spawn GameState，玩家由 PlayerManager 创建。
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        [Header("Custom")]
        [SerializeField] GameState gameStatePrefab;

        // 运行时从 spawnPrefabs 拆出；默认注册只留下非池化对象
        readonly List<GameObject> poolablePrefabs = new();

        public override void Awake()
        {
            ExtractPoolablePrefabs();
            base.Awake();
        }

        // 扫一遍所有可池化的网络对象，然后加入池化列表
        void ExtractPoolablePrefabs()
        {
            poolablePrefabs.Clear();
            for (int i = spawnPrefabs.Count - 1; i >= 0; i--)
            {
                var prefab = spawnPrefabs[i];
                if (prefab == null) continue;
                if (prefab.GetComponent<Enemy>() == null && prefab.GetComponent<ProjectileBase>() == null)
                    continue;

                poolablePrefabs.Add(prefab);
                spawnPrefabs.RemoveAt(i);
            }
        }

        public override void OnStartServer()
        {
            autoCreatePlayer = false;
            base.OnStartServer();
            Debug.Log("[Server] Server started");
            SpawnGameState();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RegisterPoolablePrefabs();
        }

        void RegisterPoolablePrefabs()
        {
            var pool = GameEntry.NetworkPool;
            if (pool == null)
            {
                Debug.LogError("[Client] NetworkObjectPool not initialized.");
                return;
            }

            foreach (var prefab in poolablePrefabs)
            {
                if (prefab == null) continue;
                pool.RegisterClientHandlers(prefab);
            }
        }

        void SpawnGameState()
        {
            if (GameEntry.State != null) return;
            if (gameStatePrefab == null)
            {
                Debug.LogError("[Server] GameState prefab not assigned on GameNetworkManager.");
                return;
            }

            var entry = FindObjectOfType<GameEntry>();
            if (entry == null)
            {
                Debug.LogError("[Server] GameEntry not found, cannot spawn GameState.");
                return;
            }

            var state = Instantiate(gameStatePrefab, entry.transform);
            state.name = "GameState";
            NetworkServer.Spawn(state.gameObject);
            Debug.Log("[Server] GameState spawned under GameEntry.");
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[Server] Client connected: connId={conn.connectionId}");

            var pm = GameEntry.PlayerManager;
            if (pm == null)
            {
                Debug.LogError("[Server] PlayerManager not ready, cannot spawn player.");
                return;
            }
            pm.SpawnPlayer(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log("[Client] Connected to server! (ready=" + NetworkClient.ready + ")");
        }

        public override void OnClientDisconnect()
        {
            Debug.Log("[Client] Disconnected from server.");
            TryCloseGamingForm();
        }

        static void TryCloseGamingForm()
        {
            if (GameEntry.UI == null) return;

            var voting = GameEntry.UI.GetUIForm(UIFormId.VotingForm);
            if (voting != null)
                GameEntry.UI.CloseUIForm(voting);

            var form = GameEntry.UI.GetUIForm(UIFormId.GamingForm);
            if (form != null)
                GameEntry.UI.CloseUIForm(form);
        }

        public override void OnClientError(TransportError error, string reason)
        {
            Debug.LogError($"[Client] Connection error: {error} — {reason}");
        }
    }
}

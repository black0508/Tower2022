using System.Collections.Generic;
using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 整局游戏阶段：准备 → 波次 → 波间 → 胜负。
    /// </summary>
    public enum GamePhase
    {
        Preparing,
        Wave,
        BetweenWaves,
        Victory,
        Defeat,
    }

    /// <summary>
    /// 服务端权威的全局游戏状态（共享金币、阶段机、玩家列表）。由 GameNetworkManager 在服务器启动时从预制体 Spawn。
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        readonly List<GamePlayer> m_Players = new();

        [SyncVar(hook = nameof(OnGoldChanged))]
        public int sharedGold = 100;

        [SyncVar(hook = nameof(OnPhaseChanged))]
        public GamePhase phase = GamePhase.Preparing;

        /// <summary>当前波次（1-based），0 表示尚未开战。</summary>
        [SyncVar(hook = nameof(OnCurrentWaveChanged))]
        public int currentWave;
        [SyncVar] public float betweenWavesTimer;

        /// <summary>总波次：服务端由 config 写入，客户端由 RpcStartBattle 同步。</summary>
        public int totalWaves;

        [SerializeField] GameObject gamePlayerPrefab;

        WaveManager m_WaveManager;

        public IReadOnlyList<GamePlayer> Players => m_Players;
        public WaveManager WaveManager => m_WaveManager;
        public int TotalWaves => totalWaves;

        public void RegisterPlayer(GamePlayer player)
        {
            if (player != null && !m_Players.Contains(player))
                m_Players.Add(player);
        }

        public void UnregisterPlayer(GamePlayer player)
        {
            if (player != null)
                m_Players.Remove(player);
        }

        public override void OnStartServer()
        {
            GameEntry.RegisterState(this);
            GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);

            EnsureWaveManager();
            if (m_WaveManager == null)
                Debug.LogError("[Server] WaveManager not found in scene.");
            else
            {
                m_WaveManager.InitServer();
                totalWaves = m_WaveManager.config != null ? m_WaveManager.config.waves.Length : 0;
            }
        }

        public override void OnStartClient()
        {
            GameEntry.RegisterState(this);
            EnsureWaveManager();

            if (GameEntry.UI != null && !GameEntry.UI.HasUIForm(UIFormId.GamingForm))
                GameEntry.UI.OpenUIForm(UIFormId.GamingForm);
        }

        void EnsureWaveManager()
        {
            if (m_WaveManager == null)
                m_WaveManager = FindObjectOfType<WaveManager>();
        }

        void OnDestroy()
        {
            GameEntry.UnregisterState(this);
            GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
        }

        void Update()
        {
            if (!isServer) return;
            TickPhase(Time.deltaTime);
        }

        [Server]
        public void SpawnPlayer(NetworkConnectionToClient conn)
        {
            if (conn.identity != null) return;
            if (gamePlayerPrefab == null)
            {
                Debug.LogError("[Server] gamePlayerPrefab not assigned on GameState.");
                return;
            }

            var go = Instantiate(gamePlayerPrefab);
            go.name = $"{gamePlayerPrefab.name} [connId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, go);
            TargetSyncBattleInfo(conn, totalWaves);
            Debug.Log($"[Server] Player added for connection {conn.connectionId}");
        }

        [Server]
        void TickPhase(float deltaTime)
        {
            switch (phase)
            {
                case GamePhase.Preparing:
                    TickPreparing();
                    break;
                case GamePhase.Wave:
                    TickWave();
                    break;
                case GamePhase.BetweenWaves:
                    TickBetweenWaves(deltaTime);
                    break;
            }
        }

        [Server]
        void TickPreparing()
        {
            if (AreAllPlayersReady())
                TransitionTo(GamePhase.BetweenWaves);
        }

        [Server]
        void TickWave()
        {
            if (m_WaveManager == null) return;

            m_WaveManager.ServerTick();
            if (!m_WaveManager.IsCurrentWaveCleared) return;

            if (m_WaveManager.IsLastWave)
                TransitionTo(GamePhase.Victory);
            else
                TransitionTo(GamePhase.BetweenWaves);
        }

        [Server]
        void TickBetweenWaves(float deltaTime)
        {
            betweenWavesTimer -= deltaTime;
            if (betweenWavesTimer > 0) return;

            currentWave++;
            m_WaveManager?.StartWave(currentWave - 1, skipDelayBeforeWave: true);
            TransitionTo(GamePhase.Wave);
        }
        [Server]
        void TransitionTo(GamePhase newPhase)
        {
            Debug.Log($"[Server] Phase: {phase} -> {newPhase}");
            phase = newPhase;

            switch (newPhase)
            {
                case GamePhase.Wave:
                    break;
                case GamePhase.BetweenWaves:
                    betweenWavesTimer = GetDelayBeforeWave(currentWave);
                    if (currentWave <= 0)
                        RpcStartBattle(totalWaves);
                    break;
                case GamePhase.Victory:
                    Debug.Log("[Server] === VICTORY ===");
                    break;
                case GamePhase.Defeat:
                    Debug.Log("[Server] === DEFEAT ===");
                    break;
            }
        }

        [Server]
        float GetDelayBeforeWave(int waveIndex)
        {
            var config = m_WaveManager?.config;
            if (config == null || waveIndex < 0 || waveIndex >= config.waves.Length)
                return 0;
            return config.waves[waveIndex].delayBeforeWave;
        }

        [Server]
        bool AreAllPlayersReady()
        {
            if (m_Players.Count == 0) return false;
            foreach (var p in m_Players)
            {
                if (!p.isReady) return false;
            }
            return true;
        }

        [Server]
        public void NotifyDefeat()
        {
            if (phase == GamePhase.Defeat || phase == GamePhase.Victory) return;
            TransitionTo(GamePhase.Defeat);
        }

        void OnEnemyKilled(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            if (e is EnemyKilledEventArgs args && args.Reason == EnemyRemoveReason.KilledByPlayer)
                sharedGold += args.GoldAmount;
        }

        [Server]
        public bool TrySpend(int amount)
        {
            if (sharedGold < amount) return false;
            sharedGold -= amount;
            return true;
        }

        void OnGoldChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
        }

        void OnPhaseChanged(GamePhase oldVal, GamePhase newVal)
        {
            GameEntry.Event.Fire(this, GamePhaseChangedEventArgs.Create(newVal));
        }

        void OnCurrentWaveChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(newVal));
        }

        [ClientRpc]
        void RpcStartBattle(int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }

        [TargetRpc]
        void TargetSyncBattleInfo(NetworkConnectionToClient _, int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }
    }
}

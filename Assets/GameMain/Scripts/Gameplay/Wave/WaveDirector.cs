using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 服务端专属：阶段状态机 + 波次推进 + 击杀奖励监听。
    /// 由 GameState.OnStartServer 动态 AddComponent 添加（不挂 Prefab）。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        GameState gameState;
        WaveManager m_WaveManager;

        /// <summary>由 GameState.OnStartServer 调用，注入依赖并初始化。</summary>
        public void InitServer(GameState state)
        {
            gameState = state;

            EnsureWaveManager();
            if (m_WaveManager == null)
            {
                Debug.LogError("[Server] WaveManager not found in scene.");
                enabled = false;
                return;
            }

            m_WaveManager.InitServer();
            gameState.totalWaves = m_WaveManager.config != null ? m_WaveManager.config.waves.Length : 0;

            GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void OnDestroy()
        {
            GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void EnsureWaveManager()
        {
            if (m_WaveManager == null)
                m_WaveManager = FindObjectOfType<WaveManager>();
        }

        void Update()
        {
            if (gameState == null) return;
            if (!NetworkServer.active) return;

            TickPhase(Time.deltaTime);
        }

        void TickPhase(float deltaTime)
        {
            switch (gameState.phase)
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

        void TickPreparing()
        {
            var pm = GameEntry.PlayerManager;
            if (pm != null && pm.AreAllPlayersReady())
                TransitionTo(GamePhase.BetweenWaves);
        }

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

        void TickBetweenWaves(float deltaTime)
        {
            gameState.betweenWavesTimer -= deltaTime;
            if (gameState.betweenWavesTimer > 0) return;

            gameState.currentWave++;
            m_WaveManager?.StartWave(gameState.currentWave - 1, skipDelayBeforeWave: true);
            TransitionTo(GamePhase.Wave);
        }

        void TransitionTo(GamePhase newPhase)
        {
            Debug.Log($"[Server] Phase: {gameState.phase} -> {newPhase}");
            gameState.phase = newPhase;

            switch (newPhase)
            {
                case GamePhase.BetweenWaves:
                    gameState.betweenWavesTimer = GetDelayBeforeWave(gameState.currentWave);
                    if (gameState.currentWave <= 0)
                    {
                        // 对局真正开始（全员 Ready → 开战）：清空上一局残留占用
                        GameEntry.Build?.ClearAllOccupancy();
                        gameState.BroadcastStartBattle();
                    }
                    break;
                case GamePhase.Victory:
                    Debug.Log("[Server] === VICTORY ===");
                    break;
                case GamePhase.Defeat:
                    Debug.Log("[Server] === DEFEAT ===");
                    break;
            }
        }

        float GetDelayBeforeWave(int waveIndex)
        {
            var config = m_WaveManager?.config;
            if (config == null || waveIndex < 0 || waveIndex >= config.waves.Length)
                return 0;
            return config.waves[waveIndex].delayBeforeWave;
        }

        void OnEnemyRemoved(object sender, GameEventArgs e)
        {
            if (!NetworkServer.active) return;
            if (e is EnemyRemovedEventArgs args && args.Reason == EnemyRemoveReason.KilledByPlayer)
                gameState.AddGold(args.GoldAmount);
        }
    }
}

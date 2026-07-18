using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 玩家网络对象：服务端权威建造与 Ready 状态。
    /// </summary>
    public class GamePlayer : NetworkBehaviour
    {
        [SyncVar] public int playerId;
        [SyncVar] public string playerName = "Player";

        [SyncVar(hook = nameof(OnReadyChanged))]
        public bool isReady;

        public override void OnStartServer()
        {
            playerId = (int)netId;
            GameEntry.PlayerManager?.RegisterPlayer(this);
        }

        public override void OnStartClient()
        {
            if (isServer) return; // Host 已在 OnStartServer 注册
            GameEntry.PlayerManager?.RegisterPlayer(this);
        }

        public override void OnStartLocalPlayer()
        {
            // isLocalPlayer 此时一定为 true，补一次本地缓存
            GameEntry.PlayerManager?.RegisterPlayer(this);
            gameObject.AddComponent<PlayerBuildModeComponent>().InitLocal(this);
        }

        void OnDestroy()
        {
            GameEntry.PlayerManager?.UnregisterPlayer(this);
        }

        [Command]
        public void CmdSetReady(bool ready)
        {
            var state = GameEntry.State;
            if (state == null || state.phase != GamePhase.Preparing) return;
            isReady = ready;
        }

        [Command]
        public void CmdVote(int optionIndex)
        {
            var state = GameEntry.State;
            if (state == null || state.phase != GamePhase.Voting) return;
            if (optionIndex < 0 || optionIndex >= state.voteOptions.Count) return;

            state.playerVotes[playerId] = optionIndex;
        }

        void OnReadyChanged(bool oldVal, bool newVal)
        {
            GameEntry.Event.Fire(this, PlayerReadyChangedEventArgs.Create(playerId, newVal));
        }

        [Command]
        public void CmdBuildTower(uint slotNetId, int towerConfigId)
        {
            if (!NetworkServer.spawned.TryGetValue(slotNetId, out var identity))
            {
                TargetBuildResult(connectionToClient, false, "Slot not found");
                return;
            }

            var slot = identity.GetComponent<BuildSlot>();
            if (slot == null)
            {
                TargetBuildResult(connectionToClient, false, "NetId is not a BuildSlot");
                return;
            }

            if (slot.IsOccupied)
            {
                TargetBuildResult(connectionToClient, false, "Slot occupied");
                return;
            }

            int cost = GetTowerCost(towerConfigId);
            if (GameEntry.State == null || !GameEntry.State.TrySpend(cost))
            {
                TargetBuildResult(connectionToClient, false, "Not enough gold");
                return;
            }

            var prefab = GetTowerPrefab(towerConfigId);
            if (prefab == null)
            {
                TargetBuildResult(connectionToClient, false, "Unknown tower config");
                return;
            }

            var towerGo = Instantiate(prefab, slot.transform.position + Vector3.up * 0.5f, Quaternion.identity);
            var tower = towerGo.GetComponent<TowerUnit>();
            if (tower != null)
            {
                tower.towerConfigId = towerConfigId;
                tower.ownerPlayerId = playerId;
            }

            NetworkServer.Spawn(towerGo);

            slot.occupiedByTowerNetId = towerGo.GetComponent<NetworkIdentity>().netId;

            Debug.Log($"[Server] Player {playerId} built towerConfigId={towerConfigId} at slot netId={slotNetId}");
            TargetBuildResult(connectionToClient, true, null);
        }

        [Command]
        public void CmdSellTower(uint towerNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(towerNetId, out var identity))
            {
                TargetBuildResult(connectionToClient, false, "Tower not found");
                return;
            }

            var tower = identity.GetComponent<TowerUnit>();
            if (tower == null)
            {
                TargetBuildResult(connectionToClient, false, "NetId is not a TowerUnit");
                return;
            }

            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg == null || !cfg.TryGetLevel(tower.towerConfigId, tower.level, out var levelDef) || levelDef.sellPrice <= 0)
            {
                TargetBuildResult(connectionToClient, false, "Invalid sell price");
                return;
            }

            if (GameEntry.State == null)
            {
                TargetBuildResult(connectionToClient, false, "GameState missing");
                return;
            }

            GameEntry.Build?.ServerClearSlotByTowerNetId(towerNetId);
            GameEntry.State.AddGold(levelDef.sellPrice);
            NetworkServer.Destroy(tower.gameObject);

            Debug.Log($"[Server] Player {playerId} sold towerConfigId={tower.towerConfigId} lv{tower.level} for {levelDef.sellPrice}");
            TargetBuildResult(connectionToClient, true, null);
        }

        [Command]
        public void CmdUpgradeTower(uint towerNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(towerNetId, out var identity))
            {
                TargetBuildResult(connectionToClient, false, "Tower not found");
                return;
            }

            var tower = identity.GetComponent<TowerUnit>();
            if (tower == null)
            {
                TargetBuildResult(connectionToClient, false, "NetId is not a TowerUnit");
                return;
            }

            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg == null)
            {
                TargetBuildResult(connectionToClient, false, "TowerConfig missing");
                return;
            }

            int next = tower.level + 1;
            if (next > cfg.GetMaxLevel(tower.towerConfigId))
            {
                TargetBuildResult(connectionToClient, false, "Already max level");
                return;
            }

            if (!cfg.TryGetLevel(tower.towerConfigId, next, out var levelDef))
            {
                TargetBuildResult(connectionToClient, false, "Unknown level config");
                return;
            }

            if (GameEntry.State == null || !GameEntry.State.TrySpend(levelDef.cost))
            {
                TargetBuildResult(connectionToClient, false, "Not enough gold");
                return;
            }

            tower.ServerUpgrade();

            Debug.Log($"[Server] Player {playerId} upgraded tower netId={towerNetId} to lv{tower.level}");
            TargetBuildResult(connectionToClient, true, null);
        }

        [TargetRpc]
        void TargetBuildResult(NetworkConnectionToClient _, bool success, string reason)
        {
            if (!success)
                Debug.LogWarning($"[Client] Build failed: {reason}");
            GameEntry.Event.Fire(this, BuildReasonEventArgs.Create(success, reason));
        }

        int GetTowerCost(int towerConfigId)
        {
            var cfg = GameEntry.GameConfig?.TowerConfig;
            return cfg != null && cfg.TryGetLevel(towerConfigId, 1, out var levelDef) ? levelDef.cost : 0;
        }

        GameObject GetTowerPrefab(int towerConfigId)
        {
            return TryGetTowerDef(towerConfigId, out var def) ? def.prefab : null;
        }

        static bool TryGetTowerDef(int towerConfigId, out TowerDef def)
        {
            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg != null && cfg.TryGetTower(towerConfigId, out def))
                return true;
            def = default;
            return false;
        }
    }
}

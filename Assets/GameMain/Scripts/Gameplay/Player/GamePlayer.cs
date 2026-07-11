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
            GameEntry.State?.RegisterPlayer(this);
        }

        public override void OnStartClient()
        {
            GameEntry.State?.RegisterPlayer(this);
        }

        public override void OnStartLocalPlayer()
        {
            gameObject.AddComponent<PlayerBuildModeComponent>().InitLocal(this);
        }

        void OnDestroy()
        {
            GameEntry.State?.UnregisterPlayer(this);
        }

        [Command]
        public void CmdSetReady(bool ready)
        {
            var state = GameEntry.State;
            if (state == null || state.phase != GamePhase.Preparing) return;
            isReady = ready;
        }

        void OnReadyChanged(bool oldVal, bool newVal)
        {
            GameEntry.Event.Fire(this, PlayerReadyChangedEventArgs.Create(playerId, newVal));
        }

        [Command]
        public void CmdBuildTower(Vector3 worldPos, int towerConfigId)
        {
            var build = GameEntry.Build;
            if (build == null)
            {
                TargetBuildResult(connectionToClient, false, "No BuildComponent");
                return;
            }

            var slot = build.GetSlotAtPosition(worldPos);
            if (slot == null)
            {
                TargetBuildResult(connectionToClient, false, "Invalid position");
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
            NetworkServer.Spawn(towerGo);

            slot.occupiedByTowerNetId = towerGo.GetComponent<NetworkIdentity>().netId;

            Debug.Log($"[Server] Player {playerId} built towerConfigId={towerConfigId} at {worldPos}");
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
            if (cfg != null && cfg.TryGetTower(towerConfigId, out var def))
                return def.cost;
            return 0;
        }

        GameObject GetTowerPrefab(int towerConfigId)
        {
            var cfg = GameEntry.GameConfig?.TowerConfig;
            if (cfg != null && cfg.TryGetTower(towerConfigId, out var def))
                return def.prefab;
            return null;
        }
    }
}

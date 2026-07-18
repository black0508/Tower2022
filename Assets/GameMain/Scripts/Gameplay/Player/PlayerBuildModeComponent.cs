using GameFramework.Event;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tower
{
    /// <summary>
    /// 本地玩家建造操作：按键进入建造模式、选塔、悬停高亮、点击放置。
    /// 仅客户端由 GamePlayer.OnStartLocalPlayer 动态添加并 InitLocal。
    /// </summary>
    public class PlayerBuildModeComponent : MonoBehaviour
    {
        enum BuildState { Idle, Building }

        GamePlayer player;
        Camera mainCam;
        BuildState buildState = BuildState.Idle;
        int selectedTowerConfigId = -1;
        uint selectedActionTowerNetId;

        public void InitLocal(GamePlayer localPlayer)
        {
            player = localPlayer;
            mainCam = Camera.main;
            GameEntry.Event.Subscribe(TowerCardClickedEventArgs.EventId, OnTowerCardClicked);
            GameEntry.Event.Subscribe(BuildReasonEventArgs.EventId, OnBuildReason);
        }

        void OnDestroy()
        {
            if (buildState == BuildState.Building)
                ExitBuildMode();

            GameEntry.Event.Unsubscribe(TowerCardClickedEventArgs.EventId, OnTowerCardClicked);
            GameEntry.Event.Unsubscribe(BuildReasonEventArgs.EventId, OnBuildReason);
        }

        void Update()
        {
            if (player == null) return;

            var gameState = GameEntry.State;
            if (gameState != null && gameState.phase == GamePhase.Preparing && Input.GetKeyDown(KeyCode.Space))
                player.CmdSetReady(!player.isReady);

            if (Input.GetKeyDown(KeyCode.B))
                ToggleBuildMode();

            if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
            {
                ClearTowerActionSelection();

                if (buildState == BuildState.Building)
                    ExitBuildMode();
                return;
            }

            // 左键选中场上已建成的塔 → 打开升级/出售面板；点空处则取消选中
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                if (TrySelectTowerAtMouse())
                    return;
                ClearTowerActionSelection();
            }

            if (buildState != BuildState.Building) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ExitBuildMode();
                return;
            }

            UpdateHover();

            if (Input.GetMouseButtonDown(0) && selectedTowerConfigId >= 0 && !IsPointerOverUI())
                TryBuildAtMouse();
        }

        void ToggleBuildMode()
        {
            if (buildState == BuildState.Idle) EnterBuildMode();
            else ExitBuildMode();
        }

        void EnterBuildMode()
        {
            if (GameEntry.Build == null)
            {
                Debug.LogWarning("[Build] BuildComponent not found on GameEntry.");
                return;
            }

            buildState = BuildState.Building;
            GameEntry.Build.HighlightAllBuildable();
            GameEntry.Event.Fire(this, BuildModeChangedEventArgs.Create(true));
        }

        void ExitBuildMode()
        {
            buildState = BuildState.Idle;
            GameEntry.Build?.ClearHighlight();
            SetSelectedTower(-1);
            GameEntry.Event.Fire(this, BuildModeChangedEventArgs.Create(false));
        }

        void OnTowerCardClicked(object sender, GameEventArgs e)
        {
            if (buildState != BuildState.Building || e is not TowerCardClickedEventArgs args) return;

            int configId = args.TowerConfigId;
            SetSelectedTower(selectedTowerConfigId == configId ? -1 : configId);
        }

        void OnBuildReason(object sender, GameEventArgs e)
        {
            if (e is not BuildReasonEventArgs args || !args.Success) return;
            SetSelectedTower(-1);
        }

        void SetSelectedTower(int towerConfigId)
        {
            if (selectedTowerConfigId == towerConfigId) return;
            selectedTowerConfigId = towerConfigId;
            GameEntry.Event.Fire(this, TowerSelectionChangedEventArgs.Create(towerConfigId));
        }

        void UpdateHover()
        {
            if (RaycastMouse(out var hit, useSlotMask: true))
                GameEntry.Build?.SetHoverHighlight(hit.collider.GetComponentInParent<BuildSlot>());
            else
                GameEntry.Build?.SetHoverHighlight(null);
        }

        void TryBuildAtMouse()
        {
            if (mainCam == null)
            {
                Debug.LogWarning("[Build] mainCam is null. Check MainCamera tag on the camera.");
                return;
            }

            if (!RaycastMouse(out var hit, useSlotMask: true))
            {
                Debug.Log("[Build] Left click but raycast hit nothing.");
                return;
            }

            var slot = hit.collider.GetComponentInParent<BuildSlot>();
            if (slot == null)
            {
                Debug.Log($"[Build] Hit '{hit.collider.gameObject.name}' (layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}) but no BuildSlot in parent chain.");
                return;
            }

            if (slot.IsOccupied)
            {
                Debug.Log($"[Build] Slot netId={slot.netId} already occupied.");
                return;
            }

            Debug.Log($"[Build] Request build towerConfigId={selectedTowerConfigId} at slot netId={slot.netId} pos={slot.transform.position}");
            player.CmdBuildTower(slot.netId, selectedTowerConfigId);
        }

        bool TrySelectTowerAtMouse()
        {
            if (mainCam == null) return false;
            if (!RaycastMouse(out var hit, useSlotMask: false)) return false;

            var tower = hit.collider.GetComponentInParent<TowerUnit>();
            if (tower == null) return false;

            selectedActionTowerNetId = tower.netId;
            GameEntry.Event.Fire(this, TowerActionSelectedEventArgs.Create(tower.netId, tower.towerConfigId, tower.level));
            return true;
        }

        void ClearTowerActionSelection()
        {
            if (selectedActionTowerNetId == 0) return;
            selectedActionTowerNetId = 0;
            GameEntry.Event.Fire(this, TowerActionSelectedEventArgs.Create(0, 0, 0));
        }

        bool RaycastMouse(out RaycastHit hit, bool useSlotMask)
        {
            hit = default;
            if (mainCam == null) return false;

            int mask = Physics.DefaultRaycastLayers;
            if (useSlotMask && GameEntry.Build != null)
            {
                int slotMask = (int)GameEntry.Build.SlotMask;
                if (slotMask != 0) mask = slotMask;
            }

            var ray = mainCam.ScreenPointToRay(Input.mousePosition);
            // 忽略触发器：塔的射程检测是大范围 Trigger，否则点射程内空地会误命中塔
            return Physics.Raycast(ray, out hit, 1000f, mask, QueryTriggerInteraction.Ignore);
        }

        static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}

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
        BuildState state = BuildState.Idle;
        int selectedTowerConfigId = -1;

        public void InitLocal(GamePlayer localPlayer)
        {
            player = localPlayer;
            mainCam = Camera.main;
            GameEntry.Event.Subscribe(TowerCardClickedEventArgs.EventId, OnTowerCardClicked);
            GameEntry.Event.Subscribe(BuildReasonEventArgs.EventId, OnBuildReason);
        }

        void OnDestroy()
        {
            if (state == BuildState.Building)
                ExitBuildMode();

            GameEntry.Event.Unsubscribe(TowerCardClickedEventArgs.EventId, OnTowerCardClicked);
            GameEntry.Event.Unsubscribe(BuildReasonEventArgs.EventId, OnBuildReason);
        }

        void Update()
        {
            if (player == null) return;

            if (Input.GetKeyDown(KeyCode.B))
                ToggleBuildMode();

            if (state != BuildState.Building) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
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
            if (state == BuildState.Idle) EnterBuildMode();
            else ExitBuildMode();
        }

        void EnterBuildMode()
        {
            if (GameEntry.Build == null)
            {
                Debug.LogWarning("[Build] BuildComponent not found on GameEntry.");
                return;
            }

            state = BuildState.Building;
            GameEntry.Build.HighlightAllBuildable();
            GameEntry.Event.Fire(this, BuildModeChangedEventArgs.Create(true));
        }

        void ExitBuildMode()
        {
            state = BuildState.Idle;
            GameEntry.Build?.ClearHighlight();
            SetSelectedTower(-1);
            GameEntry.Event.Fire(this, BuildModeChangedEventArgs.Create(false));
        }

        void OnTowerCardClicked(object sender, GameEventArgs e)
        {
            if (state != BuildState.Building || e is not TowerCardClickedEventArgs args) return;

            int configId = args.TowerInfo.TowerConfigId;
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
            if (RaycastMouse(out var hit))
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

            if (!RaycastMouse(out var hit))
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
            player.CmdBuildTower(slot.transform.position, selectedTowerConfigId);
        }

        bool RaycastMouse(out RaycastHit hit)
        {
            hit = default;
            if (mainCam == null) return false;

            int mask = GameEntry.Build != null ? (int)GameEntry.Build.SlotMask : 0;
            if (mask == 0) mask = Physics.DefaultRaycastLayers;

            var ray = mainCam.ScreenPointToRay(Input.mousePosition);
            return Physics.Raycast(ray, out hit, 1000f, mask);
        }

        static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    /// <summary>
    /// 建造管理：缓存场景槽位、客户端高亮。
    /// </summary>
    public class BuildComponent : GameFrameworkComponent
    {
        private List<BuildSlot> cachedSlots;
        private BuildSlot currentHover;

        [Tooltip("只对此 Layer 射线检测槽位，避免被地面/其他 collider 抢先命中。0 表示不限制。")]
        [SerializeField] LayerMask slotMask;

        public LayerMask SlotMask => slotMask;

        public IReadOnlyList<BuildSlot> GetAllSlots(bool forceRefresh = false)
        {
            if (cachedSlots == null || forceRefresh)
            {
                cachedSlots = new List<BuildSlot>(FindObjectsOfType<BuildSlot>());
                Log.Info($"[Build] Scanned {cachedSlots.Count} slots");
            }
            return cachedSlots;
        }

        /// <summary>仅服务端：对局开始时清空所有槽位占用（场景 SyncVar 会跨局残留）。</summary>
        public void ClearAllOccupancy()
        {
            foreach (var slot in GetAllSlots(forceRefresh: true))
            {
                if (slot != null)
                    slot.occupiedByTowerNetId = 0;
            }
        }

        /// <summary>仅服务端：按塔 netId 清空对应槽位占用。</summary>
        public void ServerClearSlotByTowerNetId(uint towerNetId)
        {
            if (towerNetId == 0) return;
            foreach (var slot in GetAllSlots())
            {
                if (slot != null && slot.occupiedByTowerNetId == towerNetId)
                {
                    slot.occupiedByTowerNetId = 0;
                    return;
                }
            }
        }

        // ===== 仅客户端调用 =====

        public void HighlightAllBuildable()
        {
            foreach (var slot in GetAllSlots())
                slot.SetVisualState(BuildSlotVisualState.Buildable);
        }

        public void ClearHighlight()
        {
            foreach (var slot in GetAllSlots())
                slot.SetVisualState(BuildSlotVisualState.Hidden);
            currentHover = null;
        }

        public void SetHoverHighlight(BuildSlot newHover)
        {
            if (currentHover == newHover) return;

            if (currentHover != null)
                currentHover.SetVisualState(BuildSlotVisualState.Buildable);

            currentHover = newHover;

            if (currentHover != null)
                currentHover.SetVisualState(BuildSlotVisualState.Hover);
        }
    }
}

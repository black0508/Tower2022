using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    public class BuildComponent : GameFrameworkComponent
    {
        private List<BuildSlot> cachedSlots;
        private BuildSlot currentHover;

        public IReadOnlyList<BuildSlot> GetAllSlots(bool forceRefresh = false)
        {
            if (cachedSlots == null || forceRefresh)
            {
                cachedSlots = new List<BuildSlot>(FindObjectsOfType<BuildSlot>());
                Log.Info($"[BuildSlot] Scanned {cachedSlots.Count} slots");
            }
            return cachedSlots;
        }

        public BuildSlot GetSlotAtPosition(Vector3 worldPos, float tolerance = 0.5f)
        {
            var sqrTol = tolerance * tolerance;
            foreach (var slot in GetAllSlots())
                if (Vector3.SqrMagnitude(slot.transform.position - worldPos) < sqrTol)
                    return slot;
            return null;
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

            // 上一帧悬停的 Slot 恢复
            if (currentHover != null)
                currentHover.SetVisualState(BuildSlotVisualState.Buildable);

            currentHover = newHover;

            // 新悬停 Slot 加深
            if (currentHover != null)
                currentHover.SetVisualState(BuildSlotVisualState.Hover);
        }
    }
}

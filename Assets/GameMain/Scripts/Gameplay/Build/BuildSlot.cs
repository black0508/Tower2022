using Mirror;
using UnityEngine;

namespace Tower
{
    public class BuildSlot : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnOccupiedChanged))]
        public uint occupiedByTowerNetId;  // 0 = 空，非0 = 被这个塔占用

        [Header("Visual")]
        public MeshRenderer meshRenderer;
        public Material matDefault;       // 透明（非建造模式）
        public Material matBuildable;     // 绿色（建造模式 + 未占用）
        public Material matHover;         // 深绿（鼠标悬停）
        public Material matOccupied;      // 灰色（已被占用）

        public bool IsOccupied => occupiedByTowerNetId != 0;

        void Awake()
        {
            if (meshRenderer == null)
                meshRenderer = GetComponentInChildren<MeshRenderer>();
        }

        void Start()
        {
            if (meshRenderer == null) return;
            // 默认隐藏（仅在建造模式下显示）
            meshRenderer.enabled = false;
        }

        void OnOccupiedChanged(uint oldVal, uint newVal)
        {
            // 占用状态变化时刷新视觉（如果当前在建造模式下）
            // 由 BuildComponent 统一管理刷新
        }

        public void SetVisualState(BuildSlotVisualState state)
        {
            if (meshRenderer == null) return;

            switch (state)
            {
                case BuildSlotVisualState.Hidden:
                    meshRenderer.enabled = false;
                    break;
                case BuildSlotVisualState.Buildable:
                    meshRenderer.enabled = true;
                    meshRenderer.material = IsOccupied ? matOccupied : matBuildable;
                    break;
                case BuildSlotVisualState.Hover:
                    meshRenderer.enabled = true;
                    meshRenderer.material = IsOccupied ? matOccupied : matHover;
                    break;
            }
        }
    }

    public enum BuildSlotVisualState { Hidden, Buildable, Hover }
}

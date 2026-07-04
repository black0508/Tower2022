using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    /// <summary>
    /// 双端共享的游戏配置。由 Client/Server 子类挂载到 GameEntry，启动时自行注册。
    /// </summary>
    public abstract class GameConfigComponent : MonoBehaviour
    {
        //TODO: 后期采用自动资源加载方式而不是拖拽方式
        [SerializeField] TowerConfig towerConfig;

        public TowerConfig TowerConfig => towerConfig;

        protected virtual void Awake()
        {
            GameEntry.RegisterGameConfig(this);
        }

        protected virtual void OnDestroy()
        {
            GameEntry.UnregisterGameConfig(this);
        }
    }
}

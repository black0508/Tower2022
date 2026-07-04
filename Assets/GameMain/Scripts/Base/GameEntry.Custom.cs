using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 游戏入口 - 自定义组件注册。
    /// 非 GF 内置的组件在此注册，通过 GameEntry.XXX 统一访问。
    /// </summary>
    public partial class GameEntry : MonoBehaviour
    {
        /// <summary>Mirror 网络管理器</summary>
        public static GameNetworkManager NetWork { get; private set; }

        /// <summary>建造管理（场景槽位缓存、高亮、服务端查位）</summary>
        public static BuildComponent Build { get; private set; }

        /// <summary>全局游戏状态（GameState Spawn 后由自身注册）</summary>
        public static GameState State { get; private set; }

        /// <summary>当前进程的游戏配置（由 Client/Server 子类在 Awake 时注册）</summary>
        public static GameConfigComponent GameConfig { get; private set; }

        internal static void RegisterState(GameState state)
        {
            State = state;
        }

        internal static void UnregisterState(GameState state)
        {
            if (State == state)
                State = null;
        }

        internal static void RegisterGameConfig(GameConfigComponent comp)
        {
            GameConfig = comp;
        }

        internal static void UnregisterGameConfig(GameConfigComponent comp)
        {
            if (GameConfig == comp)
                GameConfig = null;
        }


        private static void InitCustomComponents()
        {
            NetWork = FindObjectOfType<GameNetworkManager>();
            Build = UnityGameFramework.Runtime.GameEntry.GetComponent<BuildComponent>();
        }
    }
}

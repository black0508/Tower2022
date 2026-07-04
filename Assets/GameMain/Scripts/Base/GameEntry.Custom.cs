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

        /// <summary>建造槽位管理</summary>
        public static BuildComponent BuildSlot { get; private set; }

        /// <summary>全局游戏状态（GameState Spawn 后由自身注册）</summary>
        public static GameState State { get; private set; }

        public static ClientConfigComponent ClientConfig { get; private set; }

        internal static void RegisterState(GameState state)
        {
            State = state;
        }

        internal static void UnregisterState(GameState state)
        {
            if (State == state)
                State = null;
        }


        private static void InitCustomComponents()
        {
            NetWork = FindObjectOfType<GameNetworkManager>();
            BuildSlot = UnityGameFramework.Runtime.GameEntry.GetComponent<BuildComponent>();
            ClientConfig = UnityGameFramework.Runtime.GameEntry.GetComponent<ClientConfigComponent>();
        }
    }
}

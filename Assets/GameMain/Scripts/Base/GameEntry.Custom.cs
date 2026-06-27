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
        public static GameNetworkManager NetManager { get; private set; }

        private static void InitCustomComponents()
        {
            NetManager = FindObjectOfType<GameNetworkManager>();
            // 后续新增自定义组件在此添加
        }
    }
}

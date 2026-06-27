using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 全局网络身份判定。基于 Mirror NetworkServer/NetworkClient 静态属性判断当前角色。
    /// </summary>
    public static class GameContext
    {
        /// <summary>是否运行服务器（包括 Host 和 Dedicated Server）</summary>
        public static bool IsServer => NetworkServer.active;

        /// <summary>是否运行客户端（包括 Host 和 Pure Client）</summary>
        public static bool IsClient => NetworkClient.active;

        /// <summary>Host 模式：同时运行 Server + Client</summary>
        public static bool IsHost => NetworkServer.active && NetworkClient.active;

        /// <summary>独立服务器（无本地客户端）</summary>
        public static bool IsDedicatedServer => NetworkServer.active && !NetworkClient.active;

        /// <summary>纯客户端（未运行服务器）</summary>
        public static bool IsPureClient => !NetworkServer.active && NetworkClient.active;

        /// <summary>离线模式（未启动任何网络）</summary>
        public static bool IsOffline => !NetworkServer.active && !NetworkClient.active;
    }
}
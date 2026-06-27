using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 调试用 GUI 面板：Editor 中画按钮启动 Server / Client / Host。
    /// 挂在场景里的 [Network] 物体上。
    /// </summary>
    public class NetworkDebugUI : MonoBehaviour
    {
        private NetworkManager manager;

        private void Awake()
        {
            manager = GetComponent<NetworkManager>();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 220, 320));

            if (!NetworkClient.isConnected && !NetworkServer.active)
            {
                // 未连接状态：显示启动按钮
                GUILayout.Label("=== Network Debug ===");

                if (GUILayout.Button("Start Server", GUILayout.Height(40)))
                {
                    manager.StartServer();
                    Debug.Log("[NetworkDebugUI] Server started");
                }

                if (GUILayout.Button("Start Client", GUILayout.Height(40)))
                {
                    manager.StartClient();
                    Debug.Log("[NetworkDebugUI] Client started");
                }

                if (GUILayout.Button("Start Host", GUILayout.Height(40)))
                {
                    manager.StartHost();
                    Debug.Log("[NetworkDebugUI] Host started (Server + Client)");
                }
            }
            else
            {
                // 已连接状态：显示状态 + Stop 按钮
                GUILayout.Label("=== Network Debug ===");
                GUILayout.Label(NetworkServer.active
                    ? "Server: Running"
                    : "Server: Stopped");

                GUILayout.Label(NetworkClient.isConnected
                    ? "Client: Connected"
                    : "Client: Disconnected");

                if (GUILayout.Button("Stop", GUILayout.Height(40)))
                {
                    if (NetworkServer.active && NetworkClient.active)
                    {
                        // Host 模式：同时停服务器和客户端
                        manager.StopHost();
                    }
                    else if (NetworkServer.active)
                    {
                        manager.StopServer();
                    }
                    else if (NetworkClient.isConnected)
                    {
                        manager.StopClient();
                    }
                    Debug.Log("[NetworkDebugUI] Stopped");
                }
            }

            GUILayout.EndArea();
        }
    }
}
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 调试用 GUI 面板：Editor 中画按钮启动 Server / Client / Host。
    /// </summary>
    public class NetworkDebugUI : MonoBehaviour
    {
        private GameNetworkManager Manager => GameEntry.NetManager;

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 220, 320));

            if (!NetworkClient.isConnected && !NetworkServer.active)
            {
                GUILayout.Label("=== Network Debug ===");

                if (GUILayout.Button("Start Server", GUILayout.Height(40)))
                {
                    Manager.StartServer();
                    Debug.Log("[NetworkDebugUI] Server started");
                }

                if (GUILayout.Button("Start Client", GUILayout.Height(40)))
                {
                    Manager.StartClient();
                    Debug.Log("[NetworkDebugUI] Client started");
                }

                if (GUILayout.Button("Start Host", GUILayout.Height(40)))
                {
                    Manager.StartHost();
                    Debug.Log("[NetworkDebugUI] Host started (Server + Client)");
                }
            }
            else
            {
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
                        Manager.StopHost();
                    }
                    else if (NetworkServer.active)
                    {
                        Manager.StopServer();
                    }
                    else if (NetworkClient.isConnected)
                    {
                        Manager.StopClient();
                    }
                    Debug.Log("[NetworkDebugUI] Stopped");
                }
            }

            GUILayout.EndArea();
        }
    }
}

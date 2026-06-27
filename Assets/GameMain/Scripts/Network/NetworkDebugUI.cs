using Mirror;
using UnityEngine;

namespace Tower
{
    public class NetworkDebugUI : MonoBehaviour
    {
        private GameNetworkManager Manager => GameEntry.NetWork;

        private string RoleLabel
        {
            get
            {
                if (NetworkServer.active && NetworkClient.active) return "[Host]";
                if (NetworkServer.active) return "[Server]";
                if (NetworkClient.isConnected) return "[Client]";
                return "[Idle]";
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 260, 300));

            GUILayout.Label($"=== Network Debug {RoleLabel} ===");

            if (!NetworkClient.isConnected && !NetworkServer.active)
            {
                // 未启动：显示启动按钮
                if (GUILayout.Button("Start Server", GUILayout.Height(36)))
                {
                    Manager.StartServer();
                    Debug.Log("[NetworkDebugUI] Server started");
                }
                if (GUILayout.Button("Start Client", GUILayout.Height(36)))
                {
                    Manager.StartClient();
                    Debug.Log("[NetworkDebugUI] Client started");
                }
                if (GUILayout.Button("Start Host", GUILayout.Height(36)))
                {
                    Manager.StartHost();
                    Debug.Log("[NetworkDebugUI] Host started (Server + Client)");
                }
            }
            else
            {
                // 已启动：只显示当前角色的状态
                if (NetworkServer.active)
                {
                    int count = NetworkServer.connections.Count;
                    GUILayout.Label($"Server: Running (clients={count})");
                }

                // Client 端只显示自己的连接状态
                if (NetworkClient.active)
                {
                    GUILayout.Label(NetworkClient.isConnected
                        ? "Client: Connected"
                        : "Client: Connecting...");
                }

                if (GUILayout.Button("Stop", GUILayout.Height(36)))
                {
                    if (NetworkServer.active && NetworkClient.active)
                        Manager.StopHost();
                    else if (NetworkServer.active)
                        Manager.StopServer();
                    else if (NetworkClient.isConnected)
                        Manager.StopClient();
                    Debug.Log("[NetworkDebugUI] Stopped");
                }
            }

            GUILayout.EndArea();
        }
    }
}
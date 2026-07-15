using System.Collections.Generic;
using GameFramework.ObjectPool;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 联网对象池：GF ObjectPool 当后端，Mirror 当生成/回收 authority。
    /// 纯代码服务，无 Mono 生命周期依赖；由 GameEntry 初始化时创建。
    /// </summary>
    public class NetworkObjectPool
    {
        readonly Dictionary<string, IObjectPool<NetworkPoolObject>> m_Pools = new();

        IObjectPool<NetworkPoolObject> GetPool(GameObject prefab)
        {
            string key = prefab.name;
            if (!m_Pools.TryGetValue(key, out var pool))
            {
                pool = GameEntry.ObjectPool.CreateSingleSpawnObjectPool<NetworkPoolObject>($"Net:{key}");
                m_Pools[key] = pool;
            }
            return pool;
        }

        GameObject Acquire(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            var pool = GetPool(prefab);
            var poolObj = pool.Spawn();
            GameObject go;
            if (poolObj != null)
            {
                go = (GameObject)poolObj.Target;
                go.transform.SetPositionAndRotation(pos, rot);
            }
            else
            {
                go = Object.Instantiate(prefab, pos, rot);
                go.name = prefab.name;
                go.AddComponent<PooledTag>().poolKey = prefab.name;
                pool.Register(NetworkPoolObject.Create(go), spawned: true);
            }
            go.SetActive(true);
            return go;
        }

        void ReturnToPool(GameObject go)
        {
            var tag = go.GetComponent<PooledTag>();
            if (tag == null || !m_Pools.TryGetValue(tag.poolKey, out var pool))
            {
                Object.Destroy(go);
                return;
            }
            go.SetActive(false);
            pool.Unspawn(go);
        }

        /// <param name="beforeSpawn">在 NetworkServer.Spawn 之前设置 SyncVar（如 ServerLaunch）</param>
        public GameObject ServerSpawn(GameObject prefab, Vector3 pos, Quaternion rot,
                                      System.Action<GameObject> beforeSpawn = null)
        {
            var go = Acquire(prefab, pos, rot);
            beforeSpawn?.Invoke(go);
            NetworkServer.Spawn(go);
            return go;
        }

        public void ServerDespawn(GameObject go)
        {
            if (go == null) return;
            NetworkServer.UnSpawn(go);
            ReturnToPool(go);
        }

        public void RegisterClientHandlers(GameObject prefab)
        {
            NetworkClient.RegisterPrefab(prefab,
                spawnHandler: msg => ClientSpawn(prefab, msg),
                unspawnHandler: ClientUnspawn);
        }

        GameObject ClientSpawn(GameObject prefab, SpawnMessage msg)
        {
            return Acquire(prefab, msg.position, msg.rotation);
        }

        void ClientUnspawn(GameObject go)
        {
            if (NetworkServer.active) return;
            ReturnToPool(go);
        }
    }

    /// <summary>挂在池化实例上，记录它属于哪个池（prefab 名），回收时据此找回对应池。</summary>
    public class PooledTag : MonoBehaviour
    {
        [HideInInspector] public string poolKey;
    }
}

using GameFramework;
using GameFramework.ObjectPool;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// GF 对象池里包一个联网 GameObject。
    /// 对象「在池中」时是 UnSpawn（已下线）状态；池要丢弃它（超容量/过期）时才真正 Destroy。
    /// </summary>
    public class NetworkPoolObject : ObjectBase
    {
        public static NetworkPoolObject Create(GameObject go)
        {
            var obj = ReferencePool.Acquire<NetworkPoolObject>();
            // Name 必须为空：GF 的 Spawn() 按 name="" 查找；用 InstanceID 会导致永远取不到、一直 Instantiate
            obj.Initialize(go);
            return obj;
        }

        protected override void Release(bool isShutdown)
        {
            var go = Target as GameObject;
            if (go != null)
                Object.Destroy(go);
        }
    }
}

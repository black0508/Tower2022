using System.Collections.Generic;
using GameFramework;
using Mirror;
using UnityEngine;

namespace Tower
{
    public class BuffHolder : NetworkBehaviour
    {
        public readonly SyncList<BuffSnapshot> buffSnapshots = new();

        readonly List<BuffBehaviour> serverBuffs = new();
        readonly List<BuffBehaviour> clientBuffs = new();

        AttributeComponent attrs;

        void Awake()
        {
            attrs = GetComponent<AttributeComponent>();
        }

        [Server]
        public void AddBuff(BuffBehaviour buff, float? duration = null)
        {
            if (buff == null) return;

            for (int i = 0; i < serverBuffs.Count; i++)
            {
                if (serverBuffs[i].DefId != buff.DefId) continue;

                var existing = serverBuffs[i];
                existing.startTimeOnServer = (float)NetworkTime.time;
                existing.duration = duration ?? buff.DefaultDuration;
                existing.stackCount = buff.stackCount;
                SyncSnapshotAt(i, existing);
                attrs?.Recalculate();
                // 同 DefId 只刷新时长，归还未挂上的新实例
                ReferencePool.Release(buff);
                return;
            }

            buff.startTimeOnServer = (float)NetworkTime.time;
            buff.duration = duration ?? buff.DefaultDuration;
            buff.OnApply(this, isServer: true);
            serverBuffs.Add(buff);

            var writer = NetworkWriterPool.Get();
            buff.Serialize(writer);
            byte[] payload = writer.ToArray();
            NetworkWriterPool.Return(writer);

            buffSnapshots.Add(new BuffSnapshot
            {
                defId = (int)buff.DefId,
                startTimeOnServer = buff.startTimeOnServer,
                duration = buff.duration,
                stackCount = buff.stackCount,
                payload = payload,
            });

            attrs?.Recalculate();
        }

        [Server]
        public void RemoveBuff(BuffBehaviour buff)
        {
            int idx = serverBuffs.IndexOf(buff);
            if (idx < 0) return;

            buff.OnRemove(isServer: true);
            serverBuffs.RemoveAt(idx);
            if (idx < buffSnapshots.Count)
                buffSnapshots.RemoveAt(idx);
            ReferencePool.Release(buff);
            attrs?.Recalculate();
        }

        /// <summary>池化复用：清掉所有 Buff（服务端表 + 同步快照），并归还引用池。</summary>
        public void ClearAll()
        {
            if (!isServer) return;

            for (int i = 0; i < serverBuffs.Count; i++)
            {
                var b = serverBuffs[i];
                if (b == null) continue;
                b.OnRemove(isServer: true);
                ReferencePool.Release(b);
            }
            serverBuffs.Clear();
            buffSnapshots.Clear();
            attrs?.Recalculate();
        }

        [Server]
        void SyncSnapshotAt(int idx, BuffBehaviour buff)
        {
            var writer = NetworkWriterPool.Get();
            buff.Serialize(writer);
            byte[] payload = writer.ToArray();
            NetworkWriterPool.Return(writer);

            buffSnapshots[idx] = new BuffSnapshot
            {
                defId = (int)buff.DefId,
                startTimeOnServer = buff.startTimeOnServer,
                duration = buff.duration,
                stackCount = buff.stackCount,
                payload = payload,
            };
        }

        void Update()
        {
            if (!isServer) return;

            float dt = Time.deltaTime;
            for (int i = serverBuffs.Count - 1; i >= 0; i--)
            {
                var buff = serverBuffs[i];
                buff.OnTick(dt);
                if (NetworkTime.time >= buff.startTimeOnServer + buff.duration)
                    RemoveBuff(buff);
            }
        }

        public IEnumerable<BuffBehaviour> GetSortedServerBuffs()
        {
            serverBuffs.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            return serverBuffs;
        }

        public override void OnStartClient()
        {
            buffSnapshots.OnAdd += OnClientBuffAdd;
            buffSnapshots.OnRemove += OnClientBuffRemove;
            buffSnapshots.OnSet += OnClientBuffSet;

            for (int i = 0; i < buffSnapshots.Count; i++)
                OnClientBuffAdd(i);
        }

        public override void OnStopServer()
        {
            ClearAll();
        }

        public override void OnStopClient()
        {
            buffSnapshots.OnAdd -= OnClientBuffAdd;
            buffSnapshots.OnRemove -= OnClientBuffRemove;
            buffSnapshots.OnSet -= OnClientBuffSet;

            foreach (var b in clientBuffs)
            {
                if (b == null) continue;
                b.OnRemove(isServer: false);
                ReferencePool.Release(b);
            }
            clientBuffs.Clear();
        }

        void OnClientBuffAdd(int idx)
        {
            var snap = buffSnapshots[idx];
            var buff = BuffBehaviourRegistry.Create(snap.defId);
            if (buff == null)
            {
                Debug.LogWarning($"[BuffHolder] Unknown BuffDefId={snap.defId}");
                clientBuffs.Add(null);
                return;
            }

            buff.startTimeOnServer = snap.startTimeOnServer;
            buff.duration = snap.duration;
            buff.stackCount = snap.stackCount;
            if (snap.payload != null && snap.payload.Length > 0)
            {
                var reader = NetworkReaderPool.Get(snap.payload);
                buff.Deserialize(reader);
                NetworkReaderPool.Return(reader);
            }

            buff.OnApply(this, isServer: false);
            clientBuffs.Add(buff);
        }

        void OnClientBuffRemove(int idx, BuffSnapshot oldSnap)
        {
            if (idx < 0 || idx >= clientBuffs.Count) return;
            var buff = clientBuffs[idx];
            if (buff != null)
            {
                buff.OnRemove(isServer: false);
                ReferencePool.Release(buff);
            }
            clientBuffs.RemoveAt(idx);
        }

        void OnClientBuffSet(int idx, BuffSnapshot oldSnap)
        {
            if (idx < 0 || idx >= clientBuffs.Count) return;
            var buff = clientBuffs[idx];
            if (buff == null) return;
            var snap = buffSnapshots[idx];
            buff.startTimeOnServer = snap.startTimeOnServer;
            buff.duration = snap.duration;
            buff.stackCount = snap.stackCount;
        }
    }
}

# W2-Day3 — SlowBuff 迁移 + GF 对象池/引用池重构

> 日期：2026-07-15
> 目标工程：`F:\Download\Tower2022-main (1)\Tower2022-main`
> 前置：**Day2 Effect Pipeline 必须已合入**（`AttributeComponent` / `BuffHolder` / `BuffBehaviour` / `BuffBehaviourRegistry` / `DamageService`）
> **本文是 Day3 唯一依据**：打开即可按文件抄；抄完对照 Checklist 验收
>
> **Day3 分两步（同一篇）**：
> - **Phase1 — SlowBuff 迁移**：Frost 减速正式走 Buff 系统（下方「Phase1」全部章节）。
> - **Phase2 — 池化重构**：用 GF `ObjectPoolComponent` 池化**子弹 + 怪物**、用 `ReferencePool` 池化 **Buff**（下方「Phase2」全部章节）。
>
> 建议按序做：先跑通 Phase1 的 Buff 链路，再进 Phase2 把子弹/怪物/Buff 一起接进池。

---

# ========== Phase1 · SlowBuff 迁移 ==========

## Day3 目标 / 完成标志 / 不做

**目标**

- 把 Frost 塔的减速从「Day2 临时删掉」状态，正式迁移到 Buff 系统
- 新增 `SlowBuff : BuffBehaviour`，通过 `AttributeComponent` 的 `Speed` 黑板降速
- `FrostProjectile` 命中：AoE 伤害 + `AddBuff(new SlowBuff)`
- 完全复用 Day2 的同步/表现链路（`SyncList<BuffSnapshot>` + 客户端变蓝）

**完成标志**

- Frost 命中：范围内敌人扣血 + 变蓝 + 明显变慢
- `slowDuration` 后自动恢复（颜色 & 速度）
- 双端一致（Host + 纯客户端）；中途加入能看到「当前正被减速」的敌人是蓝色
- 与 Debug Mutation「全敌减速」叠加正确（百分比相加）

**明确不做**

- 多层叠加变强、燃烧/眩晕等其它 Buff
- Buff 图标 UI、投票 Mutation（Day4+）

---

## 依赖回顾（Day2 已就位，Day3 直接用）

| 依赖 | Day2 现状 |
|------|-----------|
| `BuffDefId.Slow = 1001` | 枚举已留 |
| `BuffHolder.AddBuff(BuffBehaviour, duration)` | 同 DefId 刷新时长；触发 `AttributeComponent.Recalculate` |
| `AttributeComponent` | `Speed.Final = (Base + Flat) * (1 + Percent)`；`Buff.CollectModifiers` 往黑板写 percent |
| `Enemy.SetSlowVisual(bool)` | 客户端变蓝/复原 |
| `FrostProjectile` | Day2 已改走 `DamageInfo`；**保留** `slowMultiplier` / `slowDuration` 字段给 Day3 |
| `BuffBehaviourRegistry` | 客户端从 `BuffSnapshot` 反序列化重建 Buff 的工厂表 |

> Day2 已删除 `Enemy.speedMultiplier` / `slowEndTime` / `ApplySlow`——Day3 **不要**再引入它们，减速全部走 Buff。

---

## 数值换算（务必记牢）

```text
Speed.Final = Base * (1 + percent)

Frost 想要 speed × slowMultiplier：
  slowMultiplier = 0.6
  → Final = Base * 0.6
  → 1 + percent = 0.6
  → percent = slowMultiplier - 1f = -0.4
```

所以 `AddBuff` 时传 **`percent = slowMultiplier - 1f`**，不要直接把 `0.6` 当 percent。

---

## 叠加 / 刷新规则（MVP 决策）

- **同 DefId（都是 Slow）**：只刷新时长，不叠强（沿用 Day2 `BuffHolder.AddBuff`）。
- **多个 Frost 命中同一敌人**：后一次把时长刷满，`percent` 不变（所有 Frost 同值，无差别）。
- **与 Mutation「全敌减速 -20%」**：两者都往 `Speed` 黑板 `AddPercent`，**百分比相加**：
  `mutation(-0.2) + slow(-0.4) = -0.6` → `speed × 0.4`。天然正确，无需特判。
- **扩展点（Day3 不做）**：若未来出现不同强度的 Slow 且要「强的覆盖弱的」，改 `BuffHolder.AddBuff` 的刷新分支——比较 `percent` 取更狠者，再 `SyncSnapshotAt` + `Recalculate`。

---

## 文件改动

**新建**

```text
Assets/GameMain/Scripts/Gameplay/Effect/
  SlowBuff.cs
```

**修改**

```text
BuffBehaviourRegistry.cs   — 注册 Slow 工厂（双端都要能反序列化）
FrostProjectile.cs         — 命中后 AddBuff(new SlowBuff)
```

**清理（可选）**

```text
Assets/GameMain/Scripts/Debug/BuffPipelineDebugTest.cs
  Day2 自测用；Day3 起 SlowBuff 就是真实减速演示，可删（或保留 F3/F4 测 Mutation）
```

---

## 1. `SlowBuff.cs`（新建）

```csharp
using Mirror;

namespace Tower
{
    /// <summary>
    /// 减速 Buff：按百分比降低 Speed。Frost 塔命中时挂上。
    /// percent 为负值（如 -0.4 表示减速 40%）。
    /// </summary>
    public class SlowBuff : BuffBehaviour
    {
        public override BuffDefId DefId => BuffDefId.Slow;
        public override float DefaultDuration => 3f;

        public float percent = -0.4f;

        public override void CollectModifiers(IStatModifierBuffer buffer) =>
            buffer.AddPercent(AttributeKey.Speed, percent);

        public override void OnApply(BuffHolder holder, bool isServer)
        {
            base.OnApply(holder, isServer);
            if (!isServer)
                holder.GetComponent<Enemy>()?.SetSlowVisual(true);
        }

        public override void OnRemove(bool isServer)
        {
            if (!isServer)
                holder.GetComponent<Enemy>()?.SetSlowVisual(false);
        }

        // percent 参与同步：中途加入的客户端也能拿到正确减速强度
        public override void Serialize(NetworkWriter writer) => writer.WriteFloat(percent);
        public override void Deserialize(NetworkReader reader) => percent = reader.ReadFloat();
    }
}
```

> **表现只在客户端设**（`if (!isServer)`）：Host 的服务端侧不直接改颜色，蓝色由 Host 的 client 面通过 `clientBuffs` 走 `OnApply(isServer:false)`（见下「表现链路」）。速度由 `Speed.Final` 变化驱动，双端各自生效。

---

## 2. `BuffBehaviourRegistry.cs`（注册 Slow）

把 Day2 注释掉的 Slow 工厂打开，放进静态字典：

```csharp
using System;
using System.Collections.Generic;

namespace Tower
{
    public static class BuffBehaviourRegistry
    {
        static readonly Dictionary<BuffDefId, Func<BuffBehaviour>> Factories = new()
        {
            { BuffDefId.Slow, () => new SlowBuff() },
        };

        public static void Register(BuffDefId id, Func<BuffBehaviour> factory) =>
            Factories[id] = factory;

        public static BuffBehaviour Create(BuffDefId id) =>
            Factories.TryGetValue(id, out var f) ? f() : null;

        public static BuffBehaviour Create(int id) => Create((BuffDefId)id);
    }
}
```

> **为什么放静态字典**：服务端 `AddBuff` 走 `new SlowBuff()`，但**客户端**是靠 `BuffBehaviourRegistry.Create(snap.defId)` 从快照重建的。漏注册 → 客户端报 `Unknown BuffDefId=1001`，敌人不变蓝。静态字典保证双端一致、不依赖任何场景组件先跑。

---

## 3. `FrostProjectile.CheckServerHit`（命中挂 SlowBuff）

Day2 已把这里改成走 `DamageInfo`，Day3 在打完伤害后补 `AddBuff`：

```csharp
protected override bool CheckServerHit()
{
    if (serverTarget == null) return false;
    if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
        return false;

    float slowPercent = slowMultiplier - 1f; // 0.6 → -0.4

    var hits = Physics.OverlapSphere(transform.position, aoeRadius, ~0);
    foreach (var h in hits)
    {
        if (!h.TryGetComponent<Enemy>(out var enemy) || !enemy.IsAlive) continue;

        var info = new DamageInfo
        {
            source = sourceNetIdentity,
            target = enemy.netIdentity,
            amount = damage,
            type = DamageType.Physical,
            tags = DamageTag.Direct,
            critChance = 0.05f,
        };
        enemy.TakeDamage(ref info);

        // 伤害可能致死；只给存活者挂减速，避免给正在销毁的敌人加 Buff
        if (enemy.IsAlive && enemy.TryGetComponent<BuffHolder>(out var holder))
            holder.AddBuff(new SlowBuff { percent = slowPercent }, slowDuration);
    }
    return true;
}
```

`slowMultiplier` / `slowDuration` 仍是 `FrostProjectile` 上的 Inspector 字段，语义从「直接改敌人 speedMultiplier」变成「生成 SlowBuff 的参数」。

---

## 表现链路（说明，无需额外写代码）

```text
服务端命中：
  AddBuff → serverBuffs + buffSnapshots(SyncList) → attrs.Recalculate()
  → Speed.Final 下降 → FloatAttribute.OnValueChanged
  → Enemy.OnSpeedChanged → ApplySpeed → NavMeshAgent 变慢

客户端（含 Host 的 client 面）：
  buffSnapshots.OnAdd → BuffBehaviourRegistry.Create(Slow) → Deserialize(percent)
  → SlowBuff.OnApply(isServer:false) → Enemy.SetSlowVisual(true) 变蓝
  Speed.Final 经 SyncList<AttributeEntry> 同步下降（客户端 agent 已关，仅表现预留）

到期：
  BuffHolder.Update 检测 NetworkTime ≥ start+duration → RemoveBuff
  服务端 Recalculate 恢复速度；客户端 OnRemove → SetSlowVisual(false) 复原色

中途加入：
  SyncList 自动 replay OnAdd → 新客户端立刻把当前被减速的敌人画成蓝色
```

> **Host 为什么也能看到蓝色**：Host 既是 server 又是 client，`OnStartClient` 已订阅 `buffSnapshots.OnAdd`；服务端 `AddBuff` 引起的 SyncList 变更会在本机走一遍 `OnClientBuffAdd → OnApply(isServer:false)`。

---

## Checklist

**新建 / 修改**

- [ ] `SlowBuff.cs`
- [ ] `BuffBehaviourRegistry` 注册 `Slow`
- [ ] `FrostProjectile.CheckServerHit` 命中 `AddBuff`

**验收**

- [ ] Frost 命中范围内敌人变蓝 + 变慢，`slowDuration` 后恢复
- [ ] 纯客户端与 Host 表现一致
- [ ] 中途加入客户端看到「当前被减速」的敌人是蓝色
- [ ] Mutation 全敌减速 + Slow 叠加 = 更慢（percent 相加，如 -0.2 + -0.4 = -0.6）
- [ ] Frost 击杀的敌人不残留 Buff（死亡即销毁）
- [ ] （可选）删 `BuffPipelineDebugTest`

---

## 关键提醒

1. **percent = slowMultiplier - 1f**，别直接把 `0.6` 当 percent。
2. **Registry 用静态字典注册**，双端一致；漏注册客户端会 `Unknown BuffDefId`、看不到蓝色。
3. **AddBuff 前判 `enemy.IsAlive`**，别给本次被打死的敌人挂 Buff。
4. **不要重新引入** `Enemy.speedMultiplier` / `slowEndTime` / `ApplySlow`（Day2 已删，全走 Buff）。
5. `slowMultiplier` / `slowDuration` 仍在 `FrostProjectile` 上（Inspector 可调），只是语义变了。
6. `Enemy` 有 `[RequireComponent(typeof(BuffHolder))]`，理论必有 `BuffHolder`；仍用 `TryGetComponent` 防御性判空。

---

# ========== Phase2 · GF 池化重构 ==========

## Phase2 目标 / 完成标志 / 不做

**目标**

- 用 GF `ObjectPoolComponent` 池化**高频生成对象**：`Projectile`（子弹）、`Enemy`（怪物）。生成/回收走池，不再 `Instantiate`/`Destroy`。
- 用 GF `ReferencePool` 池化 **Buff**（`SlowBuff` 等纯 C# 对象），消除每次命中的 `new`/GC。
- 新增一个 `NetworkObjectPool` 组件，封装「GF 池 ↔ Mirror 生成/回收」的双端胶水。

**完成标志**

- 一整局跑下来，子弹/怪物的 `Instantiate` 只在「池空补货」时发生，之后全是复用；Profiler 里 GC Alloc 尖峰明显下降。
- 复用对象**状态干净**：新怪物满血、无残留 Buff、NavMesh 正常寻路；复用子弹无穿屏拖尾残影、命中判定正确。
- 双端一致：Host、纯客户端、专用服务器三种拓扑下，池化对象的表现与非池化时完全一致。
- Buff 到期/敌人死亡后 `SlowBuff` 被 `Release` 回引用池，无泄漏、无脏复用。

**明确不做**

- **不池化塔 / 玩家 / GameState**：churn 极低（一局建几次 / 每连接一次），池化收益接近零且增加 Mirror 复用风险。塔本体依旧 `Instantiate`。
- **不用 GF `EntityComponent`**：它自带 GF 资源异步加载 + 自增 EntityId + 自己的 show/hide 生命周期，与 Mirror 的 netId 生命周期会形成「两个所有者互相打架」。本次只用低层 `ObjectPoolComponent` 当缓存后端。
- 不做命中特效 `hitEffectPrefab` 的池化（纯本地 VFX，收益中等，留待后续）。

---

## 范围与优先级（按真实 churn 排序）

| 对象 | 生成点 | 销毁点 | churn | Phase2 处理 |
|------|--------|--------|-------|-------------|
| **子弹** | `TowerUnit.Fire` | `ProjectileBase.Update` / `HomingProjectileBase` | 极高 | ✅ 池化（最高收益） |
| **怪物** | `WaveManager.OnSpawnScheduled` | `Enemy.ReachBase` / `TakeDamage` | 高 | ✅ 池化 |
| **Buff** | `AddBuff` / 客户端快照重建 | `RemoveBuff` / OnRemove | 高 | ✅ 引用池 |
| 塔 | `GamePlayer.CmdBuildTower` | 几乎不销毁 | 极低 | ❌ 不做 |
| 玩家 | `PlayerManagerComponent` | 断线 | 极低 | ❌ 不做 |

> 说明：需求里的「塔防」若指**塔发射的子弹**，则子弹正是本 Phase 的第一优先；塔**本体**几乎不生成/销毁，池化无意义。

---

## 原理：Mirror 联网对象为什么不能直接扔进池

Mirror 默认 `NetworkServer.Spawn/Destroy` 就等于 `Instantiate/Destroy`。要复用同一个 GameObject，两端都得改：

| 端 | 默认行为 | 改成 |
|----|----------|------|
| 服务端销毁 | `NetworkServer.Destroy(go)`（真删对象） | `NetworkServer.UnSpawn(go)`（只下线、`resetState`，对象留着）→ 还回池 |
| 服务端生成 | `Instantiate` + `Spawn` | 从池取（空则补 `Instantiate`）→ `NetworkServer.Spawn` |
| 客户端 | 收到 spawn/destroy 消息各自 `Instantiate`/`Destroy` | `NetworkClient.RegisterPrefab(prefab, spawnHandler, unspawnHandler)`，句柄里从**客户端池**取/还 |

已确认这些 API 在项目内 Mirror 里都在：
- `NetworkServer.UnSpawn(GameObject)` — `Assets/ThirdParty/Mirror/Core/NetworkServer.cs:1923`
- `NetworkClient.RegisterPrefab(prefab, SpawnHandlerDelegate, UnSpawnDelegate)` — `Assets/ThirdParty/Mirror/Core/NetworkClient.cs:884`

> **Host 模式关键**：Host 既是 server 又是 client，spawn 的对象由服务端持有，Mirror **不会**对 Host 自己再走客户端 spawn/unspawn 句柄。所以客户端句柄里要 `if (NetworkServer.active) return;` 兜底，避免 Host 把同一对象在同一个池里回收两次。

---

## 五大「复用重置」坑（改造的真正工作量在这）

复用同一个 GameObject 时，以下状态**不会**自动回到出厂值，必须在每次「取出时」手动重置：

1. **`Awake` 只跑一次**：池化后复用不再触发 `Awake`。凡是「每次生成都要初始化」的逻辑，必须搬到 `OnStartServer`/`OnStartClient`（这俩每次 `NetworkServer.Spawn` 都会重跑）。`Awake` 只留「一次性缓存」（如 `GetComponent`、缓存 `originalColor`）。
2. **`SyncList` 不会自动清**（最大陷阱）：Day2 后 `Enemy` 挂 `AttributeComponent`（`SyncList<AttributeEntry>`）与 `BuffHolder`（`SyncList<BuffSnapshot>`）。`UnSpawn` 只重置 `NetworkIdentity`，**不动**你的自定义 SyncList。复用前必须清空并重播种，否则新怪物带着上一条命的血量/残留 Buff 上场。
3. **`NavMeshAgent`**：复用要先 `Warp` 到新出生点、`ResetPath()`，再设 `destination`。
4. **子弹运行态**：`lifetime`、`hasHitLocally`、`meshRenderer.enabled`、`serverTarget`、`targetNetId`、`lastKnownTargetPos` 全要复位；`TrailRenderer` 必须 `Clear()`，否则复用瞬间拖一条穿屏残影。
5. **表现关闭态**：命中时 `OnHitVisually` 关了 `meshRenderer`/`trail.emitting`，复用前要重新打开。

---

## 架构总览

```text
                    NetworkObjectPool（新增 GameFrameworkComponent）
                    ├── 后端：GameEntry.ObjectPool.CreateSingleSpawnObjectPool<NetworkPoolObject>(prefab.name)
                    │        每 prefab 一个池；池里缓存 NetworkPoolObject(包 GameObject)
                    │
      服务端 ───────┤  ServerSpawn(prefab,pos,rot, beforeSpawn):
                    │     pool.Spawn() 取空闲；空则 Instantiate+Register
                    │     → beforeSpawn(go)  // 设 SyncVar（如 ServerLaunch）
                    │     → NetworkServer.Spawn(go)
                    │  ServerDespawn(go):
                    │     NetworkServer.UnSpawn(go) → go.SetActive(false) → pool.Unspawn(go)
                    │
      客户端 ───────┘  RegisterPrefab(prefab, spawnHandler, unspawnHandler)
                       spawnHandler:  pool.Spawn()（空则 Instantiate+Register）→ 摆位 → 返回 go
                       unspawnHandler: if(NetworkServer.active) return; // Host 兜底
                                       go.SetActive(false) → pool.Unspawn(go)
```

## 文件改动清单

**新建**

```text
Assets/GameMain/Scripts/Base/Pool/
  NetworkPoolObject.cs      — ObjectBase 包 GameObject
  NetworkObjectPool.cs      — GameFrameworkComponent，双端胶水
  PooledTag.cs              — 记录 poolKey，回收时找回对应池
```

**修改**

```text
GameEntry.Custom.cs           — 暴露 GameEntry.NetworkPool（照 BuildComponent/PlayerManager 的写法）
GameNetworkManager.cs         — OnStartClient 注册可池化 prefab 的客户端句柄
WaveManager.OnSpawnScheduled  — 出怪走 ServerSpawn
Enemy.cs                      — ReachBase/TakeDamage 走 ServerDespawn；OnStartServer 加 ResetRuntimeState
TowerUnit.Fire                — 发射走 ServerSpawn(..., beforeSpawn)
ProjectileBase.cs             — 两处 Destroy → ServerDespawn；加复用重置
HomingProjectileBase.cs       — 一处 Destroy → ServerDespawn；重置 serverTarget/targetNetId
AttributeComponent.cs (Day2)  — 加 ResetForSpawn()：清 SyncList 并重播种 Base
BuffHolder.cs (Day2)          — 加 ClearAll()：清 serverBuffs + buffSnapshots（并 Release 引用池）
BuffBehaviour.cs (Day2)       — 实现 IReference，加 Clear()
SlowBuff.cs (Phase1)          — override Clear() 复位 percent/holder
BuffBehaviourRegistry.cs      — Create 改为 ReferencePool.Acquire
```

---

## 1. `NetworkPoolObject.cs`（新建）

```csharp
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
            // Name 作为池内检索用；Target 存 GameObject
            obj.Initialize(go.GetInstanceID().ToString(), go);
            return obj;
        }

        protected override void Release(bool isShutdown)
        {
            var go = Target as GameObject;
            if (go != null)
                Object.Destroy(go);   // 进池的对象已 UnSpawn，直接销毁 GO 即可
        }
    }
}
```

---

## 2. `PooledTag.cs`（新建）

```csharp
using UnityEngine;

namespace Tower
{
    /// <summary>挂在池化实例上，记录它属于哪个池（prefab 名），回收时据此找回对应池。</summary>
    public class PooledTag : MonoBehaviour
    {
        [HideInInspector] public string poolKey;
    }
}
```

---

## 3. `NetworkObjectPool.cs`（新建 · 核心胶水）

```csharp
using System.Collections.Generic;
using GameFramework.ObjectPool;
using Mirror;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    /// <summary>
    /// 联网对象池：GF ObjectPool 当后端，Mirror 当生成/回收 authority。
    /// 服务端用 ServerSpawn/ServerDespawn；客户端靠注册的 spawn/unspawn 句柄自动走池。
    /// </summary>
    public class NetworkObjectPool : GameFrameworkComponent
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
            var poolObj = pool.Spawn();               // 取一个空闲；没有则 null
            GameObject go;
            if (poolObj != null)
            {
                go = (GameObject)poolObj.Target;
                go.transform.SetPositionAndRotation(pos, rot);
            }
            else
            {
                go = Instantiate(prefab, pos, rot);
                go.name = prefab.name;                // 去掉 "(Clone)"，保证 poolKey 稳定
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
                Destroy(go);                           // 非池化对象兜底
                return;
            }
            go.SetActive(false);
            pool.Unspawn(go);                          // 按 Target 归还
        }

        // ===== 服务端 =====

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
            NetworkServer.UnSpawn(go);                 // 只下线，触发客户端 unspawn 句柄
            ReturnToPool(go);
        }

        // ===== 客户端句柄注册（对每个可池化 prefab 调一次）=====

        public void RegisterClientHandlers(GameObject prefab)
        {
            NetworkClient.RegisterPrefab(prefab,
                spawnHandler:   msg => ClientSpawn(prefab, msg),
                unspawnHandler: ClientUnspawn);
        }

        GameObject ClientSpawn(GameObject prefab, SpawnMessage msg)
        {
            // Host：服务端已持有该对象，Mirror 不会走到这里；纯客户端才用池
            return Acquire(prefab, msg.position, msg.rotation);
        }

        void ClientUnspawn(GameObject go)
        {
            if (NetworkServer.active) return;          // Host 兜底：服务端 ReturnToPool 已处理
            ReturnToPool(go);
        }
    }
}
```

> **为什么用 `SingleSpawn` 池**：每个 GameObject 实例同一时刻只能被「取用」一次（spawn 后要 unspawn 才能再取）。`MultiSpawn` 是给「一个对象被多处共享引用计数」的场景（如共享贴图），不适合独立实例。

---

## 4. `GameEntry.Custom.cs`（暴露访问器）

照现有 `BuildComponent` / `PlayerManagerComponent` 的自定义组件写法，加一个 `NetworkPool` 静态属性并在初始化里 `GetComponent<NetworkObjectPool>()`。之后全局用 `GameEntry.NetworkPool.ServerSpawn(...)`。

---

## 5. `GameNetworkManager` 客户端句柄注册

客户端要在收到 spawn 消息**之前**注册好句柄。给一处能拿到「所有可池化 prefab」的列表（可直接用 `NetworkManager.spawnPrefabs`，或单列一个 `List<GameObject> poolablePrefabs`）：

```csharp
public override void OnStartClient()
{
    base.OnStartClient();
    foreach (var prefab in poolablePrefabs)       // 子弹各类型 + 各怪物 prefab
        GameEntry.NetworkPool.RegisterClientHandlers(prefab);
}
```

> 只注册**要池化**的 prefab（子弹、怪物）。塔/玩家保持 Mirror 默认，不注册句柄。

---

## 6. `WaveManager.OnSpawnScheduled`（出怪走池）

```csharp
void OnSpawnScheduled(object sender, GameEventArgs e)
{
    if (e is not WaveSpawnEnemyEventArgs args) return;
    if (args.EnemyPrefab == null) { Debug.LogError("[Server] enemyPrefab is null."); return; }

    GameEntry.NetworkPool.ServerSpawn(args.EnemyPrefab, args.SpawnPosition, Quaternion.identity);
    spawnedThisWave++;
}
```

对照原始生成（要替换的两行）：

```139:140:F:\Download\Tower2022-main (1)\Tower2022-main\Assets\GameMain\Scripts\Gameplay\Wave\WaveManager.cs
            var go = Instantiate(args.EnemyPrefab, args.SpawnPosition, Quaternion.identity);
            NetworkServer.Spawn(go);
```

---

## 7. `Enemy.cs`（回收走池 + 复用重置）

**死亡/到达**：把 `NetworkServer.Destroy(gameObject)` 换成 `GameEntry.NetworkPool.ServerDespawn(gameObject)`（`ReachBase` 与 `TakeDamage` 两处）。

**复用重置**：`OnStartServer` 开头先重置残留态（`Awake` 只保留一次性缓存）：

```csharp
public override void OnStartServer()
{
    ResetRuntimeState();
    // ...原有：找 pathEnd、NavMesh.SamplePosition + Warp、设 destination...
}

void ResetRuntimeState()
{
    reachedBase = false;
    agent.ResetPath();

    // Day2：清掉上一条命的属性/Buff 残留（SyncList 不会自动清）
    GetComponent<AttributeComponent>().ResetForSpawn();  // 见 §11
    GetComponent<BuffHolder>().ClearAll();               // 见 §12
}
```

> 提醒：Day2 后 `Enemy` 已无 `hp` SyncVar / `speedMultiplier`——血量在 `AttributeComponent` 的 `ResourceAttribute` 里，减速走 Buff。所以「满血复活」靠 `ResetForSpawn` 重播种 Base 值，而不是重置某个 `hp` 字段。

---

## 8. `TowerUnit.Fire`（发射走池）

```csharp
[Server]
void Fire(Enemy target)
{
    GameEntry.NetworkPool.ServerSpawn(
        projectilePrefab, firePoint.position, Quaternion.identity,
        beforeSpawn: go =>
        {
            var proj = go.GetComponent<ProjectileBase>();
            proj.ServerLaunch(target, damage, projectileSpeed, firePoint.position);
        });
}
```

> `ServerLaunch` 设置的 `startPos`/`speed`/`damage` 等必须在 `beforeSpawn` 里、即 `NetworkServer.Spawn` **之前**赋值，初始 SyncVar 才会随生成消息下发。

---

## 9. `ProjectileBase.cs`（回收 + 复用重置）

**两处 `NetworkServer.Destroy(gameObject)` → `GameEntry.NetworkPool.ServerDespawn(gameObject)`**（超时/超距、命中确认后）。

**复用重置**：加一个 `ResetForSpawn()`，服务端在 `ServerLaunch` 里调、客户端在 `OnStartClient` 里调：

```csharp
protected virtual void ResetForSpawn()
{
    lifetime = 0f;
    hasHitLocally = false;
    if (meshRenderer != null) meshRenderer.enabled = true;
    if (trail != null) { trail.Clear(); trail.emitting = true; }  // 关键：清残影
}

public void ServerLaunch(Enemy target, int launchDamage, float launchSpeed, Vector3 launchPos)
{
    ResetForSpawn();
    damage = launchDamage; speed = launchSpeed; startPos = launchPos;
    OnServerLaunch(target);
}

public override void OnStartClient()
{
    ResetForSpawn();
    if (!isServer) transform.position = startPos;
}
```

---

## 10. `HomingProjectileBase.cs`（追踪态重置）

`Destroy` → `ServerDespawn`（`UpdateMovement` 里目标丢失那处）。并 override 重置，清追踪缓存：

```csharp
protected override void ResetForSpawn()
{
    base.ResetForSpawn();
    serverTarget = null;
    targetNetId = 0;
    lastKnownTargetPos = default;
    hasLastKnownTargetPos = false;
}
```

> `serverTarget`/`targetNetId` 会在 `OnServerLaunch` 里被重新赋值；这里清零是防「取到池里旧对象、launch 前的一帧」读到上次目标。

---

## 11. `AttributeComponent.ResetForSpawn()`（Day2 类补方法）

复用怪物时，`SyncList<AttributeEntry>` 与内部黑板都要回到出厂：

```csharp
/// <summary>池化复用：清空同步表与运行态，用初始 Base 重新播种。</summary>
public void ResetForSpawn()
{
    // 1) 清运行时 modifier（Flat/Percent 累加缓存）
    // 2) 把每个属性的 current 拉回 Base（含 ResourceAttribute 的当前值=Max）
    // 3) 清 SyncList 后按 Base 重写（或逐条 SyncSet 回 Base）
    // 具体字段名对齐 Day2 实现（syncedAttributes / attributes 字典 / Recalculate）
    Recalculate();     // 末尾统一重算并写回 SyncList
}
```

> 字段名以 Day2 `AttributeComponent` 实际实现为准（`w2-day2-detailed.md`）。要点是：**清残留 modifier + current 回满 + SyncList 与之一致**。

---

## 12. `BuffHolder.ClearAll()`（Day2 类补方法）

```csharp
/// <summary>池化复用：清掉所有 Buff（服务端表 + 同步快照），并归还引用池。</summary>
public void ClearAll()
{
    if (isServer)
    {
        foreach (var b in serverBuffs)
        {
            b.OnRemove(isServer: true);
            ReferencePool.Release(b);      // §13 引用池
        }
        serverBuffs.Clear();
        buffSnapshots.Clear();             // SyncList，客户端会收到移除
    }
}
```

---

## 13. Buff 引用池（`ReferencePool`）

### 13.1 `BuffBehaviour` 实现 `IReference`

```csharp
using GameFramework;

public abstract class BuffBehaviour : IReference
{
    // ...原有成员...

    /// <summary>引用池回收前清干净，防脏复用。子类 override 记得 base.Clear()。</summary>
    public virtual void Clear()
    {
        holder = null;
        // 复位基类公共运行态（start/duration 等）
    }
}
```

### 13.2 `SlowBuff.Clear`

```csharp
public override void Clear()
{
    base.Clear();
    percent = -0.4f;   // 回默认，避免复用时带上一次的强度
}
```

### 13.3 `BuffBehaviourRegistry` 改用 `Acquire`

```csharp
static readonly Dictionary<BuffDefId, Func<BuffBehaviour>> Factories = new()
{
    { BuffDefId.Slow, () => ReferencePool.Acquire<SlowBuff>() },
};
```

服务端 `AddBuff` 侧同理，把 `new SlowBuff { percent = slowPercent }` 换成：

```csharp
var buff = ReferencePool.Acquire<SlowBuff>();
buff.percent = slowPercent;
holder.AddBuff(buff, slowDuration);
```

### 13.4 Release 的全路径（务必全覆盖，否则泄漏/脏复用）

| 触发 | 位置 | 动作 |
|------|------|------|
| 服务端 Buff 到期/被移除 | `BuffHolder.RemoveBuff` | `OnRemove(true)` 后 `ReferencePool.Release(buff)` |
| 客户端快照移除 | `OnClientBuffRemove` | `OnRemove(false)` 后 `Release` 客户端重建的实例 |
| holder 清空（池化复用） | `BuffHolder.ClearAll`（§12） | 逐个 `Release` |
| holder 随对象销毁 | `OnStopServer/OnStopClient` | 若还有残留 Buff，逐个 `Release` |

> **同 DefId 刷新时长**（Phase1 规则）不新建 Buff，所以不产生额外 Acquire/Release——刷新路径别误 Release 掉正在用的那个。

---

## Host / 中途加入 复核

- **Host**：spawn 的对象服务端持有，Mirror 不对 Host 走客户端句柄；`ClientUnspawn` 里 `if (NetworkServer.active) return;` 保证只由 `ServerDespawn` 归还一次。
- **纯客户端**：全靠 `RegisterClientHandlers` 注册的句柄从客户端池取/还。
- **中途加入**：新客户端补收 spawn 消息 → 走 spawn 句柄从（自己进程的）池取；`SyncList<AttributeEntry>`/`SyncList<BuffSnapshot>` 会 replay，减速蓝色照常还原（Phase1 已验证）。
- **专用服务器**：只走服务端池；客户端句柄在 server 进程不触发。

---

## Phase2 Checklist

**新建 / 接线**

- [ ] `NetworkPoolObject` / `PooledTag` / `NetworkObjectPool`
- [ ] `GameEntry.NetworkPool` 访问器（照 BuildComponent 写法）
- [ ] `GameNetworkManager.OnStartClient` 注册子弹/怪物 prefab 句柄

**生成/回收替换**

- [ ] `WaveManager` 出怪 → `ServerSpawn`
- [ ] `Enemy` 两处销毁 → `ServerDespawn`
- [ ] `TowerUnit.Fire` → `ServerSpawn(..., beforeSpawn)`
- [ ] `ProjectileBase` 两处销毁 → `ServerDespawn`
- [ ] `HomingProjectileBase` 一处销毁 → `ServerDespawn`

**复用重置**

- [ ] `Enemy.ResetRuntimeState`（reachedBase / NavMesh / 属性 / Buff）
- [ ] `AttributeComponent.ResetForSpawn`（SyncList 清 + Base 重播种）
- [ ] `BuffHolder.ClearAll`（清表 + Release）
- [ ] `ProjectileBase.ResetForSpawn`（lifetime/hasHit/mesh/trail.Clear）
- [ ] `HomingProjectileBase` 重置 serverTarget/targetNetId

**Buff 引用池**

- [ ] `BuffBehaviour : IReference` + `Clear`
- [ ] `SlowBuff.Clear`
- [ ] `Registry` / `AddBuff` 改 `Acquire`
- [ ] 4 条 Release 路径全覆盖

**验收**

- [ ] 一局跑完，子弹/怪物仅在池空时 `Instantiate`；GC 尖峰下降
- [ ] 复用怪物满血、无残留 Buff、寻路正常
- [ ] 复用子弹无穿屏拖尾残影、命中正确
- [ ] Host / 纯客户端 / 专用服务器 表现一致
- [ ] Buff 到期与敌人死亡后无引用池泄漏（可在 Release 处打点计数）

---

## 关键提醒

1. **销毁用 `UnSpawn` 不是 `Destroy`**：`Destroy` 会真删对象、池化落空。
2. **`SyncList` 必须手动清**：复用最大坑，新怪物残血/残 Buff 都出在这。
3. **`Awake` 只跑一次**：每次生成要重置的逻辑放 `OnStartServer/OnStartClient`。
4. **`ServerLaunch` 必须在 `NetworkServer.Spawn` 之前**（用 `beforeSpawn` 回调），否则初始 SyncVar 不下发。
5. **Host 客户端句柄要 `if (NetworkServer.active) return;`**，避免双重归还。
6. **`TrailRenderer.Clear()`**：漏了就穿屏残影。
7. **引用池 Release 全路径覆盖**：漏 Release=泄漏，重复 Release=脏复用；刷新时长路径不要误 Release。
8. **只池化子弹 + 怪物**：塔/玩家不动。

---

## Day4 预告

投票系统（Voting Phase + `SyncList` / `SyncDictionary`），插在「一波清空 → 下一波 `PreWave`」之间（见 `2026-07-15-wave-phase-redesign.md` 的阶段衔接）。

---

## 相关文档

- `w2-day2-detailed.md`：Day2 Effect Pipeline（本文的前置地基；末尾「Day3 SlowBuff 预览」以本文为准）
- `active.md`：会话备忘

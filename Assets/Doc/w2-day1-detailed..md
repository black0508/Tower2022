# W2-Day1 — W1 重构收尾 + GameState 拆分

> 日期：2026-07-13 起
> 依赖：W1 已完成（`F:/Download/Tower2022-main`）
> 主题：**上午**收 W1 遗留 bug 和精简项（10 项），**下午**拆分 GameState 成 3 个组件
> **不做**：Effect Pipeline 骨架（顺延 Day2）

---

## Day1 目标

1. 收干净 W1 遗留的 bug 和架构小味道，代码基线清爽后再开 W2 新功能
2. 拆分 GameState，为 Day2 挂 BuffHolder / Day4 投票状态机预留清晰位置

## Day1 完成标志

- ✅ 10 项重构全部落地，编译通过、Host 模式一局能完整玩完
- ✅ GameState 拆成 `GameState` + `WaveDirector` + `PlayerManagerComponent` 三个组件
- ✅ GameEntry 上新增 `GameEntry.PlayerManager` 静态访问入口
- ✅ Prefab 引用全部重连，双端启动无 Null 报错
- ✅ 一局完整流程（Preparing → Wave 1-5 → Victory 或 Defeat）逻辑不变

## Day1 不做（明确边界）

- ❌ Effect Pipeline 骨架（Day2）
- ❌ BuffHolder 组件（Day2）
- ❌ towerType / enemyType 元数据字段（未来需要时再加）
- ❌ AttributeComponent（未来需要时再加）
- ❌ EventArgs 走 ReferencePool 改造（待确认现状后另开任务）
- ❌ 性能优化项（sqrMagnitude / LayerMask 等）
- ❌ 命名微调（killedThisWave 改名等）

---

# 上午：W1 重构 10 项

按依赖顺序做，前面的动完再动后面的。

---

## 1. `GameState.SpawnPlayer` 删除无效守卫

### 动机

`GameState.cs:106`：
```csharp
if (conn.identity != null) return;
```

这个判断本意是"防止同一 conn 被重复 Spawn"，但 `OnServerConnect` 每个连接只会调一次，且此时 `conn.identity` 一般本来就是 null（还没 Spawn）。**摆设 + 掩盖真实的重复 Spawn 场景**（如果真发生，应该报错而不是静默 return）。

### 改动

`GameState.cs:106`：
```csharp
// 删除这行
if (conn.identity != null) return;

// 改成
if (conn.identity != null) {
    Debug.LogWarning($"[Server] Player already spawned for conn {conn.connectionId}, skip.");
    return;
}
```

### 验证

原有 Host 模式启动流程无变化，日志正常。

---

## 2. `GamePlayer` Register 分 Host / Client-Only 场景

### 动机

`GamePlayer.cs:17-26`：
```csharp
public override void OnStartServer() {
    playerId = (int)netId;
    GameEntry.State?.RegisterPlayer(this);
}

public override void OnStartClient() {
    GameEntry.State?.RegisterPlayer(this);
}
```

Host 模式下两个方法都会跑，同一个 GamePlayer 被 Register 两次。当前靠 `!m_Players.Contains(player)` 挡住重复，能跑但**行为分裂**：
- Host：Register 两次（第二次被去重挡住）
- Dedicated Server：只 Register 一次（OnStartServer）
- Client-Only：只 Register 一次（OnStartClient）

### 改动

`GamePlayer.cs:17-26`：
```csharp
public override void OnStartServer() {
    playerId = (int)netId;
    GameEntry.PlayerManager?.RegisterPlayer(this);   // 注意：下午拆分后改成 PlayerManager
}

public override void OnStartClient() {
    if (isServer) return;   // Host 模式已经在 OnStartServer 注册过了
    GameEntry.PlayerManager?.RegisterPlayer(this);
}
```

**这里已经用到下午的 `GameEntry.PlayerManager`，所以本项和下午的拆分要一起提交。**

### 验证

- Host 启动，日志显示 Register 一次
- 断线重连（若测得到），Register 数量正确
- Dedicated Server + Client 启动（未来打包时验证），Register 数量正确

---

## 3. `HomeBase.OnStartClient` 补发 HpChanged 事件

### 动机

`HomeBase.cs` 只在 SyncVar hook 里 Fire `HomeBaseHpChangedEventArgs`。**新客户端加入时** hook 不触发（SyncVar 初始值同步走的是 SpawnMessage，不走 hook）。结果：新加入客户端的 GamingForm 打开时 UI 显示 0 或错误值。

当前 `GamingForm.RefreshHpFromScene` 用 `FindObjectOfType` 兜底，但 HomeBase 可能还没 Spawn 到客户端（时序问题），仍不可靠。

### 改动

`HomeBase.cs`，`OnStartClient` 里补一次 Fire：

```csharp
public override void OnStartClient() {
    // 通知 UI 一次当前值（新客户端加入用）
    GameEntry.Event.Fire(this, HomeBaseHpChangedEventArgs.Create(hp, maxHp));
}
```

（如果没有 `OnStartClient` 就加一个。）

### 验证

- Host 启动、加 Client、Client 的 GamingForm HP 显示正确
- HomeBase 被打中，两端 HP 显示都跟着变

---

## 4. `GamingForm.RefreshHpFromScene` 改成走事件初始化

### 动机

`GamingForm.cs:122-127`：
```csharp
void RefreshHpFromScene() {
    var homeBase = FindObjectOfType<HomeBase>();
    if (homeBase != null)
        RefreshHp(homeBase.hp, homeBase.maxHp);
}
```

- `FindObjectOfType` 慢
- 时序问题：Form.OnOpen 时 HomeBase 可能还没 Spawn（Host 模式下 OK，Client 加入模式下有风险）
- 承接 #3 后不再需要主动查询

### 改动

**方案 A（推荐）**：`GamingForm.OnOpen` 里删除 `RefreshHpFromScene()` 调用，改成 HomeBase 自己在 `OnStartClient` 里 Fire 事件（即 #3 的实现）。

**方案 B（兜底）**：如果 HomeBase.OnStartClient 触发时 GamingForm 还没打开，事件收不到。GamingForm.OnOpen 时可以做**从 GameEntry.HomeBase 主动读一次**（对应 #10 加的 `GameEntry.RegisterHomeBase`）：

```csharp
protected override void OnOpen(object userData) {
    // ...
    // 删除 RefreshHpFromScene()
    if (GameEntry.HomeBase != null)
        RefreshHp(GameEntry.HomeBase.hp, GameEntry.HomeBase.maxHp);
    // ...
}
```

**建议方案 B**（跟 #10 的 GameEntry.RegisterHomeBase 联动）。

### 验证

- 删掉 `RefreshHpFromScene` 方法后编译通过
- Host 打开 Form → HP 显示正确
- Client 加入后打开 Form → HP 显示正确

---

## 5. 删除遗留的 `EnemySpawner.cs`

### 动机

`Assets/GameMain/Scripts/Gameplay/Spawner/EnemySpawner.cs` 已被 WaveManager 取代，代码里再也没引用。留着容易误接线。

### 改动

- 删除文件 `EnemySpawner.cs` + `.meta`
- 如果场景里还有 EnemySpawner 组件引用，全部移除

### 验证

- 项目全局搜索 `EnemySpawner`，只剩 `WaveManager` 相关（如果 WaveManager 里没引用则完全清空）
- 编译通过

---

## 6. `CmdBuildTower` 传 `slotNetId` 代替 `worldPos`

### 动机

`GamePlayer.cs:52-95` 现状：客户端已经拿到 BuildSlot 对象，却传 worldPos，服务端再用 `BuildComponent.GetSlotAtPosition` 通过容差匹配反查一次。

问题：
- **容差匹配不精确**（`tolerance = 0.5f`，SqrMagnitude 阈值 0.25），Slot 挤密就可能匹配错
- **O(n) 遍历所有 Slot**
- **客户端已经知道 slotNetId，直接传就是权威身份**

### 改动

**GamePlayer.cs**：
```csharp
[Command]
public void CmdBuildTower(uint slotNetId, int towerConfigId)
{
    if (!NetworkServer.spawned.TryGetValue(slotNetId, out var identity)) {
        TargetBuildResult(connectionToClient, false, "Slot not found");
        return;
    }

    var slot = identity.GetComponent<BuildSlot>();
    if (slot == null) {
        TargetBuildResult(connectionToClient, false, "NetId is not a BuildSlot");
        return;
    }

    if (slot.IsOccupied) {
        TargetBuildResult(connectionToClient, false, "Slot occupied");
        return;
    }

    int cost = GetTowerCost(towerConfigId);
    if (GameEntry.State == null || !GameEntry.State.TrySpend(cost)) {
        TargetBuildResult(connectionToClient, false, "Not enough gold");
        return;
    }

    var prefab = GetTowerPrefab(towerConfigId);
    if (prefab == null) {
        TargetBuildResult(connectionToClient, false, "Unknown tower config");
        return;
    }

    var towerGo = Instantiate(prefab, slot.transform.position + Vector3.up * 0.5f, Quaternion.identity);
    NetworkServer.Spawn(towerGo);
    slot.occupiedByTowerNetId = towerGo.GetComponent<NetworkIdentity>().netId;

    Debug.Log($"[Server] Player {playerId} built towerConfigId={towerConfigId} at slot netId={slotNetId}");
    TargetBuildResult(connectionToClient, true, null);
}
```

**PlayerBuildModeComponent.cs:146**：
```csharp
// 原来
player.CmdBuildTower(slot.transform.position, selectedTowerConfigId);
// 改成
player.CmdBuildTower(slot.netId, selectedTowerConfigId);
```

**BuildComponent.cs**：`GetSlotAtPosition` 方法可以删除（如果没别的地方用）。全局搜一下确认。

### 验证

- 建塔流程无变化：进入建造模式 → 选塔 → 点 Slot → 塔立起来
- 服务端日志显示 slotNetId 而非 worldPos
- BuildComponent.GetSlotAtPosition 删除后编译通过

---

## 7. `HomingProjectileBase` 抽层

### 动机

`HomingProjectile.cs` 和 `FrostProjectile.cs` 有 90% 相同代码：
- `targetNetId` 字段
- `serverTarget` 字段
- `lastKnownTargetPos` / `hasLastKnownTargetPos` 缓存
- `TryGetTargetPos` 完全一样
- `UpdateMovement` 移动 + 朝向 + 距离检查完全一样

只有 `CheckServerHit` 不同（Homing 单体伤害 / Frost AoE + 减速）。

### 改动

**新增 `HomingProjectileBase.cs`**（放在 `Gameplay/Missile/` 下）：

```csharp
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 追踪型子弹的通用基类：处理目标查询、移动、朝向、本地预测命中。
    /// 子类只需实现 CheckServerHit（服务端命中判定与效果）。
    /// </summary>
    public abstract class HomingProjectileBase : ProjectileBase
    {
        protected const float HitDistance = 0.3f;

        [SyncVar] public uint targetNetId;

        [HideInInspector] public Enemy serverTarget;

        Vector3 lastKnownTargetPos;
        bool hasLastKnownTargetPos;

        protected override void OnServerLaunch(Enemy target)
        {
            targetNetId = target.netIdentity.netId;
            serverTarget = target;
        }

        protected override void UpdateMovement()
        {
            if (!TryGetTargetPos(out Vector3 targetPos))
            {
                if (isServer) NetworkServer.Destroy(gameObject);
                return;
            }

            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, speed * Time.deltaTime);

            var moveDir = targetPos - transform.position;
            if (moveDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(moveDir);

            if (!isServer && Vector3.Distance(transform.position, targetPos) < HitDistance)
                HitVisually();
        }

        protected bool TryGetTargetPos(out Vector3 pos)
        {
            if (isServer)
            {
                if (serverTarget != null && serverTarget.hp > 0)
                {
                    lastKnownTargetPos = serverTarget.transform.position;
                    hasLastKnownTargetPos = true;
                    pos = lastKnownTargetPos;
                    return true;
                }
            }
            else if (NetworkClient.spawned.TryGetValue(targetNetId, out var identity))
            {
                lastKnownTargetPos = identity.transform.position;
                hasLastKnownTargetPos = true;
                pos = lastKnownTargetPos;
                return true;
            }

            if (hasLastKnownTargetPos)
            {
                pos = lastKnownTargetPos;
                return true;
            }

            pos = default;
            return false;
        }
    }
}
```

**修改 `HomingProjectile.cs`**（大幅精简，只剩单体伤害逻辑）：

```csharp
using Mirror;
using UnityEngine;

namespace Tower
{
    public class HomingProjectile : HomingProjectileBase
    {
        protected override bool CheckServerHit()
        {
            if (serverTarget == null) return false;
            if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
                return false;

            if (serverTarget.hp > 0)
                serverTarget.TakeDamage(damage);
            return true;
        }
    }
}
```

**修改 `FrostProjectile.cs`**（大幅精简，只剩 AoE + 减速）：

```csharp
using Mirror;
using UnityEngine;

namespace Tower
{
    public class FrostProjectile : HomingProjectileBase
    {
        [Header("Frost Config")]
        public float aoeRadius = 2f;
        public float slowMultiplier = 0.6f;
        public float slowDuration = 3f;

        protected override bool CheckServerHit()
        {
            if (serverTarget == null) return false;
            if (Vector3.Distance(transform.position, serverTarget.transform.position) >= HitDistance)
                return false;

            var hits = Physics.OverlapSphere(transform.position, aoeRadius, ~0);
            foreach (var h in hits)
            {
                if (!h.TryGetComponent<Enemy>(out var enemy)) continue;
                enemy.TakeDamage(damage);
                enemy.ApplySlow(slowMultiplier, slowDuration);
            }

            return true;
        }
    }
}
```

### 验证

- Homing 塔攻击正常，敌人受伤
- Frost 塔攻击正常，AoE 内敌人受伤 + 减速
- 客户端本地预测（视觉命中）正常

---

## 8. `EnemyKilledEventArgs` → `EnemyRemovedEventArgs`（含 EventId 和 enum 名统一）

### 动机

Reason=`ReachedBase` 的敌人语义上不是"killed"（击杀是玩家动作），是"removed"（从场上消失）。当前命名让所有订阅点都要读 Reason 才能知道具体语义。

### 改动

**批量重命名**：
- `EnemyKilledEventArgs` → `EnemyRemovedEventArgs`
- `EnemyRemoveReason.KilledByPlayer` 保留
- `EnemyRemoveReason.ReachedBase` 保留
- 静态 EventId 字段自动跟着改（因为它是 `typeof(EnemyRemovedEventArgs).GetHashCode()` 或类似方式生成的）

**订阅点全部改**：
- `Enemy.cs`（Fire 处 2 个）
- `HomeBase.cs`（Subscribe/Unsubscribe/Handler）
- `WaveManager.cs`（Subscribe/Unsubscribe/Handler）
- `GameState.cs`（Subscribe/Unsubscribe/Handler，下午会搬到 WaveDirector）

### 验证

- 全局搜索 `EnemyKilled` 结果为 0
- 全局搜索 `EnemyRemoved` 覆盖上面的所有位置
- 编译通过
- 敌人被塔打死、走到基地，Reason 语义一致

---

## 9. `TowerBase` → `TowerUnit`

### 动机

`TowerBase` 和 `HomeBase` 都以 `Base` 结尾，语义完全不同（塔的具体类 vs 主基地）。W2 之后 Tower 要挂 BuffHolder、加 towerType 元数据，改名越晚越痛。

### 改动

- 文件重命名：`TowerBase.cs` → `TowerUnit.cs`
- 类名：`public class TowerBase` → `public class TowerUnit`
- 所有引用点：`GetComponent<TowerBase>()` / `TowerBase[]` 等全部改
- Prefab 的组件引用会跟着 rename 自动重连（Unity 通过 GUID 跟踪）

### 验证

- 编译通过
- Tower Prefab 在 Inspector 中显示 TowerUnit 组件（不丢引用）
- 塔攻击流程正常

---

## 10. `HomeBase` 加 `GameEntry.RegisterHomeBase` 静态入口

### 动机

- GameState 已经走 `GameEntry.RegisterState` 模式
- HomeBase 是场景静态 NetworkBehaviour，客户端需要通过 GameEntry 主动访问（承接 #4 的 GamingForm 初始化）
- 未来 Effect Pipeline 里 HomeBase 也是可能被 Buff 的目标

### 改动

**`GameEntry.Custom.cs`** 加：
```csharp
public static HomeBase HomeBase { get; private set; }

internal static void RegisterHomeBase(HomeBase homeBase) {
    HomeBase = homeBase;
}

internal static void UnregisterHomeBase(HomeBase homeBase) {
    if (HomeBase == homeBase)
        HomeBase = null;
}
```

**`HomeBase.cs`**：
```csharp
public override void OnStartServer() {
    GameEntry.RegisterHomeBase(this);
    GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
}

public override void OnStartClient() {
    GameEntry.RegisterHomeBase(this);
    GameEntry.Event.Fire(this, HomeBaseHpChangedEventArgs.Create(hp, maxHp));   // #3 的补发
}

public override void OnStopServer() {
    GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
    GameEntry.UnregisterHomeBase(this);
}

public override void OnStopClient() {
    GameEntry.UnregisterHomeBase(this);
}
```

**`GamingForm.OnOpen`**（承接 #4 方案 B）：
```csharp
if (GameEntry.HomeBase != null)
    RefreshHp(GameEntry.HomeBase.hp, GameEntry.HomeBase.maxHp);
```

### 验证

- Host 启动后 `GameEntry.HomeBase` 非 null
- Client 加入后 `GameEntry.HomeBase` 非 null
- GamingForm HP 显示正确

---

# 下午：GameState 拆分成 3 组

按依赖顺序：先建新组件的骨架 → 迁移逻辑 → 更新引用点 → 删除旧代码。

---

## 拆分蓝图

```
[改前]
GameState (NetworkBehaviour) — 260 行，塞了 9 类关注点

[改后]
GameEntry (场景根)
└── PlayerManagerComponent (GF Component, 双端)
    ├── 玩家列表管理 (Register/Unregister/Players/AreAllPlayersReady)
    ├── SpawnPlayer (服务端)
    ├── gamePlayerPrefab 字段
    └── TargetSyncBattleInfo (客户端加入时补同步)

GameState Prefab (NetworkServer.Spawn 出来的，双端都实例化)
├── GameState (NetworkBehaviour) ← 双端共有
│   ├── SyncVar: phase / currentWave / sharedGold / betweenWavesTimer / totalWaves
│   ├── SyncVar hook → Fire Event
│   ├── [Server] TrySpend(int)
│   ├── [Server] NotifyDefeat()
│   ├── [Server] AddGold(int)   ← 新加，给 WaveDirector 用
│   ├── OnStartServer 动态 AddComponent<WaveDirector>() ← 关键
│   └── RpcStartBattle
└── WaveDirector (MonoBehaviour, 运行时动态挂载, 仅服务端)
    ├── InitServer(GameState) ← 由 GameState.OnStartServer 调用
    ├── 引用 WaveManager
    ├── Update → TickPhase 分发
    ├── TickPreparing / TickWave / TickBetweenWaves
    ├── TransitionTo
    ├── GetDelayBeforeWave
    └── OnEnemyRemoved (KilledByPlayer) → GameState.AddGold
```

**Day2 会在 GameState Prefab 上再挂 BuffHolder 组件（双端共有，直接挂 Prefab）。**

**W2 挂载原则**（记住）：
- 双端共有 → 挂 Prefab
- 服务端专属 → OnStartServer 里 AddComponent
- 客户端专属 → OnStartClient 里 AddComponent

---

## Step A：新建 `PlayerManagerComponent`

### 位置

`Assets/GameMain/Scripts/Base/PlayerManagerComponent.cs`

### 代码骨架

```csharp
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    /// <summary>
    /// 玩家管理（双端）：玩家列表、Spawn、Ready 判断、加入补同步。
    /// 从 GameState 拆出来，避免 GameState 单类过重。
    /// </summary>
    public class PlayerManagerComponent : GameFrameworkComponent
    {
        readonly List<GamePlayer> m_Players = new();

        [SerializeField] GameObject gamePlayerPrefab;

        public IReadOnlyList<GamePlayer> Players => m_Players;

        public void RegisterPlayer(GamePlayer player)
        {
            if (player != null && !m_Players.Contains(player))
                m_Players.Add(player);
        }

        public void UnregisterPlayer(GamePlayer player)
        {
            if (player != null)
                m_Players.Remove(player);
        }

        public bool AreAllPlayersReady()
        {
            if (m_Players.Count == 0) return false;
            foreach (var p in m_Players)
                if (!p.isReady) return false;
            return true;
        }

        [Server]
        public void SpawnPlayer(NetworkConnectionToClient conn)
        {
            if (conn.identity != null) {
                Log.Warning($"[Server] Player already spawned for conn {conn.connectionId}, skip.");
                return;
            }
            if (gamePlayerPrefab == null) {
                Log.Error("[Server] gamePlayerPrefab not assigned on PlayerManagerComponent.");
                return;
            }

            var go = Instantiate(gamePlayerPrefab);
            go.name = $"{gamePlayerPrefab.name} [connId={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, go);

            // 补同步：新客户端加入时告知当前 totalWaves 等信息
            var state = GameEntry.State;
            if (state != null)
                TargetSyncBattleInfo(conn, state.totalWaves, state.currentWave);

            Log.Info($"[Server] Player added for connection {conn.connectionId}");
        }

        [TargetRpc]
        void TargetSyncBattleInfo(NetworkConnectionToClient conn, int totalWaves, int currentWave)
        {
            var state = GameEntry.State;
            if (state == null) return;
            state.totalWaves = totalWaves;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }
    }
}
```

**注意**：`GameFrameworkComponent` 里不能直接用 `[TargetRpc]`（`GameFrameworkComponent` 不是 NetworkBehaviour）。这里有两种方案：

- **方案 A**：`PlayerManagerComponent` 改成继承 `NetworkBehaviour` + 挂在 GameEntry 上（需要给 GameEntry 加 NetworkIdentity，架构变动大）
- **方案 B**：`TargetSyncBattleInfo` 走 **GameState** 上的 `[TargetRpc]`（GameState 是 NetworkBehaviour，本来就有这个方法），`PlayerManagerComponent.SpawnPlayer` 里调 `GameEntry.State.TargetSyncBattleInfo(conn, ...)`
- **方案 C**：`TargetSyncBattleInfo` 走 **GamePlayer** 上的 `[TargetRpc]`（每个 GamePlayer 自己有 NetworkIdentity，`conn.identity.GetComponent<GamePlayer>().TargetSyncSelf(...)`）

**推荐方案 B**：改动最小，GameState 已经有 `TargetSyncBattleInfo` 方法，只需要保留即可，`PlayerManagerComponent.SpawnPlayer` 里 `GameEntry.State?.TargetSyncBattleInfo(conn, ...)`。

**修正后的 SpawnPlayer**：

```csharp
[Server]
public void SpawnPlayer(NetworkConnectionToClient conn)
{
    if (conn.identity != null) {
        Log.Warning($"[Server] Player already spawned for conn {conn.connectionId}, skip.");
        return;
    }
    if (gamePlayerPrefab == null) {
        Log.Error("[Server] gamePlayerPrefab not assigned on PlayerManagerComponent.");
        return;
    }

    var go = Instantiate(gamePlayerPrefab);
    go.name = $"{gamePlayerPrefab.name} [connId={conn.connectionId}]";
    NetworkServer.AddPlayerForConnection(conn, go);

    // 补同步走 GameState 的 TargetRpc（GameState 是 NetworkBehaviour）
    GameEntry.State?.SendBattleInfoTo(conn);

    Log.Info($"[Server] Player added for connection {conn.connectionId}");
}
```

在 GameState 里加：
```csharp
[Server]
public void SendBattleInfoTo(NetworkConnectionToClient conn)
{
    TargetSyncBattleInfo(conn, totalWaves);
}
```

（`TargetSyncBattleInfo` 保留原样。）

### 挂载

- GameEntry 场景 GameObject 上通过 Inspector 添加 `PlayerManagerComponent`
- 配置 `gamePlayerPrefab` 字段（把原来挂在 GameState Prefab 上的 gamePlayerPrefab 拖过来）

### GameEntry.Custom.cs 新增静态入口

```csharp
public static PlayerManagerComponent PlayerManager { get; private set; }

private static void InitCustomComponents()
{
    NetWork = FindObjectOfType<GameNetworkManager>();
    Build = UnityGameFramework.Runtime.GameEntry.GetComponent<BuildComponent>();
    PlayerManager = UnityGameFramework.Runtime.GameEntry.GetComponent<PlayerManagerComponent>();
}
```

---

## Step B：新建 `WaveDirector`（服务端专属，运行时动态挂载）

### 关键设计原则

**GameState Prefab 双端都会实例化**（NetworkServer.Spawn 后所有客户端也会 Instantiate 一份）。所以：

- **双端共有的组件** → 直接挂在 Prefab 上（GameState、BuffHolder）
- **服务端专属组件** → 由服务端在 `OnStartServer` 里 `AddComponent<T>()`
- **客户端专属组件** → 由客户端在 `OnStartClient` 里 `AddComponent<T>()`

`WaveDirector` 是纯服务端逻辑（阶段状态机、击杀奖励），**不挂在 Prefab 上**，由 `GameState.OnStartServer` 动态添加。

### 位置

`Assets/GameMain/Scripts/Gameplay/Wave/WaveDirector.cs`

### 代码骨架

```csharp
using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 服务端专属：阶段状态机 + 波次推进 + 击杀奖励监听。
    /// 从 GameState 拆出来。由 GameState.OnStartServer 动态 AddComponent 添加。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        GameState gameState;
        WaveManager m_WaveManager;

        /// <summary>由 GameState.OnStartServer 调用，注入依赖并初始化。</summary>
        public void InitServer(GameState state)
        {
            gameState = state;

            EnsureWaveManager();
            if (m_WaveManager == null) {
                Debug.LogError("[Server] WaveManager not found in scene.");
                enabled = false;
                return;
            }

            m_WaveManager.InitServer();
            gameState.totalWaves = m_WaveManager.config != null ? m_WaveManager.config.waves.Length : 0;

            GameEntry.Event.Subscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void OnDestroy()
        {
            GameEntry.Event.Unsubscribe(EnemyRemovedEventArgs.EventId, OnEnemyRemoved);
        }

        void EnsureWaveManager()
        {
            if (m_WaveManager == null)
                m_WaveManager = FindObjectOfType<WaveManager>();
        }

        void Update()
        {
            if (gameState == null) return;
            // 由 GameState.OnStartServer 动态添加，不需要再判 NetworkServer.active
            // 但保留一层防御（避免 InitServer 未调用就跑 Update）
            if (!NetworkServer.active) return;

            TickPhase(Time.deltaTime);
        }

        void TickPhase(float deltaTime)
        {
            switch (gameState.phase)
            {
                case GamePhase.Preparing:
                    TickPreparing();
                    break;
                case GamePhase.Wave:
                    TickWave();
                    break;
                case GamePhase.BetweenWaves:
                    TickBetweenWaves(deltaTime);
                    break;
            }
        }

        void TickPreparing()
        {
            var pm = GameEntry.PlayerManager;
            if (pm != null && pm.AreAllPlayersReady())
                TransitionTo(GamePhase.BetweenWaves);
        }

        void TickWave()
        {
            if (m_WaveManager == null) return;

            m_WaveManager.ServerTick();
            if (!m_WaveManager.IsCurrentWaveCleared) return;

            if (m_WaveManager.IsLastWave)
                TransitionTo(GamePhase.Victory);
            else
                TransitionTo(GamePhase.BetweenWaves);
        }

        void TickBetweenWaves(float deltaTime)
        {
            gameState.betweenWavesTimer -= deltaTime;
            if (gameState.betweenWavesTimer > 0) return;

            gameState.currentWave++;
            m_WaveManager?.StartWave(gameState.currentWave - 1, skipDelayBeforeWave: true);
            TransitionTo(GamePhase.Wave);
        }

        void TransitionTo(GamePhase newPhase)
        {
            Debug.Log($"[Server] Phase: {gameState.phase} -> {newPhase}");
            gameState.phase = newPhase;

            switch (newPhase)
            {
                case GamePhase.BetweenWaves:
                    gameState.betweenWavesTimer = GetDelayBeforeWave(gameState.currentWave);
                    if (gameState.currentWave <= 0)
                        gameState.BroadcastStartBattle();
                    break;
                case GamePhase.Victory:
                    Debug.Log("[Server] === VICTORY ===");
                    break;
                case GamePhase.Defeat:
                    Debug.Log("[Server] === DEFEAT ===");
                    break;
            }
        }

        float GetDelayBeforeWave(int waveIndex)
        {
            var config = m_WaveManager?.config;
            if (config == null || waveIndex < 0 || waveIndex >= config.waves.Length)
                return 0;
            return config.waves[waveIndex].delayBeforeWave;
        }

        void OnEnemyRemoved(object sender, GameEventArgs e)
        {
            if (!NetworkServer.active) return;
            if (e is EnemyRemovedEventArgs args && args.Reason == EnemyRemoveReason.KilledByPlayer)
                gameState.AddGold(args.GoldAmount);
        }
    }
}
```

### 挂载

- GameState Prefab 上通过 Inspector 添加 `WaveDirector` 组件
- `gameState` 字段自动 GetComponent 兜底

---

## Step C：精简 `GameState`

### 拆分后的完整代码

```csharp
using GameFramework.Event;
using Mirror;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 全局游戏状态（网络对象）：只承载当前局的同步字段和权威变更接口。
    /// 阶段推进由 WaveDirector 负责；玩家列表由 PlayerManagerComponent 负责。
    /// </summary>
    public class GameState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnGoldChanged))]
        public int sharedGold = 100;

        [SyncVar(hook = nameof(OnPhaseChanged))]
        public GamePhase phase = GamePhase.Preparing;

        [SyncVar(hook = nameof(OnCurrentWaveChanged))]
        public int currentWave;

        [SyncVar] public float betweenWavesTimer;

        /// <summary>总波次：服务端由 WaveDirector 写入，客户端通过 TargetRpc 或 RpcStartBattle 同步。</summary>
        public int totalWaves;

        public int TotalWaves => totalWaves;

        public override void OnStartServer()
        {
            GameEntry.RegisterState(this);

            // 服务端专属组件运行时挂载（Prefab 双端实例化，不能直接挂）
            var director = gameObject.AddComponent<WaveDirector>();
            director.InitServer(this);
        }

        public override void OnStartClient()
        {
            GameEntry.RegisterState(this);

            if (GameEntry.UI != null && !GameEntry.UI.HasUIForm(UIFormId.GamingForm))
                GameEntry.UI.OpenUIForm(UIFormId.GamingForm);
        }

        void OnDestroy()
        {
            GameEntry.UnregisterState(this);
        }

        // ===== 服务端权威变更接口 =====

        [Server]
        public bool TrySpend(int amount)
        {
            if (sharedGold < amount) return false;
            sharedGold -= amount;
            return true;
        }

        [Server]
        public void AddGold(int amount)
        {
            if (amount <= 0) return;
            sharedGold += amount;
        }

        [Server]
        public void NotifyDefeat()
        {
            if (phase == GamePhase.Defeat || phase == GamePhase.Victory) return;
            phase = GamePhase.Defeat;
        }

        [Server]
        public void SendBattleInfoTo(NetworkConnectionToClient conn)
        {
            TargetSyncBattleInfo(conn, totalWaves);
        }

        [Server]
        public void BroadcastStartBattle()
        {
            RpcStartBattle(totalWaves);
        }

        // ===== SyncVar Hook → Event =====

        void OnGoldChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
        }

        void OnPhaseChanged(GamePhase oldVal, GamePhase newVal)
        {
            GameEntry.Event.Fire(this, GamePhaseChangedEventArgs.Create(newVal));
        }

        void OnCurrentWaveChanged(int oldVal, int newVal)
        {
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(newVal));
        }

        // ===== RPC =====

        [ClientRpc]
        void RpcStartBattle(int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }

        [TargetRpc]
        void TargetSyncBattleInfo(NetworkConnectionToClient _, int total)
        {
            totalWaves = total;
            GameEntry.Event.Fire(this, CurrentWaveChangedEventArgs.Create(currentWave));
        }
    }
}
```

### 关键变化

- 删掉了 `m_Players` / `RegisterPlayer` / `UnregisterPlayer` / `AreAllPlayersReady`（搬到 PlayerManagerComponent）
- 删掉了 `SpawnPlayer` / `gamePlayerPrefab` 字段（搬到 PlayerManagerComponent）
- 删掉了 `Update` / `TickPhase` / `TickPreparing` / `TickWave` / `TickBetweenWaves` / `TransitionTo` / `GetDelayBeforeWave`（搬到 WaveDirector）
- 删掉了 `OnEnemyKilled` 订阅（搬到 WaveDirector，改名 `OnEnemyRemoved`）
- 删掉了 `EnsureWaveManager` / `m_WaveManager` 引用（搬到 WaveDirector）
- 新增 `AddGold` 方法（供 WaveDirector 调用）
- 新增 `SendBattleInfoTo` / `BroadcastStartBattle` 方法（把 RPC 封装出来供拆出去的组件调用）

---

## Step D：更新引用点

### `GameNetworkManager.OnServerConnect`

原来：
```csharp
public override void OnServerConnect(NetworkConnectionToClient conn)
{
    base.OnServerConnect(conn);
    var state = GameEntry.State;
    if (state == null) { ... }
    state.SpawnPlayer(conn);
}
```

改成：
```csharp
public override void OnServerConnect(NetworkConnectionToClient conn)
{
    base.OnServerConnect(conn);
    Debug.Log($"[Server] Client connected: connId={conn.connectionId}");

    var pm = GameEntry.PlayerManager;
    if (pm == null) {
        Debug.LogError("[Server] PlayerManager not ready, cannot spawn player.");
        return;
    }
    pm.SpawnPlayer(conn);
}
```

### `GamePlayer.cs` 引用改成 PlayerManager

```csharp
public override void OnStartServer()
{
    playerId = (int)netId;
    GameEntry.PlayerManager?.RegisterPlayer(this);
}

public override void OnStartClient()
{
    if (isServer) return;
    GameEntry.PlayerManager?.RegisterPlayer(this);
}

void OnDestroy()
{
    GameEntry.PlayerManager?.UnregisterPlayer(this);
}
```

### `StatusPanelUI.GetLocalPlayerReady`

原来：
```csharp
foreach (var p in state.Players) { ... }
```
改成：
```csharp
var pm = GameEntry.PlayerManager;
if (pm == null) return false;
foreach (var p in pm.Players) { ... }
```

---

## Step E：Prefab 组件调整

**GameState Prefab**：
1. **不要挂 WaveDirector 组件**（服务端专属，由 `GameState.OnStartServer` 动态 `AddComponent`）
2. 保留 GameState 组件（字段变少但仍在）
3. **移除 `gamePlayerPrefab` 字段引用**（已搬到 PlayerManagerComponent）

**GameEntry 场景 GameObject**：
1. 通过 Inspector 添加 `PlayerManagerComponent`
2. 配置 `gamePlayerPrefab` 字段（拖 GamePlayer Prefab）

**关于双端组件差异的原则**（W2 后续也要遵循）：
- 双端共有组件 → 直接挂 Prefab（如 GameState 本身、Day2 的 BuffHolder）
- 服务端专属组件 → `OnStartServer` 里 `AddComponent<T>()`（如 WaveDirector）
- 客户端专属组件 → `OnStartClient` 里 `AddComponent<T>()`（当前无，未来可能有）

---

## Step F：编译 + 一局完整测试

### 编译检查

- 所有 `GameEntry.State.Players` → `GameEntry.PlayerManager.Players`
- 所有 `GameEntry.State.RegisterPlayer` → `GameEntry.PlayerManager.RegisterPlayer`
- 所有 `GameEntry.State.SpawnPlayer` → `GameEntry.PlayerManager.SpawnPlayer`
- 所有 `GameEntry.State.AreAllPlayersReady` → `GameEntry.PlayerManager.AreAllPlayersReady`
- 全局搜索 `EnemyKilledEventArgs` → 应该没有（都改成了 `EnemyRemovedEventArgs`）
- 全局搜索 `TowerBase` → 应该没有（都改成了 `TowerUnit`）
- 全局搜索 `EnemySpawner` → 只有 WaveManager 内部或完全没有

### Host 模式一局测试

1. Host 启动 → 场景加载 → GameState 被 Spawn
2. Host 玩家自动 Spawn → PlayerManager.Players 有 1 个玩家
3. 按 Space Ready → StatusPanel 显示"等待其他玩家..."
4. （单人测试）AreAllPlayersReady 返回 true → 进入 BetweenWaves
5. Wave 1 → 5 敌人陆续出来 → 塔攻击 → 敌人死亡 → 金币增加
6. Wave 完 → BetweenWaves 倒计时 → Wave 2
7. ...
8. Wave 5 完 → Victory Log

### Client 加入测试

1. Host 启动
2. Client（ParrelSync 副 Editor）Connect
3. Client 场景加载
4. Client 的 GamePlayer 被 Spawn
5. Client 的 PlayerManager.Players 有 2 个玩家
6. Client 的 GamingForm 打开 → HP / 金币 / 波次正确显示（承接 #3/#4/#10 验证）

---

# Day1 完成后的仓库状态

## 新增文件

- `Assets/GameMain/Scripts/Base/PlayerManagerComponent.cs`
- `Assets/GameMain/Scripts/Gameplay/Wave/WaveDirector.cs`
- `Assets/GameMain/Scripts/Gameplay/Missile/HomingProjectileBase.cs`

## 删除文件

- `Assets/GameMain/Scripts/Gameplay/Spawner/EnemySpawner.cs`
- （若无别处引用）`BuildComponent.GetSlotAtPosition` 方法内联删除

## 重命名文件

- `Assets/GameMain/Scripts/Gameplay/Enemy/TowerBase.cs` → `Assets/GameMain/Scripts/Gameplay/Tower/TowerUnit.cs`
  （建议顺手把塔相关代码从 `Gameplay/Enemy/` 挪到 `Gameplay/Tower/`）

## 主要修改文件

- `GameState.cs`（大幅精简：260 行 → 约 110 行）
- `GameNetworkManager.cs`（OnServerConnect 走 PlayerManager）
- `GamePlayer.cs`（Register 分场景 + CmdBuildTower 参数改 slotNetId）
- `HomeBase.cs`（OnStartClient 补 Fire + Register 到 GameEntry）
- `GamingForm.cs`（删 RefreshHpFromScene，走 GameEntry.HomeBase 初始化）
- `PlayerBuildModeComponent.cs`（CmdBuildTower 传 slotNetId）
- `HomingProjectile.cs` / `FrostProjectile.cs`（继承 HomingProjectileBase）
- `Enemy.cs`（EnemyKilledEventArgs → EnemyRemovedEventArgs）
- `HomeBase.cs`（订阅点改名）
- `WaveManager.cs`（订阅点改名）
- `EnemyKilledEventArgs.cs`（文件重命名 + 类改名 → `EnemyRemovedEventArgs.cs`）
- `GameEntry.Custom.cs`（加 PlayerManager / HomeBase 静态入口）
- `StatusPanelUI.cs`（Players 访问点改成 GameEntry.PlayerManager）

## Prefab 调整

- **GameState Prefab**：**不要挂 WaveDirector**（服务端专属，运行时 AddComponent）；移除 gamePlayerPrefab 字段
- **GameEntry 场景 GameObject**：添加 PlayerManagerComponent，配置 gamePlayerPrefab

---

# Day1 收尾 Checklist

按顺序打勾：

**上午（重构 10 项）**
- [ ] 1. GameState.SpawnPlayer 守卫改警告
- [ ] 2. GamePlayer.OnStartClient 分 Host/Client
- [ ] 3. HomeBase.OnStartClient 补 Fire HpChanged
- [ ] 4. GamingForm 删 RefreshHpFromScene
- [ ] 5. 删 EnemySpawner.cs
- [ ] 6. CmdBuildTower 传 slotNetId
- [ ] 7. HomingProjectileBase 抽层
- [ ] 8. EnemyKilledEventArgs → EnemyRemovedEventArgs
- [ ] 9. TowerBase → TowerUnit
- [ ] 10. GameEntry.RegisterHomeBase

**下午（GameState 拆分）**
- [ ] A. 新建 PlayerManagerComponent 并挂 GameEntry
- [ ] B. 新建 WaveDirector（**不挂 Prefab**，由 GameState.OnStartServer 动态 AddComponent）
- [ ] C. 精简 GameState（删掉 9 类关注点）
- [ ] D. 更新所有引用点（GameNetworkManager / GamePlayer / StatusPanelUI）
- [ ] E. Prefab 组件调整（GameState Prefab + GameEntry GameObject）
- [ ] F. 编译通过 + Host 一局跑完 + Client 加入验证

**收尾**
- [ ] 更新 `out/session/active.md`（记录 Day1 完成，进入 Day2）
- [ ] 提交 git（一次 commit 覆盖整个 Day1，commit msg：`refactor: W1 收尾 + GameState 拆分`）

---

# 已知延后项（在 W2 总览已记录，此处备查）

- Effect Pipeline 骨架 → Day2
- BuffHolder 挂载 → Day2
- towerType / enemyType 元数据 → 未来需要时
- AttributeComponent → 未来需要时
- Effect 查询式接口（ModifyXxx）→ 未来需要时
- EventArgs ReferencePool 改造 → 另开任务，先确认现状
- 性能优化（sqrMagnitude / LayerMask）→ 出瓶颈时
- 命名微调（killedThisWave → removedThisWave）→ 优先级低

---

# 相关文档

- `out/session/w2-overview.md`：W2 全周规划
- `out/session/w1-day5-7-detailed.md`：W1 最后阶段实现细节（含拆分前的 GameState 参考）
- `out/session/active.md`：项目备忘录

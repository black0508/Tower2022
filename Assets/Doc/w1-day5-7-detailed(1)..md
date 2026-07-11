# W1 Day 5-7 详细开发文档（当前版）

> 目标：W1 收尾 —— 从"永远刷怪"变成"能玩一整局"
> 工期：Day 5 ~5h、Day 6 ~5-6h、Day 7 ~4-5h（合计 ~14-16h）
> 前置：Day 4 已完成（玩家建塔 + 共享金币 + BuildSlot 系统 + GameState 已用 GF Event）
> 参考：[`ref-towerdefense-demo-wave-level-supplement.md`](./ref-towerdefense-demo-wave-level-supplement.md)（对照 GF Demo 波次/关卡设计）

---

## 总览

```
Day 5: GameStateManager 扩展（phase/wave/base HP）+ 多波次骨架
       → 游戏有开始有结束（跑通状态机）

Day 6: 第二种塔 Frost + Projectile 抽象重构（P2）
       → 塔多样性，兑现 Day 3 遗留的抽象债务

Day 7: HUD UI + 胜负结算 UI + 数值调优 + Buffer
       → 让 demo 看起来完整，能给别人演示
```

**W1 结束时状态**：
- 双 Editor 打开 → Host 按 Ready → 3 波怪按序生成
- 玩家用 Cannon + Frost 两种塔守 Base
- 守住 = Victory 全屏面板；Base HP 归零 = Defeat
- Host 按 Restart 重开一局
- HUD 显示 Gold / Wave / Base HP / 状态提示

---

## 全局架构约定（Day 5-7 通用）

### 1. 事件系统统一用 GF Event，禁用 UnityEvent

- 所有跨对象通信走 `GameEntry.Event.Fire(...)` + `Subscribe(...)`
- **禁止**在 NetworkBehaviour 里暴露 `UnityEvent<T>` 字段供外部订阅
- UI 层订阅 GF Event 而非 SyncVar hook 的 UnityEvent

### 2. Enemy 事件语义（方案 A：单事件 + reason 字段）

```csharp
public enum EnemyRemoveReason
{
    KilledByPlayer,   // 被玩家击杀（给金币）
    ReachedBase,      // 到达 Base（不给金币，扣 Base HP）
}

public class EnemyKilledEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(EnemyKilledEventArgs).GetHashCode();
    public override int Id => EventId;

    public EnemyRemoveReason Reason;
    public int GoldAmount;
    public int BaseDamage;    // ReachedBase 时才有效
    public Vector3 Position;
    public uint EnemyNetId;

    public static EnemyKilledEventArgs Create(EnemyRemoveReason reason, int gold, int baseDmg, Vector3 pos, uint netId)
    {
        var args = ReferencePool.Acquire<EnemyKilledEventArgs>();
        args.Reason = reason;
        args.GoldAmount = gold;
        args.BaseDamage = baseDmg;
        args.Position = pos;
        args.EnemyNetId = netId;
        return args;
    }

    public override void Clear() { /* GF 池化清理 */ }
}
```

**订阅者按 Reason 分发**：
- `GameState`：`if (reason == KilledByPlayer) sharedGold += args.GoldAmount;`
- `Base`：`if (reason == ReachedBase) TakeDamage(args.BaseDamage);`
- `WaveManager`：两种都算 killedThisWave（离开战场即计数）

### 3. GamePlayer 手动 Spawn + GameEntry 注册

- **不再使用** `NetworkManager.playerPrefab` 自动 Spawn
- `GameNetworkManager.OnServerAddPlayer` 手动 Instantiate + `NetworkServer.AddPlayerForConnection`
- 双端各自在 `OnStartServer` / `OnStartClient` 注册到 `GameEntry.RegisterPlayer(this)`
- `GameEntry.Players` 只读列表，`GameState.AreAllPlayersReady` 遍历此列表
- **PlayerManager 暂缓**（W2+）——现在用 GameEntry 静态 List 临时管理

### 4. 你已有的 GameState.cs（Day 4 已实现）

```csharp
public class GameState : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnGoldChanged))]
    public int sharedGold = 100;

    public override void OnStartServer()
    {
        GameEntry.RegisterState(this);
        GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
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
        GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
    }

    void OnEnemyKilled(object sender, GameEventArgs e)
    {
        if (!isServer) return;
        if (e is EnemyKilledEventArgs args)
            sharedGold += args.GoldAmount;
    }

    [Server]
    public bool TrySpend(int amount)
    {
        if (sharedGold < amount) return false;
        sharedGold -= amount;
        return true;
    }

    void OnGoldChanged(int oldVal, int newVal)
    {
        GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
    }
}
```

**Day 5 会在此基础上扩展**（不重写）：
- 加 `phase` / `currentWave` / `betweenWavesTimer` SyncVar
- 加 phase 切换 hook → Fire `GamePhaseChangedEventArgs`
- 加 Update 状态机
- `OnEnemyKilled` 加 Reason 分支（只 KilledByPlayer 加金币）

### 5. 参考 GF Demo 的波次设计（Day 5 借鉴）

对照项目：`E:\Unity\Project\TowerDefense-GameFramework-Demo-master\...`  
详细分析见 [`ref-towerdefense-demo-wave-level-supplement.md`](./ref-towerdefense-demo-wave-level-supplement.md)。

**我们采纳的部分**：

| 参考项目做法 | Tower2022 W1 落地 |
|-------------|-------------------|
| `Wave` + `WaveElement` 两级数据 | `WaveEntry` + `WaveSpawnElement[]`（SO 版，W2 再迁 DataTable） |
| `SpawnTime` 为相对上一只的间隔 | `WaveSpawnElement.spawnDelay` |
| `FinishWaitTime` 末只刷出后再等 N 秒 | `WaveEntry.finishWaitTime` |
| `SpawnEnemyEventArgs` 调度与执行解耦 | `WaveSpawnEnemyEventArgs` → `NetworkServer.Spawn` |
| `WaveInfoUpdateEventArgs` 驱动 HUD 进度 | Day 5 定义事件，Day 7 UI 订阅 |

**我们刻意不同的部分**（联机 W1 更合适）：

| 参考项目 | Tower2022 |
|----------|-----------|
| 波次时间轴可重叠（不等清场） | **清场后**才进 `BetweenWaves` / `Victory` |
| `DataPlayer.HP` 基地血 | `Base.hp` + 事件扣血 |
| UI 按钮 `StartWave` | 全员 `Ready` 后开始 |

### 6. 脚本路径约定

文档示例统一使用 **`Assets/GameMain/Scripts/`**（与 Day 4 已落地代码一致），命名空间 `Tower`。

---

## Day 5 — GameStateManager 扩展 + Base HP + 多波次（约5小时）

### 目标

- GameState 状态机扩展：Preparing → Wave → BetweenWaves → Victory/Defeat
- Base HP 系统：Enemy 到达 Base（走事件）扣 1 血，HP=0 触发 Defeat
- WaveManager：读 `WaveConfig` SO，**累积计时**刷怪；Fire `WaveSpawnEnemyEventArgs` 执行 Spawn
- 波次完成条件：**本波 spawn 阶段结束 + 场上敌人全部离场**（击杀或到 Base）
- 全员 Ready 机制：所有 Player.isReady=true 才能进入 Wave
- Enemy 事件重构：ReachBase 走事件，加 Reason 字段
- GamePlayer 手动 Spawn + GameEntry.RegisterPlayer
- 胜负判定 Console 打印（UI 留 Day 7）

### 核心架构

```
[NetworkBehaviour, 场景静态]
  GameState (Day 4 已存在，Day 5 扩展)
    - SyncVar sharedGold（已有）
    - +SyncVar phase, currentWave, betweenWavesTimer
    - +Update 状态机
    - +OnEnemyKilled 按 Reason 分支
    - Fire GamePhaseChangedEventArgs

[NetworkBehaviour, 与 GameState 平级]
  WaveManager
    - 引用 WaveConfig SO
    - 服务端维护 RuntimeWave（累积计时队列，参考 GF Demo Wave.cs）
    - Fire WaveSpawnEnemyEventArgs → 内部 OnSpawnScheduled 执行 NetworkServer.Spawn
    - 订阅 EnemyKilledEventArgs 计数 killedThisWave
    - 0.5s 节流 Fire WaveInfoUpdateEventArgs（Day 7 HUD 用）

[ScriptableObject]
  WaveConfig
    - WaveEntry[] waves
    - 每波：delayBeforeWave, finishWaitTime, WaveSpawnElement[] spawns
    - 每个元素：spawnDelay（距上一只）, enemyPrefab

[NetworkBehaviour, 场景静态]
  Base
    - SyncVar hp = 20
    - 服务端订阅 EnemyKilledEventArgs（Reason==ReachedBase 时扣血）
    - Fire BaseHpChangedEventArgs

[GamePlayer 变化]
  - 手动 Spawn（GameNetworkManager.OnServerAddPlayer）
  - OnStartServer/OnStartClient 双端注册 GameEntry.RegisterPlayer
  - +SyncVar isReady, CmdSetReady

[Enemy 变化]
  - 移除 FindWithTag("Base") 和直接引用
  - TakeDamage 死亡 → Fire EnemyKilledEventArgs(KilledByPlayer)
  - ReachBase → Fire EnemyKilledEventArgs(ReachedBase)
```

### 波次状态机（单波内部）

参考 GF Demo `Wave.ProcessWave`，Tower2022 在服务端维护等价逻辑：

```text
StartWave(index):
  构建 spawn 队列（每条元素累积 delay）
  totalSpawnEndTime = sum(spawnDelay) + finishWaitTime
  phase = Spawning

每帧 ServerTick:
  if delayBeforeWave > 0: 倒计时后 return
  if 队列非空且 timer 到达下一条 CumulativeTime:
      Fire WaveSpawnEnemyEventArgs → Spawn
  else if 队列空且 timer >= totalSpawnEndTime:
      spawnPhaseComplete = true

IsCurrentWaveCleared =
  spawnPhaseComplete && killedThisWave >= spawnedThisWave
```

> 与参考项目的差异：参考项目 `Wave.Finish` 只表示 spawn 时间轴走完；我们额外要求 **清场** 才切波，避免联机时「上一波残怪 + 下一波新怪」叠加失控。

### 数值（暂定，Day 7 调优）

- Base HP: 20
- 3 波怪（每波 5 只，间隔 2s，波前延迟 3s，末只后 finishWait 5s）：
  - Wave1：5× `spawnDelay=2`
  - Wave2：8× `spawnDelay=1.5`（可在 SO 里逐条填，或编辑器脚本批量生成）
  - Wave3：12× `spawnDelay=1`
- BetweenWaves 倒计时：15s
- 击杀金币：10（走事件参数）
- 到达 Base 伤害：1（走事件参数）

**W1 快速填表技巧**：在 `WaveConfig` Inspector 里，每波 `spawns` 数组长度 = 怪物数量，统一 `spawnDelay` 即可等价于旧的 `enemyCount + spawnInterval` 模型。

---

### Step 1：EnemyKilled 事件加 Reason 字段（20分钟）

#### 1.1 修改 EnemyKilledEventArgs

参考"全局架构约定 - 2"给出的完整代码。

**关键改动**：
- 加 `EnemyRemoveReason` 枚举
- EventArgs 加 `Reason / BaseDamage / Position / EnemyNetId` 字段
- `Create` 工厂方法参数变化

#### 1.2 修改 GameState.OnEnemyKilled

```csharp
void OnEnemyKilled(object sender, GameEventArgs e)
{
    if (!isServer) return;
    if (e is EnemyKilledEventArgs args)
    {
        // 只有被玩家击杀才给金币
        if (args.Reason == EnemyRemoveReason.KilledByPlayer)
            sharedGold += args.GoldAmount;
    }
}
```

#### 1.3 修改 Enemy.cs

```csharp
[Server]
public void TakeDamage(int dmg)
{
    hp -= dmg;
    if (hp <= 0)
    {
        GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
            reason: EnemyRemoveReason.KilledByPlayer,
            gold: 10,
            baseDmg: 0,
            pos: transform.position,
            netId: netId));
        NetworkServer.Destroy(gameObject);
    }
}

[Server]
void ReachBase()
{
    // ❌ 不再 FindWithTag / GetComponent<Base>
    GameEntry.Event.Fire(this, EnemyKilledEventArgs.Create(
        reason: EnemyRemoveReason.ReachedBase,
        gold: 0,
        baseDmg: 1,
        pos: transform.position,
        netId: netId));
    NetworkServer.Destroy(gameObject);
}
```

**Enemy 现在完全解耦**——不知道 Base 的存在，也不知道金币系统。它只 Fire 事件。

---

### Step 2：Base HP 系统（30分钟）

#### 2.1 写 Base.cs

`Assets/GameMain/Scripts/Gameplay/Base/Base.cs`：

```csharp
using Mirror;
using UnityEngine;

public class Base : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnHpChanged))]
    public int hp = 20;

    public int maxHp = 20;

    public override void OnStartServer()
    {
        GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
    }

    public override void OnStopServer()
    {
        GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
    }

    void OnEnemyKilled(object sender, GameEventArgs e)
    {
        if (!isServer) return;
        if (e is EnemyKilledEventArgs args && args.Reason == EnemyRemoveReason.ReachedBase)
        {
            TakeDamage(args.BaseDamage);
        }
    }

    [Server]
    void TakeDamage(int dmg)
    {
        hp = Mathf.Max(0, hp - dmg);
        Debug.Log($"[Server] Base took {dmg} damage, hp={hp}");

        if (hp <= 0)
        {
            // 通知 GameState 进入 Defeat
            var state = GameEntry.State;   // GameEntry 已有的注册接口
            if (state != null) state.NotifyDefeat();
        }
    }

    void OnHpChanged(int oldVal, int newVal)
    {
        GameEntry.Event.Fire(this, BaseHpChangedEventArgs.Create(newVal, maxHp));
    }
}
```

#### 2.2 挂到 Day 2 的 Base Cube

- 选场景里的 Base Cube
- 加 NetworkIdentity（**Scene Object 勾选**）
- 加 Base.cs 组件

#### 2.3 定义 BaseHpChangedEventArgs

```csharp
public class BaseHpChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(BaseHpChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int CurrentHp;
    public int MaxHp;

    public static BaseHpChangedEventArgs Create(int cur, int max)
    {
        var args = ReferencePool.Acquire<BaseHpChangedEventArgs>();
        args.CurrentHp = cur; args.MaxHp = max;
        return args;
    }

    public override void Clear() { }
}
```

---

### Step 3：GameState 状态机扩展（1小时）

#### 3.1 定义 GamePhase

`Assets/GameMain/Scripts/Gameplay/State/GamePhase.cs`：

```csharp
public enum GamePhase
{
    Preparing,
    Wave,
    BetweenWaves,
    Victory,
    Defeat,
}
```

#### 3.2 GameState 增量扩展

在你 Day 4 的 GameState 基础上追加（**不重写已有代码**）：

```csharp
public class GameState : NetworkBehaviour
{
    // ===== Day 4 已有 =====
    [SyncVar(hook = nameof(OnGoldChanged))]
    public int sharedGold = 100;

    // ===== Day 5 新增 =====
    [SyncVar(hook = nameof(OnPhaseChanged))]
    public GamePhase phase = GamePhase.Preparing;

    [SyncVar] public int currentWave;

    [SyncVar] public float betweenWavesTimer;

    [Header("Config")]
    public float betweenWavesDuration = 15f;

    [Header("References")]
    public WaveManager waveManager;

    public override void OnStartServer()
    {
        GameEntry.RegisterState(this);
        GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
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
        GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
    }

    // ===== Day 5 新增：状态机 =====

    [Server]
    void Update()
    {
        if (!isServer) return;

        switch (phase)
        {
            case GamePhase.Preparing:
                if (AreAllPlayersReady()) TransitionTo(GamePhase.Wave);
                break;

            case GamePhase.Wave:
                waveManager.ServerTick();
                if (waveManager.IsCurrentWaveCleared)
                {
                    if (waveManager.IsLastWave)
                        TransitionTo(GamePhase.Victory);
                    else
                        TransitionTo(GamePhase.BetweenWaves);
                }
                break;

            case GamePhase.BetweenWaves:
                betweenWavesTimer -= Time.deltaTime;
                if (betweenWavesTimer <= 0)
                {
                    currentWave++;
                    waveManager.StartWave(currentWave);
                    TransitionTo(GamePhase.Wave);
                }
                break;

            case GamePhase.Victory:
            case GamePhase.Defeat:
                break;
        }
    }

    [Server]
    void TransitionTo(GamePhase newPhase)
    {
        Debug.Log($"[Server] Phase: {phase} -> {newPhase}");
        phase = newPhase;

        switch (newPhase)
        {
            case GamePhase.Wave:
                if (currentWave == 0) waveManager.StartWave(0);
                break;
            case GamePhase.BetweenWaves:
                betweenWavesTimer = betweenWavesDuration;
                break;
            case GamePhase.Victory:
                Debug.Log("[Server] === VICTORY ===");
                break;
            case GamePhase.Defeat:
                Debug.Log("[Server] === DEFEAT ===");
                break;
        }
    }

    [Server]
    bool AreAllPlayersReady()
    {
        var players = GameEntry.Players;   // GameEntry 临时注册接口
        if (players.Count == 0) return false;
        foreach (var p in players) if (!p.isReady) return false;
        return true;
    }

    [Server]
    public void NotifyDefeat()
    {
        if (phase == GamePhase.Defeat || phase == GamePhase.Victory) return;
        TransitionTo(GamePhase.Defeat);
    }

    // ===== Day 5 修改：OnEnemyKilled 加 Reason 分支 =====

    void OnEnemyKilled(object sender, GameEventArgs e)
    {
        if (!isServer) return;
        if (e is EnemyKilledEventArgs args)
        {
            if (args.Reason == EnemyRemoveReason.KilledByPlayer)
                sharedGold += args.GoldAmount;
            // ReachedBase 由 Base 自己处理
        }
    }

    // ===== SyncVar Hooks（Fire GF Event，禁用 UnityEvent）=====

    void OnGoldChanged(int oldVal, int newVal)
    {
        GameEntry.Event.Fire(this, SharedGoldChangedEventArgs.Create(newVal));
    }

    void OnPhaseChanged(GamePhase oldVal, GamePhase newVal)
    {
        GameEntry.Event.Fire(this, GamePhaseChangedEventArgs.Create(newVal));
    }

    [Server]
    public bool TrySpend(int amount)
    {
        if (sharedGold < amount) return false;
        sharedGold -= amount;
        return true;
    }
}
```

#### 3.3 定义 GamePhaseChangedEventArgs

```csharp
public class GamePhaseChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(GamePhaseChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public GamePhase Phase;

    public static GamePhaseChangedEventArgs Create(GamePhase phase)
    {
        var args = ReferencePool.Acquire<GamePhaseChangedEventArgs>();
        args.Phase = phase;
        return args;
    }

    public override void Clear() { }
}
```

---

### Step 4：WaveConfig ScriptableObject（30分钟）

#### 4.1 数据结构（对齐 GF Demo 两级模型）

`Assets/GameMain/Scripts/Gameplay/Wave/WaveConfig.cs`：

```csharp
using UnityEngine;

namespace Tower
{
    [CreateAssetMenu(fileName = "WaveConfig", menuName = "TowerDefense/WaveConfig")]
    public class WaveConfig : ScriptableObject
    {
        public WaveEntry[] waves;
    }

    [System.Serializable]
    public class WaveEntry
    {
        [Tooltip("本波开始前等待（秒）")]
        public float delayBeforeWave = 3f;

        [Tooltip("最后一只按计划刷出后，本波 spawn 阶段还需等待（秒）。参考 GF Demo FinishWaitTime")]
        public float finishWaitTime = 5f;

        public WaveSpawnElement[] spawns;
    }

    [System.Serializable]
    public class WaveSpawnElement
    {
        [Tooltip("距上一只的间隔（秒）。第一只的间隔从 delayBeforeWave 结束后开始计")]
        public float spawnDelay = 2f;

        public GameObject enemyPrefab;
    }
}
```

#### 4.2 创建配置资产

- Project 右键 → Create → TowerDefense → WaveConfig
- 命名 `WaveConfig_W1_Level1.asset`
- **Wave 1**（5 只，间隔 2s）：`spawns` 长度 5，每条 `spawnDelay=2`，`enemyPrefab` 拖 Slime
- **Wave 2**（8 只，间隔 1.5s）：`spawns` 长度 8，`spawnDelay=1.5`
- **Wave 3**（12 只，间隔 1s）：`spawns` 长度 12，`spawnDelay=1`
- 每波 `finishWaitTime=5`

> W2+ 可将 `enemyPrefab` 换成 `int enemyConfigId`，走 GF DataTable，与参考项目 `DRWaveElement.EnemyId` 对齐。

#### 4.3 定义 WaveSpawnEnemyEventArgs（调度/执行解耦）

`Assets/GameMain/Scripts/Core/Events/EventArgs/WaveSpawnEnemyEventArgs.cs`：

```csharp
using GameFramework;
using GameFramework.Event;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 波次调度器决定「该刷了」时 Fire；由 WaveManager 订阅并执行 NetworkServer.Spawn。
    /// 对应参考项目 SpawnEnemyEventArgs。
    /// </summary>
    public sealed class WaveSpawnEnemyEventArgs : GameEventArgs
    {
        public static readonly int EventId = typeof(WaveSpawnEnemyEventArgs).GetHashCode();
        public override int Id => EventId;

        public int WaveIndex { get; private set; }
        public int SpawnIndex { get; private set; }
        public GameObject EnemyPrefab { get; private set; }
        public Vector3 SpawnPosition { get; private set; }

        public static WaveSpawnEnemyEventArgs Create(int waveIndex, int spawnIndex,
            GameObject prefab, Vector3 pos)
        {
            var args = ReferencePool.Acquire<WaveSpawnEnemyEventArgs>();
            args.WaveIndex = waveIndex;
            args.SpawnIndex = spawnIndex;
            args.EnemyPrefab = prefab;
            args.SpawnPosition = pos;
            return args;
        }

        public override void Clear()
        {
            WaveIndex = 0;
            SpawnIndex = 0;
            EnemyPrefab = null;
            SpawnPosition = default;
        }
    }
}
```

#### 4.4 定义 WaveInfoUpdateEventArgs（Day 7 HUD 用，Day 5 先埋事件）

```csharp
public sealed class WaveInfoUpdateEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(WaveInfoUpdateEventArgs).GetHashCode();
    public override int Id => EventId;

    public int CurrentWave { get; private set; }      // 1-based 显示
    public int TotalWaves { get; private set; }
    public float SpawnProgress { get; private set; }  // 0~1，本波刷怪时间轴进度

    public static WaveInfoUpdateEventArgs Create(int cur, int total, float progress)
    {
        var args = ReferencePool.Acquire<WaveInfoUpdateEventArgs>();
        args.CurrentWave = cur;
        args.TotalWaves = total;
        args.SpawnProgress = progress;
        return args;
    }

    public override void Clear() { }
}
```

---

### Step 5：WaveManager（1.5小时）

#### 5.1 写 WaveManager.cs（累积计时 + 事件驱动 Spawn）

`Assets/GameMain/Scripts/Gameplay/Wave/WaveManager.cs`：

```csharp
using GameFramework.Event;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Tower
{
    public class WaveManager : NetworkBehaviour
    {
        const float WaveInfoUpdateInterval = 0.5f;

        [Header("Config")]
        public WaveConfig config;
        public Transform spawnPoint;

        [Header("Runtime (Inspector 可观察)")]
        [SyncVar] public int spawnedThisWave;
        [SyncVar] public int killedThisWave;

        // 服务端运行时状态（参考 GF Demo Wave.cs）
        float waveTimer;
        float delayBeforeWaveTimer;
        float waveInfoTimer;
        float spawnPhaseEndTime;
        bool spawnPhaseComplete;
        int waveIndex;
        Queue<ScheduledSpawn> spawnQueue;

        struct ScheduledSpawn
        {
            public float cumulativeTime;
            public GameObject prefab;
            public int spawnIndex;
        }

        public bool IsCurrentWaveCleared =>
            spawnPhaseComplete && spawnedThisWave > 0 && killedThisWave >= spawnedThisWave;

        public bool IsLastWave => waveIndex >= config.waves.Length - 1;

        public override void OnStartServer()
        {
            spawnQueue = new Queue<ScheduledSpawn>();
            GameEntry.Event.Subscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
            GameEntry.Event.Subscribe(WaveSpawnEnemyEventArgs.EventId, OnSpawnScheduled);
        }

        public override void OnStopServer()
        {
            GameEntry.Event.Unsubscribe(EnemyKilledEventArgs.EventId, OnEnemyKilled);
            GameEntry.Event.Unsubscribe(WaveSpawnEnemyEventArgs.EventId, OnSpawnScheduled);
        }

        [Server]
        public void StartWave(int index)
        {
            if (config == null || index >= config.waves.Length) return;

            waveIndex = index;
            var entry = config.waves[index];
            spawnedThisWave = 0;
            killedThisWave = 0;
            waveTimer = 0;
            delayBeforeWaveTimer = entry.delayBeforeWave;
            waveInfoTimer = 0;
            spawnPhaseComplete = false;
            spawnQueue.Clear();

            float cumulative = 0;
            for (int i = 0; i < entry.spawns.Length; i++)
            {
                cumulative += entry.spawns[i].spawnDelay;
                spawnQueue.Enqueue(new ScheduledSpawn
                {
                    cumulativeTime = cumulative,
                    prefab = entry.spawns[i].enemyPrefab,
                    spawnIndex = i
                });
            }

            spawnPhaseEndTime = cumulative + entry.finishWaitTime;
            Debug.Log($"[Server] Wave {index + 1}/{config.waves.Length} started, spawns={entry.spawns.Length}");
        }

        [Server]
        public void ServerTick()
        {
            if (spawnPhaseComplete && killedThisWave >= spawnedThisWave) return;

            if (delayBeforeWaveTimer > 0)
            {
                delayBeforeWaveTimer -= Time.deltaTime;
                return;
            }

            waveTimer += Time.deltaTime;
            waveInfoTimer += Time.deltaTime;

            if (waveInfoTimer >= WaveInfoUpdateInterval)
            {
                waveInfoTimer = 0;
                float progress = spawnPhaseEndTime > 0 ? Mathf.Clamp01(waveTimer / spawnPhaseEndTime) : 1f;
                GameEntry.Event.Fire(this, WaveInfoUpdateEventArgs.Create(
                    waveIndex + 1, config.waves.Length, progress));
            }

            if (spawnQueue.Count > 0 && waveTimer >= spawnQueue.Peek().cumulativeTime)
            {
                var next = spawnQueue.Dequeue();
                GameEntry.Event.Fire(this, WaveSpawnEnemyEventArgs.Create(
                    waveIndex, next.spawnIndex, next.prefab, spawnPoint.position));
            }
            else if (spawnQueue.Count == 0 && waveTimer >= spawnPhaseEndTime)
            {
                spawnPhaseComplete = true;
            }
        }

        [Server]
        void OnSpawnScheduled(object sender, GameEventArgs e)
        {
            if (e is not WaveSpawnEnemyEventArgs args) return;
            if (args.EnemyPrefab == null) return;

            var go = Instantiate(args.EnemyPrefab, args.SpawnPosition, Quaternion.identity);
            NetworkServer.Spawn(go);
            spawnedThisWave++;
        }

        void OnEnemyKilled(object sender, GameEventArgs e)
        {
            if (!isServer) return;
            killedThisWave++; // KilledByPlayer 与 ReachedBase 都算离场
        }
    }
}
```

**设计说明**：

- `StartWave` 预计算 `cumulativeTime` 队列 — 与参考项目 `Wave.Create` 同思路。
- Fire `WaveSpawnEnemyEventArgs` 再 Subscribe 自己处理 Spawn — 保留扩展点（以后可换生成服务、对象池）。
- `spawnPhaseComplete` 只表示时间轴走完；`IsCurrentWaveCleared` 还要求清场。

#### 5.2 部署 WaveManager

- 单独 `[WaveManager]` 空物体
- 加 `NetworkIdentity`（Scene Object）+ `WaveManager.cs`
- Inspector 拖入 `config` / `spawnPoint`
- 回到 GameState 所在物体，把 `WaveManager` 拖到 `GameState.waveManager` 字段

#### 5.3 移除旧 EnemySpawner

- Day 2 的 `EnemySpawner.cs` 不再使用，从 SpawnPoint 移除
- `GameNetworkManager.OnStartServer` 里删除 `spawner.StartSpawning()` 相关代码

---

### Step 6：GamePlayer 手动 Spawn + GameEntry 注册（45分钟）

#### 6.1 GameEntry 加临时 Player 注册接口

`GameEntry.Player.cs`（partial class 扩展）：

```csharp
using System.Collections.Generic;

public static partial class GameEntry
{
    private static readonly List<GamePlayer> _players = new();
    public static IReadOnlyList<GamePlayer> Players => _players;

    public static void RegisterPlayer(GamePlayer p)
    {
        if (p != null && !_players.Contains(p)) _players.Add(p);
    }

    public static void UnregisterPlayer(GamePlayer p)
    {
        _players.Remove(p);
    }
}
```

**未来演进**：W2+ 抽出 PlayerManager 时，把这段逻辑迁到 PlayerManager，GameEntry 上层只暴露 `GameEntry.Players` 转发。

#### 6.2 修改 GamePlayer.cs

```csharp
using Mirror;
using UnityEngine;

public class GamePlayer : NetworkBehaviour
{
    [SyncVar] public int playerId;
    [SyncVar] public string playerName = "Player";

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady;

    // Day 4 保留字段
    public GameObject cannonTowerPrefab;
    public GameObject frostTowerPrefab;   // Day 6 加

    public override void OnStartServer()
    {
        playerId = (int)netId;
        GameEntry.RegisterPlayer(this);
    }

    public override void OnStartClient()
    {
        GameEntry.RegisterPlayer(this);
    }

    void OnDestroy()
    {
        GameEntry.UnregisterPlayer(this);
    }

    [Command]
    public void CmdSetReady(bool ready)
    {
        var state = GameEntry.State;
        if (state == null || state.phase != GamePhase.Preparing) return;
        isReady = ready;
    }

    void OnReadyChanged(bool oldVal, bool newVal)
    {
        GameEntry.Event.Fire(this, PlayerReadyChangedEventArgs.Create(playerId, newVal));
    }

    // Day 4 已有 CmdBuildTower 等（保留）
}
```

#### 6.3 定义 PlayerReadyChangedEventArgs

```csharp
public class PlayerReadyChangedEventArgs : GameEventArgs
{
    public static readonly int EventId = typeof(PlayerReadyChangedEventArgs).GetHashCode();
    public override int Id => EventId;

    public int PlayerId;
    public bool IsReady;

    public static PlayerReadyChangedEventArgs Create(int id, bool ready)
    {
        var args = ReferencePool.Acquire<PlayerReadyChangedEventArgs>();
        args.PlayerId = id; args.IsReady = ready;
        return args;
    }

    public override void Clear() { }
}
```

#### 6.4 修改 GameNetworkManager 手动 Spawn

```csharp
using Mirror;
using UnityEngine;

public class GameNetworkManager : NetworkManager
{
    [Header("Custom")]
    public GameObject gamePlayerPrefab;   // 自定义字段，不用 NetworkManager.playerPrefab

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("[Server] Server started");
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // 手动创建 GamePlayer，不用 base
        var go = Instantiate(gamePlayerPrefab);
        NetworkServer.AddPlayerForConnection(conn, go);
        Debug.Log($"[Server] Player added for connection {conn.connectionId}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Mirror 会自动 Destroy Player 对象，触发 OnDestroy → UnregisterPlayer
        base.OnServerDisconnect(conn);
    }
}
```

**Inspector 配置**：
- NetworkManager.playerPrefab **留空**
- gamePlayerPrefab 拖入 GamePlayer Prefab

#### 6.5 客户端 Ready 输入

BuildComponent 或新建 `PlayerInputComponent.cs`：

```csharp
void Update()
{
    if (!isLocalPlayer) return;

    var state = GameEntry.State;
    if (state != null && state.phase == GamePhase.Preparing
        && Input.GetKeyDown(KeyCode.Space))
    {
        var player = GetComponent<GamePlayer>();
        player.CmdSetReady(!player.isReady);
    }

    // 建造模式相关（Day 4 已有）
    // ...
}
```

---

### Step 7：测试（30分钟）

- 主 Editor Play → Start Server → Console "Server started"
- 副 Editor Play → Start Client
- 双端 Console：GamePlayer 各自 OnStartServer/OnStartClient 注册
- Host 按 Space：Console "Phase: Preparing" 期间 Player.isReady 变 true
- Client 按 Space：同上
- 全员 Ready → GameState.Update 检测到 → "Phase: Preparing -> Wave"
- Wave 1 (5 只) 全灭 → "Phase: Wave -> BetweenWaves"，倒计时 15s
- 15s 后 → Wave 2 (8 只)
- 3 波全过 → "=== VICTORY ==="
- 或者：怪走到 Base，HP 归零 → "=== DEFEAT ==="
- 断开一个 Client：GameEntry.Players 减少一个

---

### Day 5 验收清单

- ☐ EnemyKilledEventArgs 加 Reason / BaseDamage / Position / EnemyNetId 字段
- ☐ EnemyRemoveReason 枚举定义
- ☐ Enemy.TakeDamage 死亡 Fire EnemyKilled(Reason=KilledByPlayer)
- ☐ Enemy.ReachBase Fire EnemyKilled(Reason=ReachedBase)，无 Base 引用
- ☐ Enemy 移除 FindWithTag("Base") 相关代码
- ☐ Base.cs 挂在 Base Cube（NetworkIdentity Scene Object）
- ☐ Base 订阅 EnemyKilledEventArgs，Reason==ReachedBase 时扣血
- ☐ BaseHpChangedEventArgs 定义
- ☐ Base HP 归零调 GameState.NotifyDefeat
- ☐ GamePhase 枚举定义
- ☐ GameState 加 phase / currentWave / betweenWavesTimer SyncVar
- ☐ GameState.Update 状态机跑通（Preparing/Wave/BetweenWaves/Victory/Defeat）
- ☐ GameState.OnPhaseChanged Fire GamePhaseChangedEventArgs
- ☐ GameState.OnEnemyKilled 只在 KilledByPlayer 时加金币
- ☐ WaveConfig SO：`WaveEntry` + `WaveSpawnElement[]` + `finishWaitTime`
- ☐ WaveSpawnEnemyEventArgs / WaveInfoUpdateEventArgs 定义
- ☐ WaveManager 累积计时队列 + Fire WaveSpawnEnemyEventArgs
- ☐ WaveManager 清场判定：spawnPhaseComplete && killed >= spawned
- ☐ WaveManager 挂在场景，config/spawnPoint 拖入
- ☐ GameState.waveManager 引用 WaveManager
- ☐ GameEntry 加 RegisterPlayer / UnregisterPlayer / Players 接口
- ☐ GamePlayer.OnStartServer/OnStartClient 双端注册
- ☐ GamePlayer.OnDestroy 反注册
- ☐ GamePlayer.isReady SyncVar + CmdSetReady + OnReadyChanged 事件
- ☐ PlayerReadyChangedEventArgs 定义
- ☐ GameNetworkManager.OnServerAddPlayer 手动 Spawn（不用 base）
- ☐ NetworkManager.playerPrefab 字段留空
- ☐ 客户端按 Space 切换 Ready
- ☐ 全员 Ready 后 Phase 转到 Wave，第一波开始
- ☐ 3 波全通过 → Victory Console
- ☐ Base HP 归零 → Defeat Console
- ☐ 旧 EnemySpawner 移除
- ☐ 全文档零 UnityEvent，全部走 GF Event

---

### Day 5 必踩的坑

#### 坑1：Enemy 事件 Fire 时机
**原因**：`NetworkServer.Destroy` 会触发 OnDestroy，如果先 Destroy 再 Fire，事件里的 sender 已 null
**解决**：先 Fire 再 Destroy（代码顺序已正确）

#### 坑2：GameEntry.Players 客户端为空
**原因**：客户端 GamePlayer.OnStartClient 才注册，UI 或 UI 相关组件如果早于 OnStartClient 访问会拿到空列表
**解决**：UI 层订阅 PlayerReadyChangedEventArgs，或懒查询模式

#### 坑3：AreAllPlayersReady 单机测试卡死
**原因**：只有 Host 一人，遍历 Players 都是 Ready，可以过——但如果 Host 单机模式没 GamePlayer 呢？
**解决**：`if (players.Count == 0) return false;` 已在代码里，Host 模式 Mirror 会自动 AddPlayer

#### 坑4：手动 Spawn 后 Player 没绑定 connection
**症状**：CmdBuildTower 报 "Not the local player"
**解决**：`NetworkServer.AddPlayerForConnection(conn, go)` 必须调用（不能只 NetworkServer.Spawn），代码已正确

#### 坑5：OnDestroy 时 GameEntry 已析构
**原因**：Editor 停止 Play 时对象销毁顺序不确定
**解决**：GameEntry.UnregisterPlayer 内部判 null（已写）

#### 坑6：WaveManager 订阅事件时 GameEntry.Event 未初始化
**原因**：GF 组件 Awake 顺序
**解决**：GF 一般 GameEntry 优先加载。若报错改用 `Start()` 时机订阅

#### 坑7：spawnedThisWave 全部生成完但 killedThisWave 不到
**原因**：Reason=ReachedBase 也要计数（否则最后一只怪走到 Base 时本波永远不结束）
**验证**：WaveManager.OnEnemyKilled 里 killedThisWave++ 不判 Reason（代码已正确）

#### 坑8：SyncVar hook Fire GF Event 时机
**原因**：hook 在客户端触发，服务端不触发（服务端直接改字段不走 hook？）
**解决**：Mirror 中 SyncVar hook **双端都会触发**（值变化时），Fire 事件双端都收到——UI 无所谓，服务端订阅要判 `if (!isServer) return;` 之类

**特别注意**：客户端 Fire 的事件，服务端订阅者收不到（GF Event 是本地事件，不跨端）。跨端通信必须用 SyncVar / RPC，事件只在本端传播。

#### 坑9：NetworkManager.playerPrefab 空报警告
**原因**：Mirror 检查 playerPrefab 字段
**解决**：可能有编辑器警告但不影响运行；或手动置一个占位 Prefab（不用它，OnServerAddPlayer 覆盖）

---

## Day 6 — Frost 塔 + Projectile 抽象重构（约5-6小时）

### 目标

- 抽象 ProjectileBase 基类，重构 Homing 和 Frost 两个子类
- Frost 塔：AOE 减速 + 30 伤害
- Enemy 减速机制（speedMultiplier + 结束时间，NetworkTime 权威）
- 塔卡片列表加 Frost 卡片
- 减速视觉：Enemy 材质变蓝

### 数值

- Frost 伤害：30
- Frost 减速：40%（speedMultiplier 0.6）
- Frost 减速时长：3s
- Frost AOE 半径：2m
- Frost 成本：100
- Frost 攻击间隔：1.5s
- 减速叠加规则：**取最低**

---

### Step 1：Projectile 抽象基类（1小时）

#### 1.1 写 ProjectileBase.cs

`Assets/GameMain/Scripts/Gameplay/Missile/ProjectileBase.cs`：

```csharp
using Mirror;
using UnityEngine;

public abstract class ProjectileBase : NetworkBehaviour
{
    [Header("Sync State")]
    [SyncVar] public Vector3 startPos;
    [SyncVar] public float speed = 20f;

    [Header("References")]
    public MeshRenderer meshRenderer;
    public TrailRenderer trail;
    public GameObject hitEffectPrefab;

    [Header("Safety")]
    public float maxLifetime = 5f;
    public float maxRange = 50f;

    [HideInInspector] public int damage;
    [HideInInspector] public GameObject shooter;

    protected float lifetime;
    protected bool hasHitLocally;

    public override void OnStartClient()
    {
        if (!isServer) transform.position = startPos;
    }

    void Update()
    {
        lifetime += Time.deltaTime;
        if (lifetime > maxLifetime)
        {
            if (isServer) NetworkServer.Destroy(gameObject);
            return;
        }
        if (Vector3.Distance(transform.position, startPos) > maxRange)
        {
            if (isServer) NetworkServer.Destroy(gameObject);
            return;
        }

        if (hasHitLocally) return;

        UpdateMovement();

        if (isServer && CheckServerHit())
        {
            RpcConfirmHit();
            NetworkServer.Destroy(gameObject);
        }
    }

    protected abstract void UpdateMovement();
    protected abstract bool CheckServerHit();

    [ClientRpc]
    void RpcConfirmHit() => HitVisually();

    protected void HitVisually()
    {
        if (hasHitLocally) return;
        hasHitLocally = true;
        OnHitVisually();
    }

    protected virtual void OnHitVisually()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
        if (trail != null) trail.emitting = false;
        if (hitEffectPrefab != null)
            Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
    }
}
```

---

### Step 2：HomingProjectile（重构 Day 3 的 Projectile）（1小时）

#### 2.1 改名

- `Projectile.cs` → `HomingProjectile.cs`
- 类名 `Projectile` → `HomingProjectile`
- 继承改成 `: ProjectileBase`
- Prefab 名 `Projectile_Bullet` → `Projectile_HomingBullet`

#### 2.2 写 HomingProjectile.cs

```csharp
using Mirror;
using UnityEngine;

public class HomingProjectile : ProjectileBase
{
    [SyncVar] public uint targetNetId;

    [HideInInspector] public Enemy serverTarget;

    protected override void UpdateMovement()
    {
        Vector3 targetPos;
        if (isServer)
        {
            if (serverTarget == null || serverTarget.hp <= 0)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }
            targetPos = serverTarget.transform.position;
        }
        else
        {
            if (!NetworkClient.spawned.TryGetValue(targetNetId, out var targetIdentity))
            {
                HitVisually();
                return;
            }
            targetPos = targetIdentity.transform.position;
        }

        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, speed * Time.deltaTime);
    }

    protected override bool CheckServerHit()
    {
        if (serverTarget == null) return false;
        var dist = Vector3.Distance(transform.position, serverTarget.transform.position);
        if (dist < 0.3f)
        {
            serverTarget.TakeDamage(damage);
            return true;
        }
        return false;
    }
}
```

#### 2.3 修改 Tower.cs 引用

```csharp
public GameObject projectilePrefab;

[Server]
void Fire(Enemy target)
{
    var go = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
    var proj = go.GetComponent<HomingProjectile>();

    proj.targetNetId = target.netIdentity.netId;
    proj.startPos = firePoint.position;
    proj.speed = projectileSpeed;
    proj.damage = damage;
    proj.serverTarget = target;

    NetworkServer.Spawn(go);
}
```

---

### Step 3：Enemy 减速机制（45分钟）

#### 3.1 修改 Enemy.cs

```csharp
using Mirror;
using UnityEngine;
using UnityEngine.AI;

public class Enemy : NetworkBehaviour
{
    [Header("Stats")]
    [SyncVar] public int hp = 100;
    public int maxHp = 100;
    public float baseSpeed = 3f;

    [Header("Slow State")]
    [SyncVar(hook = nameof(OnSpeedMultiplierChanged))]
    public float speedMultiplier = 1f;

    [SyncVar] public float slowEndTime;

    [Header("Visual")]
    public MeshRenderer meshRenderer;
    private Color originalColor;

    private NavMeshAgent agent;
    private Transform baseTarget;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (meshRenderer != null) originalColor = meshRenderer.material.color;
    }

    // OnStartServer / OnStartClient 保持

    void Update()
    {
        if (!isServer) return;

        if (speedMultiplier < 1f && NetworkTime.time >= slowEndTime)
            speedMultiplier = 1f;

        agent.speed = baseSpeed * speedMultiplier;

        if (baseTarget != null && Vector3.Distance(transform.position, baseTarget.position) < 1f)
            ReachBase();
    }

    [Server]
    public void ApplySlow(float newMultiplier, float duration)
    {
        if (newMultiplier < speedMultiplier)
        {
            speedMultiplier = newMultiplier;
            slowEndTime = (float)NetworkTime.time + duration;
        }
        else if (newMultiplier == speedMultiplier)
        {
            slowEndTime = Mathf.Max(slowEndTime, (float)NetworkTime.time + duration);
        }
    }

    void OnSpeedMultiplierChanged(float oldVal, float newVal)
    {
        if (meshRenderer == null) return;
        meshRenderer.material.color = newVal < 1f ? Color.blue : originalColor;
    }

    // TakeDamage / ReachBase：Day 5 已改成 Fire EnemyKilledEventArgs
}
```

---

### Step 4：FrostProjectile（1小时）

`Assets/GameMain/Scripts/Gameplay/Missile/FrostProjectile.cs`：

```csharp
using Mirror;
using UnityEngine;

public class FrostProjectile : ProjectileBase
{
    [SyncVar] public uint targetNetId;

    [Header("Frost Config")]
    public float aoeRadius = 2f;
    public float slowMultiplier = 0.6f;
    public float slowDuration = 3f;

    [HideInInspector] public Enemy serverTarget;

    protected override void UpdateMovement()
    {
        Vector3 targetPos;
        if (isServer)
        {
            if (serverTarget == null || serverTarget.hp <= 0)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }
            targetPos = serverTarget.transform.position;
        }
        else
        {
            if (!NetworkClient.spawned.TryGetValue(targetNetId, out var targetIdentity))
            {
                HitVisually();
                return;
            }
            targetPos = targetIdentity.transform.position;
        }

        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, speed * Time.deltaTime);
    }

    protected override bool CheckServerHit()
    {
        if (serverTarget == null) return false;
        var dist = Vector3.Distance(transform.position, serverTarget.transform.position);
        if (dist < 0.3f)
        {
            var hits = Physics.OverlapSphere(transform.position, aoeRadius);
            foreach (var h in hits)
            {
                if (h.TryGetComponent<Enemy>(out var e))
                {
                    e.TakeDamage(damage);
                    e.ApplySlow(slowMultiplier, slowDuration);
                }
            }
            return true;
        }
        return false;
    }
}
```

**Prefab**：复制 Projectile_HomingBullet → Projectile_FrostBullet，材质换蓝色，脚本换 FrostProjectile，注册到 spawnPrefabs。

---

### Step 5：Frost 塔 Prefab（30分钟）

- 复制 Tower_Cannon → Tower_Frost，材质换蓝
- Tower.cs 数值：attackRange=5, attackInterval=1.5, damage=30, projectileSpeed=20, projectilePrefab=Projectile_FrostBullet
- 注册到 spawnPrefabs

---

### Step 6：塔卡片列表加 Frost（30分钟）

- 复制 TowerCard_Cannon → TowerCard_Frost，towerType=1, cost=100
- 修改 GamePlayer.cs：

```csharp
int GetTowerCost(int towerType) => towerType switch
{
    0 => 50, 1 => 100, _ => 0
};

GameObject GetTowerPrefab(int towerType) => towerType switch
{
    0 => cannonTowerPrefab,
    1 => frostTowerPrefab,
    _ => null
};
```

- Inspector 拖入 frostTowerPrefab

---

### Step 7：测试（45分钟）

- 建 Cannon（50 金）→ 打怪扣血
- 建 Frost（100 金）→ 打怪扣 30 血 + AOE 减速
- Enemy 变蓝，速度明显变慢
- 3 秒后 Enemy 恢复
- 多 Frost 命中同一 Enemy：取最低（不叠成更慢）

---

### Day 6 验收清单

- ☐ ProjectileBase.cs 抽象基类完成
- ☐ Projectile.cs 改名 HomingProjectile.cs
- ☐ Projectile_Bullet Prefab 改名 Projectile_HomingBullet
- ☐ Tower.cs 引用改成 HomingProjectile
- ☐ FrostProjectile.cs 完成（AOE + 减速）
- ☐ Projectile_FrostBullet Prefab 创建
- ☐ Enemy 加 speedMultiplier / slowEndTime SyncVar
- ☐ Enemy.ApplySlow 取最低规则
- ☐ 减速时材质变蓝
- ☐ Tower_Frost Prefab
- ☐ 塔卡片 UI 加 Frost
- ☐ GamePlayer 支持 towerType 0/1
- ☐ 双端建两种塔正常
- ☐ Frost AOE 命中多怪
- ☐ 减速取最低验证

---

### Day 6 必踩的坑

#### 坑1：ProjectileBase.Update 是虚方法要 override 吗
**答**：不是虚，Unity 直接调用基类 Update。子类只覆盖 UpdateMovement/CheckServerHit

#### 坑2：FrostProjectile OverlapSphere Layer
**解决**：`Physics.OverlapSphere(pos, radius, ~0)` 强制所有 Layer

#### 坑3：Enemy 减速视觉客户端不刷新
**原因**：SyncVar hook 只在值变化时触发
**解决**：Update 里兜底刷新 material.color

#### 坑4：NetworkTime.time vs Time.time
**必须用 NetworkTime.time**：双端一致

#### 坑5：材质 Instance 泄漏
**Day 6 简化**：暂不处理，W2 优化时统一

#### 坑6：Frost 塔单打时几乎永续减速
**原因**：3s 减速 > 1.5s 攻击间隔
**是否合理**：是，Frost 是控制塔

---

## Day 7 — HUD UI + 胜负结算 + 数值调优（约4-5小时）

### 目标

- HUD 显示 Gold / Wave / Base HP（订阅 GF Event）
- 状态提示（Preparing / BetweenWaves 倒计时 / 胜负）
- Victory / Defeat 全屏面板 + Restart 按钮（Host Only）
- 数值调优（打 3 局改 WaveConfig / Prefab）
- 写 w1-readme.md

---

### Step 1：HUD 顶部横条（1小时）

#### 1.1 UI 层级（GF UI Form 内部）

```
GamingForm (UIFormId.GamingForm, Day 4 GameState.OnStartClient 已 OpenUIForm)
  ├── HUD_Top
  │     ├── GoldText "Gold: 100"
  │     ├── WaveText "Wave: 1/3"
  │     └── BaseHpText "Base: 20/20"
  ├── BuildBar (Day 4 已有)
  ├── StatusMessage
  └── ResultPanel
```

#### 1.2 GamingForm 订阅事件

`Assets/GameMain/Scripts/UI/UIForms/GamingForm.cs`（GF UIForm）：

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using GameFramework.Event;

namespace Tower
{
    public class GamingForm : UGuiForm
    {
        [SerializeField] private Text goldText;
        [SerializeField] private Text waveText;
        [SerializeField] private Text baseHpText;
        [SerializeField] private Image waveProgressImg;  // 可选，参考 GF Demo fillAmount

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            GameEntry.Event.Subscribe(SharedGoldChangedEventArgs.EventId, OnGoldChanged);
            GameEntry.Event.Subscribe(BaseHpChangedEventArgs.EventId, OnBaseHpChanged);
            GameEntry.Event.Subscribe(GamePhaseChangedEventArgs.EventId, OnPhaseChanged);
            GameEntry.Event.Subscribe(WaveInfoUpdateEventArgs.EventId, OnWaveInfoUpdated);

            var state = GameEntry.State;
            if (state != null)
            {
                RefreshGold(state.sharedGold);
                RefreshWave(state.currentWave);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            GameEntry.Event.Unsubscribe(SharedGoldChangedEventArgs.EventId, OnGoldChanged);
            GameEntry.Event.Unsubscribe(BaseHpChangedEventArgs.EventId, OnBaseHpChanged);
            GameEntry.Event.Unsubscribe(GamePhaseChangedEventArgs.EventId, OnPhaseChanged);
            GameEntry.Event.Unsubscribe(WaveInfoUpdateEventArgs.EventId, OnWaveInfoUpdated);
            base.OnClose(isShutdown, userData);
        }

        void OnGoldChanged(object sender, GameEventArgs e)
        {
            RefreshGold(((SharedGoldChangedEventArgs)e).CurrentGold);
        }

        void OnBaseHpChanged(object sender, GameEventArgs e)
        {
            var args = (BaseHpChangedEventArgs)e;
            baseHpText.text = $"Base: {args.CurrentHp}/{args.MaxHp}";
        }

        void OnPhaseChanged(object sender, GameEventArgs e)
        {
            if (GameEntry.State != null)
                RefreshWave(GameEntry.State.currentWave);
        }

        void OnWaveInfoUpdated(object sender, GameEventArgs e)
        {
            var args = (WaveInfoUpdateEventArgs)e;
            waveText.text = $"Wave: {args.CurrentWave}/{args.TotalWaves}";
            if (waveProgressImg != null)
                waveProgressImg.fillAmount = args.SpawnProgress;
        }

        void RefreshGold(int gold) => goldText.text = $"Gold: {gold}";
        void RefreshWave(int wave)
        {
            var total = GameEntry.State?.waveManager?.config?.waves?.Length ?? 0;
            waveText.text = $"Wave: {wave + 1}/{total}";
        }
    }
}
```

---

### Step 2：状态提示 UI（1小时）

在 GamingForm 里加：

```csharp
[SerializeField] private GameObject statusPanel;
[SerializeField] private Text statusText;

void OnPhaseChanged(object sender, GameEventArgs e)
{
    var args = (GamePhaseChangedEventArgs)e;
    UpdateStatusUI(args.Phase);
    RefreshWave(GameEntry.State?.currentWave ?? 0);
}

void UpdateStatusUI(GamePhase phase)
{
    switch (phase)
    {
        case GamePhase.Preparing:
            statusPanel.SetActive(true);
            var localReady = GetLocalPlayerReady();
            statusText.text = localReady ? "Waiting for other players..." : "Press SPACE to Ready";
            break;
        case GamePhase.Wave:
            statusPanel.SetActive(false);
            break;
        case GamePhase.BetweenWaves:
            statusPanel.SetActive(true);
            // 倒计时在 Update 里刷（GamingForm 支持 OnUpdate）
            break;
        case GamePhase.Victory:
        case GamePhase.Defeat:
            statusPanel.SetActive(false);
            // ResultPanel 接管
            break;
    }
}

protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
{
    base.OnUpdate(elapseSeconds, realElapseSeconds);
    var state = GameEntry.State;
    if (state != null && state.phase == GamePhase.BetweenWaves)
    {
        statusText.text = $"Wave {state.currentWave + 2} in {Mathf.CeilToInt(state.betweenWavesTimer)}s";
    }
}

bool GetLocalPlayerReady()
{
    foreach (var p in GameEntry.Players)
        if (p.isLocalPlayer) return p.isReady;
    return false;
}
```

**订阅 PlayerReadyChangedEventArgs 刷新 Preparing 文本**：

```csharp
GameEntry.Event.Subscribe(PlayerReadyChangedEventArgs.EventId, OnPlayerReadyChanged);

void OnPlayerReadyChanged(object sender, GameEventArgs e)
{
    if (GameEntry.State?.phase == GamePhase.Preparing)
        UpdateStatusUI(GamePhase.Preparing);
}
```

---

### Step 3：Victory / Defeat 面板 + Restart（1小时）

GamingForm 里加：

```csharp
[SerializeField] private GameObject resultPanel;
[SerializeField] private Text resultText;
[SerializeField] private Button restartButton;

protected override void OnOpen(object userData)
{
    base.OnOpen(userData);
    // ...

    resultPanel.SetActive(false);
    restartButton.onClick.AddListener(OnRestartClicked);
}

void UpdateStatusUI(GamePhase phase)
{
    // ...
    switch (phase)
    {
        case GamePhase.Victory:
        case GamePhase.Defeat:
            statusPanel.SetActive(false);
            resultPanel.SetActive(true);
            resultText.text = phase == GamePhase.Victory ? "VICTORY" : "DEFEAT";
            resultText.color = phase == GamePhase.Victory ? Color.green : Color.red;
            restartButton.interactable = NetworkServer.active;
            break;
    }
}

void OnRestartClicked()
{
    if (!NetworkServer.active) return;
    NetworkManager.singleton.ServerChangeScene(
        NetworkManager.singleton.onlineScene);
}
```

---

### Step 4：数值调优（1-1.5小时）

#### 4.1 打 3 局

- 局 1：默认数值
- 局 2：调最疼的点
- 局 3：验证

#### 4.2 常见调优

| 问题 | 调优方向 |
|------|----------|
| 前期建不起塔 | 初始金币 100 → 150 |
| 过完 Wave 1 无敌 | 每波数量增加 |
| 3 波太快 | 加到 5 波 |
| Frost 用不到 | Frost 成本降到 80 |
| Cannon 过强 | Enemy HP 100 → 150 |

**只改 SO / Prefab，不改代码**。

#### 4.3 记录

`out/session/w1-tuning-notes.md`：初始 → 局 1 → 修改 → 局 2 → 定稿

---

### Step 5：写 w1-readme.md（30分钟）

`out/session/w1-readme.md`：

```markdown
# W1 交付说明

## 如何运行

1. Unity 打开项目，主 Editor Play → "Start Server"
2. ParrelSync 副 Editor Play → "Start Client"（IP: localhost）
3. 双端 Preparing 阶段各自按 Space Ready
4. 全员 Ready → 游戏开始

## 玩法

- **Space**：Preparing 阶段切换 Ready
- **B**：切换建造模式
- **点击底部塔卡片**：选中塔（金币不足自动置灰）
- **鼠标 + 左键**：建塔
- **ESC / 右键**：退出建造模式
- 守 Base，HP 归零 = Defeat；打通 3 波 = Victory
- Host 可点 Restart

## 已知问题（W2+ 处理）

- 无对象池
- 无死亡特效 / 音效
- 减速视觉简陋（材质变蓝）

## 项目结构（Day 5-7 新增）

- `Gameplay/State/GameState.cs` — 状态机（Day 4 存在，Day 5 扩展）
- `Gameplay/State/GamePhase.cs` — 状态枚举
- `Gameplay/Wave/WaveManager.cs` — 波次管理
- `Gameplay/Wave/WaveConfig.cs` — 波次 SO（WaveEntry + WaveSpawnElement）
- `Gameplay/Wave/WaveManager.cs` — 累积计时波次管理
- `Core/Events/EventArgs/WaveSpawnEnemyEventArgs.cs`
- `Core/Events/EventArgs/WaveInfoUpdateEventArgs.cs`
- `Gameplay/Base/Base.cs` — Base HP（订阅事件扣血）
- `Gameplay/Projectile/ProjectileBase.cs` — 抽象基类
- `Gameplay/Projectile/HomingProjectile.cs` — 追踪弹
- `Gameplay/Projectile/FrostProjectile.cs` — 冰弹
- `Event/EnemyKilledEventArgs.cs` — 加 Reason
- `Event/BaseHpChangedEventArgs.cs`
- `Event/GamePhaseChangedEventArgs.cs`
- `Event/PlayerReadyChangedEventArgs.cs`
- `UI/GamingForm.cs` — HUD + 状态 + 结算
```

---

### Day 7 验收清单

- ☐ HUD 顶部显示 Gold / Wave / Base HP，全部走 GF Event 订阅
- ☐ Wave 进度条订阅 WaveInfoUpdateEventArgs.SpawnProgress
- ☐ Preparing 中央显示 "Press SPACE to Ready" / "Waiting..."
- ☐ BetweenWaves 显示倒计时
- ☐ Victory 绿色 / Defeat 红色面板
- ☐ Host 的 Restart 按钮可点，Client 置灰
- ☐ Restart 后场景重载
- ☐ 打 3 局完整流程无严重 bug
- ☐ 30% 会输 / 70% 能过
- ☐ Cannon + Frost 都被使用
- ☐ w1-readme.md 完成
- ☐ 全文档零 UnityEvent

---

### Day 7 必踩的坑

#### 坑1：ServerChangeScene 后 GameEntry.State 是旧引用
**原因**：旧 GameState Destroy → UnregisterState，新 GameState OnStartClient → RegisterState
**验证**：切换过程中 GameEntry.State 会有短暂 null，UI 判 null

#### 坑2：GamingForm 场景切换后 OnClose 未触发
**原因**：GF UIForm 生命周期 vs Mirror 场景切换
**解决**：ServerChangeScene 前手动 CloseUIForm，或在 OnClose 判 isShutdown

#### 坑3：SyncVar hook Fire GF Event，服务端订阅收得到吗
**答**：hook 双端触发，服务端也 Fire 事件。GF Event 是本地事件，服务端 Fire → 服务端订阅者收到；客户端 Fire → 客户端订阅者收到。**跨端不会串**

#### 坑4：Restart 后 Player 列表残留
**原因**：ServerChangeScene 会 Despawn 所有对象，OnDestroy 触发 UnregisterPlayer
**验证**：重载后 GameEntry.Players.Count 应为 0，重连后重新填充

---

## W1 收尾（Day 7 结束时）

- ☐ 从 Preparing 到 Victory/Defeat 完整流程
- ☐ Cannon + Frost 两种塔
- ☐ HUD 完整
- ☐ Restart 可用
- ☐ 全部走 GF Event，零 UnityEvent
- ☐ Enemy / Base / GameState / WaveManager 通过事件解耦
- ☐ w1-readme.md 完成

**W2 起点**：对象池、Effect Pipeline、通用 Buff 系统、更多敌人和塔类型、PlayerManager 抽出。

---

## Day 5-7 整体不要做的事

- ❌ 通用 Buff 系统（Frost 用简化 speedMultiplier）
- ❌ 对象池
- ❌ 音效 / 死亡特效
- ❌ 多关卡选择
- ❌ 存档 / 排行榜
- ❌ Boss / 更多敌人 / 塔升级
- ❌ PlayerManager（暂用 GameEntry 静态 List）

---

## 卡住时的求救清单

1. **报错**：粘贴报错 + 操作步骤
2. **不同步**：spawnPrefabs / NetworkIdentity Scene Object / [Server] 标记
3. **状态机卡住**：GameState.phase Console 打印
4. **UI 不刷新**：GameEntry.Event 订阅是否成功、事件是否 Fire
5. **数值失衡**：改 SO 和 Prefab

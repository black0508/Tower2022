# W1 Day 3-4 详细开发文档

> 目标：
> - **Day 3**：场景里手动放塔，塔自动开火打死敌人（双端同步）
> - **Day 4**：待讨论
> 工期：Day 3 约3小时，Day 4 待估
> 范围：核心战斗循环 + （Day 4 内容待定）
> 前置：Day 2 已完成（敌人沿路径走 + Spawner定时生成）

---

## 总览

```
Day 3: 塔检测敌人 + 开火扣血（双端模拟视觉子弹 + 服务器权威伤害）
Day 4: 待讨论
```

**Day 3 完成后能看到**：
- 路径上手动放的塔自动开火
- 子弹追踪敌人，飞行流畅
- 敌人扣血、死亡、销毁，两端同步

---

## 核心概念速览

### 子弹同步策略（Day 3 核心）

**采用方案D：网络对象 + 视觉本地预测**

```
Projectile = NetworkBehaviour（有NetworkIdentity）
  - SyncVar：targetNetId / startPos / speed / hasHitVisually
  - 不挂 NetworkTransform，位置不同步
  - 服务器和客户端都用相同的MoveTowards追踪

客户端追到目标 → 立刻"假命中"：播特效、隐藏Mesh、停拖尾
                  但不Destroy（等服务器销毁）

服务器追到目标 → TakeDamage + 设hasHitVisually=true + NetworkServer.Destroy

中途加入 → Mirror自动同步现有子弹，从startPos开始模拟
```

**优势**：
- 视觉响应快（本地预测，立即假命中）
- 销毁权威性强（服务器决定）
- 中途加入能看到现有子弹
- 带宽中等（Spawn + SyncVar + Despawn，无每帧位置）
- 防作弊强（伤害和销毁都服务器权威）

**对应UE**：类似 LetsGo MoeBulletLauncher 的 NetworkObject + 选择性复制思路，但更激进（位置完全不同步）。

### 重构原则

- **子弹自治**：Tower 只 Spawn 子弹，**不管子弹怎么飞**
- **双端共享代码**：Update 里 isServer 分支隔离权威动作（扣血 vs 假命中），追踪逻辑共享
- **职责单一**：Tower 选目标+开火，Projectile 飞行+命中，Enemy 受伤+移动

---

## Day 3 — 塔自动攻击（约3小时）

### Step 1：创建 Tower Prefab（30分钟）

#### 1.1 创建 GameObject
- Hierarchy → 3D Object → Cube，命名 `Tower_Cannon`
- Scale (1, 2, 1)，让塔显得高一点
- 子物体：再加一个小 Cube 命名 `FirePoint`，放在塔顶（局部位置 (0, 1.2, 0)）

#### 1.2 加组件到 Tower_Cannon
- `NetworkIdentity`
- `SphereCollider`（**isTrigger ✅**, radius = 5，作为攻击范围检测）
- `Tower.cs`（自己写，下面给代码）

⚠️ **不要加 Rigidbody**。Trigger 触发要求"双方至少一方有Rigidbody"，**Enemy 已经有 kinematic Rigidbody**，所以塔不需要。

#### 1.3 注意 Layer Collision
- Edit → Project Settings → Physics → Layer Collision Matrix
- 默认 Default × Default 是勾选的，不用改
- 如果你给Tower/Enemy设了Layer，确保它们之间能碰撞

---

### Step 2：写 Tower.cs（1小时）

**职责**：选目标 + 计时 + Spawn子弹。**不管子弹怎么移动**（子弹自治）。

`Assets/Scripts/Gameplay/Tower/Tower.cs`：

```csharp
using Mirror;
using UnityEngine;
using System.Collections.Generic;

public class Tower : NetworkBehaviour
{
    [Header("Network State")]
    [SyncVar] public int ownerPlayerId = -1;
    [SyncVar] public int level = 1;
    [SyncVar] public int hp = 100;
    [SyncVar] public int maxHp = 100;

    [Header("Attack Config")]
    public float attackRange = 5f;
    public float attackInterval = 1f;
    public int damage = 25;
    public float projectileSpeed = 20f;

    [Header("References")]
    public Transform firePoint;
    public GameObject projectilePrefab;

    [Header("Runtime State")]
    [SerializeField] private List<Enemy> enemiesInRange = new();
    [SerializeField] private Enemy currentTarget;
    [SerializeField] private float attackTimer;

    void OnTriggerEnter(Collider c)
    {
        if (!isServer) return;
        if (c.TryGetComponent<Enemy>(out var e) && !enemiesInRange.Contains(e))
            enemiesInRange.Add(e);
    }

    void OnTriggerExit(Collider c)
    {
        if (!isServer) return;
        if (c.TryGetComponent<Enemy>(out var e))
            enemiesInRange.Remove(e);
    }

    void Update()
    {
        if (!isServer) return;

        enemiesInRange.RemoveAll(e => e == null || e.hp <= 0);

        if (currentTarget == null || currentTarget.hp <= 0 || !enemiesInRange.Contains(currentTarget))
            currentTarget = enemiesInRange.Count > 0 ? enemiesInRange[0] : null;

        if (currentTarget != null) {
            attackTimer += Time.deltaTime;
            if (attackTimer >= attackInterval) {
                attackTimer = 0;
                Fire(currentTarget);
            }
        }
    }

    [Server]
    void Fire(Enemy target)
    {
        var go = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        var proj = go.GetComponent<Projectile>();

        // 同步字段（客户端会收到）
        proj.targetNetId = target.netIdentity.netId;
        proj.startPos = firePoint.position;
        proj.speed = projectileSpeed;

        // 服务器内部字段（不同步，仅服务器使用）
        proj.damage = damage;
        proj.serverTarget = target;

        NetworkServer.Spawn(go);
    }
}
```

---

### Step 3：写 Projectile.cs（1小时）

**职责**：自己追踪、自己判命中、服务器侧自己扣血、客户端侧自己假命中。

⚠️ NetworkBehaviour（方案D）。**双端跑同一份Update**，靠 isServer 分支决定权威动作。

`Assets/Scripts/Gameplay/Projectile/Projectile.cs`：

```csharp
using Mirror;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    [Header("Sync State")]
    [SyncVar] public uint targetNetId;
    [SyncVar] public Vector3 startPos;
    [SyncVar] public float speed = 20f;
    [SyncVar(hook = nameof(OnHitChanged))] public bool hasHitVisually;

    [Header("References")]
    public MeshRenderer meshRenderer;
    public TrailRenderer trail;
    public GameObject hitEffectPrefab;

    [Header("Safety")]
    public float maxLifetime = 5f;
    public float maxRange = 50f;

    // 服务器侧才用（不同步）
    [HideInInspector] public int damage;
    [HideInInspector] public Enemy serverTarget;

    private float lifetime;
    private bool hasHitLocally;

    public override void OnStartClient()
    {
        if (!isServer) {
            transform.position = startPos;
        }
    }

    void Update()
    {
        // 三层保险（双端都跑）
        lifetime += Time.deltaTime;
        if (lifetime > maxLifetime) {
            if (isServer) NetworkServer.Destroy(gameObject);
            return;
        }
        if (Vector3.Distance(transform.position, startPos) > maxRange) {
            if (isServer) NetworkServer.Destroy(gameObject);
            return;
        }

        if (hasHitLocally) return;

        // 找目标位置（服务器用引用，客户端查字典）
        Vector3 targetPos;
        if (isServer) {
            if (serverTarget == null || serverTarget.hp <= 0) {
                NetworkServer.Destroy(gameObject);
                return;
            }
            targetPos = serverTarget.transform.position;
        } else {
            if (!NetworkClient.spawned.TryGetValue(targetNetId, out var targetIdentity)) {
                HitVisually();
                return;
            }
            targetPos = targetIdentity.transform.position;
        }

        // 追踪（双端用同公式）
        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, speed * Time.deltaTime);

        // 到达
        if (Vector3.Distance(transform.position, targetPos) < 0.3f) {
            if (isServer) {
                serverTarget.TakeDamage(damage);
                hasHitVisually = true;  // SyncVar兜底通知所有客户端
                NetworkServer.Destroy(gameObject);
            } else {
                HitVisually();
            }
        }
    }

    void HitVisually()
    {
        if (hasHitLocally) return;
        hasHitLocally = true;

        if (meshRenderer != null) meshRenderer.enabled = false;
        if (trail != null) trail.emitting = false;
        if (hitEffectPrefab != null)
            Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
    }

    void OnHitChanged(bool oldVal, bool newVal)
    {
        if (newVal && !hasHitLocally) HitVisually();
    }
}
```

---

### Step 4：创建 Projectile Prefab（20分钟）

- Hierarchy → 3D Object → Sphere，命名 `Projectile_Bullet`
- Scale (0.3, 0.3, 0.3)，让子弹小一点
- 加组件：
  - `NetworkIdentity`（**必须**，方案D靠 Mirror 同步生成/销毁/SyncVar）
  - `Projectile.cs`
  - 可选：`TrailRenderer` 加个拖尾
- Inspector 配置 Projectile.cs 的引用：
  - meshRenderer：拖自身的 MeshRenderer
  - trail：拖自身的 TrailRenderer（如有）
  - hitEffectPrefab：暂留空（W2 视觉打磨期再加）
- 拖到 `Assets/Prefabs/`
- 删除 Hierarchy 里的实例
- **注册到 NetworkManager 的 Spawnable Prefabs**（方案D 必须）

---

### Step 5：场景配置（20分钟）

#### 5.1 拖塔到场景
- 把 `Tower_Cannon` 拖到 Hierarchy
- 位置：路径中间，比如 (0, 0.5, 2)（在敌人路径侧边一点，让 SphereCollider 能覆盖路径）
- 让塔的位置使其 attackRange=5 能覆盖到敌人路径

#### 5.2 配置塔的字段
- Inspector：
  - FirePoint：拖入塔身上的 FirePoint 子物体
  - projectilePrefab：拖入 Projectile_Bullet Prefab
  - 其他字段保持默认

#### 5.3 验证场景
- 选 Tower → Scene视图能看到 SphereCollider 的绿色圈（5米范围）
- 圈应该覆盖敌人会经过的路径

---

### Step 6：测试（30分钟）

- 主 Editor Play → Start Server
- 副 Editor Play → Start Client
- 看到敌人沿路径走
- 敌人进入塔范围 → 塔每秒开火
- 子弹飞向敌人 → 命中后客户端隐藏 Mesh、播命中
- 敌人扣血到 0 → 销毁，两端同步

---

### Day 3 验收清单

- ☐ Tower_Cannon Prefab 配置正确（NetworkIdentity / SphereCollider isTrigger / Tower.cs）
- ☐ Projectile_Bullet Prefab 配置正确（NetworkIdentity / Projectile.cs / 可选 TrailRenderer）
- ☐ Projectile_Bullet **已注册** 到 NetworkManager 的 spawnPrefabs 列表
- ☐ 场景里手动放了一座塔，attackRange 覆盖敌人路径
- ☐ Tower 字段：FirePoint / projectilePrefab 都拖好
- ☐ Play 后塔每秒开一炮（Inspector 看 attackTimer 在变化）
- ☐ 子弹双端同步出现（Mirror Spawn 广播）
- ☐ 子弹追踪敌人，飞行流畅（双端各自 MoveTowards）
- ☐ 命中后敌人扣血（Inspector 看 hp 变化）
- ☐ 命中后客户端的子弹立即"假命中"（隐藏 Mesh / 停拖尾）
- ☐ hp=0 时敌人销毁，两端同步消失
- ☐ Console 看到 `[Server] Enemy died` 日志

---

### Day 3 必踩的坑

#### 坑1：OnTriggerEnter 不触发
**检查**：
- Tower 有 SphereCollider 且 **isTrigger 勾选**
- Enemy 有 Rigidbody（Kinematic 即可）
- Layer Collision Matrix 允许 Tower×Enemy

#### 坑2：客户端的 OnTriggerEnter 也触发了
**症状**：客户端 enemiesInRange 也被填充
**原因**：OnTrigger 是 Unity 生命周期，双端都触发
**解决**：方法第一行 `if (!isServer) return;`（已在代码里）

#### 坑3：子弹没出现
- Tower 的 projectilePrefab 字段是空的 → 拖入 Projectile_Bullet Prefab
- Projectile_Bullet **缺少** NetworkIdentity → 加上（方案D 必须）

#### 坑4：客户端看不到子弹
- Projectile_Bullet 没注册到 spawnPrefabs → 注册（方案D 必须）
- 检查 Mirror 控制台是否有 SpawnPrefab 警告

#### 坑5：currentTarget 死了但塔还在试图攻击
**已处理**：代码里 `currentTarget == null || currentTarget.hp <= 0 || !enemiesInRange.Contains(currentTarget)` 检查

#### 坑6：延迟伤害不准
- 用 `NetworkTime.time` 不是 `Time.time`
- `NetworkTime` 双端同步，`Time.time` 是本机的

#### 坑7：子弹追不上快速移动的敌人
**症状**：子弹在敌人后面追，永远到不了 0.3 距离
**原因**：子弹速度 < 敌人速度 + 0.3
**解决**：projectileSpeed 调到 20+（敌人 baseSpeed 才 3），不会出现

#### 坑8：客户端假命中后子弹停在原地不消失
**原因**：客户端 `HitVisually()` 只是隐藏视觉，等服务器 `NetworkServer.Destroy` 才真正销毁
**正常**：这是方案D 的设计——销毁权威性靠服务器，客户端只是"看起来打到了"

---

## Day 4 — 玩家建塔 + 共享金币（约5小时）

### 目标

- 按 B 进入建造模式，所有 Buildable Slot 标绿
- 屏幕底部出现塔列表（金币不足的塔置灰）
- 选塔 + 点击 Slot → 建造 → 扣金币 → 双端同步
- 怪死 → 双端金币 +10
- 服务端权威，失败客户端不预测，控制台日志反馈

### 核心架构

```
[场景静态]
  ~30 个 BuildSlot Cube（手摆，沿路径外两排）
    - 挂 NetworkIdentity（场景启动时 Mirror 自动 Spawn）
    - 挂 BuildSlot.cs（SyncVar 记录占用状态）

[GF Component, 双端都挂]
  BuildSlotComponent
    - 懒加载缓存所有 BuildSlot
    - 标绿/悬停反馈（仅客户端调用）
    - 服务端验证位置合法（双端调用）

[场景单例, NetworkBehaviour]
  EconomyManager
    - SyncVar sharedGold = 100
    - [Server] TrySpend / Earn

[每个玩家加入时 Mirror Spawn]
  GamePlayer Prefab (NetworkIdentity)
    - GamePlayer.cs：playerId, CmdBuildTower
    - BuildComponent：本地建造逻辑（按键 + 射线 + UI 联动）
      ⚠️ 有此组件 = 能建造（未来"观战玩家"不挂这个组件即可）
```

### 数值（暂定）

- 初始共享金币：100
- 建 Cannon 塔：50
- 杀 1 个怪：+10

---

### Step 1：BuildSlot Prefab + 场景手摆（30分钟）

#### 1.1 创建 BuildSlot Prefab

- Hierarchy → 3D Object → Cube，命名 `BuildSlot`
- Scale (1, 0.1, 1)（薄薄一片，贴地）
- 加组件：
  - `NetworkIdentity`（**Server Only ❌、Scene Object ✅** —— Mirror 启动时自动 Spawn）
  - `BoxCollider`（保持默认，**不要 isTrigger**，用于鼠标射线检测）
  - `BuildSlot.cs`（下面给代码）
  - `MeshRenderer`（默认）
- 创建半透明材质：Albedo (0,1,0,0.3)，Rendering Mode = Transparent，命名 `Mat_BuildSlot_Default`
- 拖到 `Assets/Prefabs/`

#### 1.2 写 BuildSlot.cs

`Assets/Scripts/Gameplay/Build/BuildSlot.cs`：

```csharp
using Mirror;
using UnityEngine;

public class BuildSlot : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnOccupiedChanged))]
    public uint occupiedByTowerNetId;  // 0 = 空，非0 = 被这个塔占用

    [Header("Visual")]
    public MeshRenderer meshRenderer;
    public Material matDefault;       // 透明（非建造模式）
    public Material matBuildable;     // 绿色（建造模式 + 未占用）
    public Material matHover;         // 深绿（鼠标悬停）
    public Material matOccupied;      // 灰色（已被占用）

    public bool IsOccupied => occupiedByTowerNetId != 0;

    void Start()
    {
        // 默认隐藏（仅在建造模式下显示）
        meshRenderer.enabled = false;
    }

    void OnOccupiedChanged(uint oldVal, uint newVal)
    {
        // 占用状态变化时刷新视觉（如果当前在建造模式下）
        // 由 BuildSlotComponent 统一管理刷新
    }

    public void SetVisualState(BuildSlotVisualState state)
    {
        switch (state)
        {
            case BuildSlotVisualState.Hidden:
                meshRenderer.enabled = false;
                break;
            case BuildSlotVisualState.Buildable:
                meshRenderer.enabled = true;
                meshRenderer.material = IsOccupied ? matOccupied : matBuildable;
                break;
            case BuildSlotVisualState.Hover:
                meshRenderer.enabled = true;
                meshRenderer.material = IsOccupied ? matOccupied : matHover;
                break;
        }
    }
}

public enum BuildSlotVisualState { Hidden, Buildable, Hover }
```

#### 1.3 场景手摆 BuildSlot

- 沿路径外两排手摆 ~30 个 BuildSlot 实例
- 路径在 Z=0 的直线上（SpawnPoint(-12,0,0) → Base(12,0,0)）
- 推荐位置：
  - 上排 Z=2：X 从 -10 到 10 每 2 单位一个（共 11 个）
  - 下排 Z=-2：X 从 -10 到 10 每 2 单位一个（共 11 个）
  - 远端补几个 Z=±4：X 从 -8 到 8 每 4 单位一个（共 ~10 个）
- **每个 BuildSlot Y=0.05**（贴地）

#### 1.4 注册到 NetworkManager

- BuildSlot Prefab 拖到 NetworkManager 的 `Spawnable Prefabs` 列表（虽然是场景物体，注册了无害）

---

### Step 2：BuildSlotComponent (GF Component)（45分钟）

#### 2.1 写 BuildSlotComponent.cs

`Assets/Scripts/Gameplay/Build/BuildSlotComponent.cs`：

```csharp
using GameFramework;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

public class BuildSlotComponent : GameFrameworkComponent
{
    private List<BuildSlot> cachedSlots;
    private BuildSlot currentHover;

    public IReadOnlyList<BuildSlot> GetAllSlots(bool forceRefresh = false)
    {
        if (cachedSlots == null || forceRefresh)
        {
            cachedSlots = new List<BuildSlot>(FindObjectsOfType<BuildSlot>());
            Log.Info($"[BuildSlot] Scanned {cachedSlots.Count} slots");
        }
        return cachedSlots;
    }

    public BuildSlot GetSlotAtPosition(Vector3 worldPos, float tolerance = 0.5f)
    {
        var sqrTol = tolerance * tolerance;
        foreach (var slot in GetAllSlots())
            if (Vector3.SqrMagnitude(slot.transform.position - worldPos) < sqrTol)
                return slot;
        return null;
    }

    // ===== 仅客户端调用 =====

    public void HighlightAllBuildable()
    {
        foreach (var slot in GetAllSlots())
            slot.SetVisualState(BuildSlotVisualState.Buildable);
    }

    public void ClearHighlight()
    {
        foreach (var slot in GetAllSlots())
            slot.SetVisualState(BuildSlotVisualState.Hidden);
        currentHover = null;
    }

    public void SetHoverHighlight(BuildSlot newHover)
    {
        if (currentHover == newHover) return;

        // 上一帧悬停的 Slot 恢复
        if (currentHover != null)
            currentHover.SetVisualState(BuildSlotVisualState.Buildable);

        currentHover = newHover;

        // 新悬停 Slot 加深
        if (currentHover != null)
            currentHover.SetVisualState(BuildSlotVisualState.Hover);
    }
}
```

#### 2.2 在 GameEntry 注册

- GameEntry 物体加 `BuildSlotComponent` 组件（如果 GF 项目结构需要新建子物体，按 GF 文档来）
- 双端都挂（场景里就一份，自动双端有效）

---

### Step 3：EconomyManager（30分钟）

#### 3.1 写 EconomyManager.cs

`Assets/Scripts/Gameplay/Economy/EconomyManager.cs`：

```csharp
using Mirror;
using UnityEngine;
using UnityEngine.Events;

public class EconomyManager : NetworkBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [SyncVar(hook = nameof(OnGoldChanged))]
    public int sharedGold = 100;

    public UnityEvent<int> onGoldChanged = new();

    void Awake()
    {
        Instance = this;
    }

    [Server]
    public bool TrySpend(int amount)
    {
        if (sharedGold < amount) return false;
        sharedGold -= amount;
        return true;
    }

    [Server]
    public void Earn(int amount)
    {
        sharedGold += amount;
    }

    void OnGoldChanged(int oldVal, int newVal)
    {
        onGoldChanged?.Invoke(newVal);
    }
}
```

#### 3.2 部署

- [Network] 物体（或单独的 GameManager 物体）加 `EconomyManager` 组件
- 该物体加 `NetworkIdentity`（必须，因为 EconomyManager 是 NetworkBehaviour）
- **场景物体**勾上 NetworkIdentity 的 Scene Object

---

### Step 4：Enemy 死亡给金币（10分钟）

修改 `Enemy.cs` 的 `TakeDamage`：

```csharp
[Server]
public void TakeDamage(int dmg)
{
    hp -= dmg;
    if (hp <= 0)
    {
        Debug.Log($"[Server] Enemy {netId} died");
        if (EconomyManager.Instance != null)
            EconomyManager.Instance.Earn(10);
        NetworkServer.Destroy(gameObject);
    }
}
```

---

### Step 5：塔列表 UI（45分钟）

#### 5.1 UI 层级

```
Canvas (UGUI, Screen Space - Overlay)
  └── BuildBar (HorizontalLayoutGroup, 屏幕底部居中)
        └── TowerCard_Cannon (Button + Text)
              ├── Icon (Image)
              ├── NameText "Cannon"
              └── CostText "50"
```

#### 5.2 写 TowerCardUI.cs

`Assets/Scripts/UI/TowerCardUI.cs`：

```csharp
using UnityEngine;
using UnityEngine.UI;

public class TowerCardUI : MonoBehaviour
{
    [Header("Tower Info")]
    public int towerType = 0;   // 0 = Cannon
    public int cost = 50;

    [Header("UI Refs")]
    public Button button;
    public Image highlightFrame;  // 选中时显示的高亮框

    void Start()
    {
        button.onClick.AddListener(OnClick);
        if (EconomyManager.Instance != null)
            EconomyManager.Instance.onGoldChanged.AddListener(RefreshInteractable);
        RefreshInteractable(EconomyManager.Instance != null ? EconomyManager.Instance.sharedGold : 0);
        SetSelected(false);
    }

    void RefreshInteractable(int currentGold)
    {
        button.interactable = currentGold >= cost;
    }

    void OnClick()
    {
        // 通知 BuildComponent 选中此塔
        var localPlayer = NetworkClient.localPlayer;
        if (localPlayer == null) return;
        var build = localPlayer.GetComponent<BuildComponent>();
        if (build != null) build.SelectTower(towerType, this);
    }

    public void SetSelected(bool selected)
    {
        if (highlightFrame != null)
            highlightFrame.enabled = selected;
    }
}
```

#### 5.3 写 BuildBarUI.cs（管理整个 UI 栏的显示/隐藏）

```csharp
using UnityEngine;

public class BuildBarUI : MonoBehaviour
{
    public GameObject barRoot;

    void Start()
    {
        barRoot.SetActive(false);  // 默认隐藏
    }

    public void Show() => barRoot.SetActive(true);
    public void Hide() => barRoot.SetActive(false);
}
```

---

### Step 6：BuildComponent + GamePlayer（1.5小时）

#### 6.1 创建 GamePlayer Prefab

- Hierarchy → Create Empty，命名 `GamePlayer`
- 加组件：
  - `NetworkIdentity`
  - `GamePlayer.cs`
  - `BuildComponent.cs`
- 拖到 `Assets/Prefabs/`
- 删除 Hierarchy 实例
- **NetworkManager 的 `playerPrefab` 字段拖入此 Prefab**

#### 6.2 写 GamePlayer.cs

`Assets/Scripts/Gameplay/Player/GamePlayer.cs`：

```csharp
using Mirror;
using UnityEngine;

public class GamePlayer : NetworkBehaviour
{
    [SyncVar] public int playerId;
    [SyncVar] public string playerName = "Player";

    public GameObject cannonTowerPrefab;  // 服务端 Spawn 用，Inspector 拖入

    public override void OnStartServer()
    {
        playerId = (int)netId;
    }

    [Command]
    public void CmdBuildTower(Vector3 worldPos, int towerType)
    {
        var slotComp = GameEntry.GetComponent<BuildSlotComponent>();
        if (slotComp == null) { TargetBuildFailed(connectionToClient, "No BuildSlotComponent"); return; }

        // 1. 找对应 Slot
        var slot = slotComp.GetSlotAtPosition(worldPos);
        if (slot == null) { TargetBuildFailed(connectionToClient, "Invalid position"); return; }

        // 2. 检查占用
        if (slot.IsOccupied) { TargetBuildFailed(connectionToClient, "Slot occupied"); return; }

        // 3. 检查金币
        int cost = GetTowerCost(towerType);
        if (!EconomyManager.Instance.TrySpend(cost))
        { TargetBuildFailed(connectionToClient, "Not enough gold"); return; }

        // 4. Spawn 塔
        var prefab = GetTowerPrefab(towerType);
        var towerGo = Instantiate(prefab, slot.transform.position + Vector3.up * 0.5f, Quaternion.identity);
        NetworkServer.Spawn(towerGo);

        // 5. 标记 Slot 占用
        slot.occupiedByTowerNetId = towerGo.GetComponent<NetworkIdentity>().netId;

        Debug.Log($"[Server] Player {playerId} built tower {towerType} at {worldPos}");
    }

    [TargetRpc]
    void TargetBuildFailed(NetworkConnectionToClient _, string reason)
    {
        Debug.LogWarning($"[Client] Build failed: {reason}");
        // Day 4 不做 Toast，仅 Console 日志
        // 客户端清掉选中状态
        var build = GetComponent<BuildComponent>();
        if (build != null) build.OnServerRejected();
    }

    int GetTowerCost(int towerType) => towerType == 0 ? 50 : 0;
    GameObject GetTowerPrefab(int towerType) => towerType == 0 ? cannonTowerPrefab : null;
}
```

#### 6.3 写 BuildComponent.cs

`Assets/Scripts/Gameplay/Player/BuildComponent.cs`：

```csharp
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildComponent : NetworkBehaviour
{
    private bool isInBuildMode;
    private int selectedTowerType = -1;
    private TowerCardUI selectedCard;
    private BuildBarUI buildBar;
    private BuildSlotComponent slotComp;
    private Camera mainCam;

    public override void OnStartLocalPlayer()
    {
        buildBar = FindObjectOfType<BuildBarUI>();
        slotComp = GameEntry.GetComponent<BuildSlotComponent>();
        mainCam = Camera.main;
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        // 切换建造模式
        if (Input.GetKeyDown(KeyCode.B)) ToggleBuildMode();
        if (isInBuildMode && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
            ExitBuildMode();

        if (!isInBuildMode) return;

        // 鼠标射线 + 悬停反馈
        UpdateHover();

        // 左键点击建造
        if (Input.GetMouseButtonDown(0) && selectedTowerType >= 0
            && !EventSystem.current.IsPointerOverGameObject())
        {
            TryBuildAtMouse();
        }
    }

    void ToggleBuildMode()
    {
        if (isInBuildMode) ExitBuildMode();
        else EnterBuildMode();
    }

    void EnterBuildMode()
    {
        isInBuildMode = true;
        slotComp.HighlightAllBuildable();
        if (buildBar != null) buildBar.Show();
    }

    void ExitBuildMode()
    {
        isInBuildMode = false;
        slotComp.ClearHighlight();
        if (buildBar != null) buildBar.Hide();
        DeselectTower();
    }

    public void SelectTower(int towerType, TowerCardUI card)
    {
        // 同一个塔再点 = 取消
        if (selectedTowerType == towerType)
        {
            DeselectTower();
            return;
        }

        if (selectedCard != null) selectedCard.SetSelected(false);
        selectedTowerType = towerType;
        selectedCard = card;
        if (selectedCard != null) selectedCard.SetSelected(true);
    }

    void DeselectTower()
    {
        if (selectedCard != null) selectedCard.SetSelected(false);
        selectedTowerType = -1;
        selectedCard = null;
    }

    void UpdateHover()
    {
        var ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 100f))
        {
            var slot = hit.collider.GetComponent<BuildSlot>();
            slotComp.SetHoverHighlight(slot);
        }
        else
        {
            slotComp.SetHoverHighlight(null);
        }
    }

    void TryBuildAtMouse()
    {
        var ray = mainCam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 100f)) return;

        var slot = hit.collider.GetComponent<BuildSlot>();
        if (slot == null || slot.IsOccupied) return;

        var player = GetComponent<GamePlayer>();
        player.CmdBuildTower(slot.transform.position, selectedTowerType);
    }

    public void OnServerRejected()
    {
        // 服务端拒绝，客户端清选中（Day 4 简化处理）
        DeselectTower();
    }
}
```

---

### Step 7：场景配置 + UI 搭建（30分钟）

1. 场景里 [Network] 物体加 `EconomyManager`（带 NetworkIdentity）
2. GameEntry 物体加 `BuildSlotComponent`
3. Canvas → BuildBar → TowerCard_Cannon（按 Step 5.1 层级搭好）
4. NetworkManager 的 playerPrefab = GamePlayer Prefab
5. GamePlayer Prefab 的 cannonTowerPrefab = Day 3 的 Tower_Cannon Prefab
6. **Tower_Cannon Prefab 已在 spawnPrefabs 列表（Day 3 已注册）**

---

### Step 8：测试（30分钟）

- 主 Editor Play → Start Server
- 副 Editor Play → Start Client（Mirror 自动 Spawn 各自的 GamePlayer）
- 双端按 B 进入建造模式 → 看到所有 BuildSlot 标绿
- 点击底部 Cannon 卡片（金币 100 ≥ 50，可点）→ 卡片高亮
- 鼠标移到某 Slot → 该 Slot 加深
- 左键 → 服务端验证通过 → 双端看到塔出现，金币 -50
- 怪被这座塔打死 → 双端金币 +10（UI 卡片重新可点）
- 客户端 A 和 B 同时点同一个 Slot：
  - 服务端先到先得，第二个收到 TargetRpc 失败 → Console 输出 "Build failed: Slot occupied"

---

### Day 4 验收清单

- ☐ ~30 个 BuildSlot 摆在场景里（路径外两排）
- ☐ BuildSlot Prefab 配置正确（NetworkIdentity 场景物体 / BoxCollider 非 Trigger / BuildSlot.cs / 4 种 Material）
- ☐ BuildSlotComponent 挂在 GameEntry，双端启动后 Console 打印 "Scanned N slots"
- ☐ EconomyManager 挂在 [Network] 物体，初始 sharedGold = 100
- ☐ GamePlayer Prefab 配置正确（NetworkIdentity / GamePlayer.cs / BuildComponent.cs / cannonTowerPrefab 拖入）
- ☐ NetworkManager.playerPrefab = GamePlayer Prefab
- ☐ Canvas + BuildBar + TowerCard_Cannon UI 搭建完成
- ☐ Play 后双端各自有 GamePlayer Spawn
- ☐ 按 B 进入建造模式：所有 Slot 标绿 + UI 栏显示
- ☐ 点 Cannon 卡片：卡片高亮，selectedTowerType = 0
- ☐ 鼠标悬停 Slot：该 Slot 加深
- ☐ 点击 Slot：双端看到塔生成，金币 -50
- ☐ 杀怪：双端金币 +10
- ☐ 金币 < 50 时 Cannon 卡片自动置灰，无法点击
- ☐ 已建塔的 Slot 显示灰色，无法再次建造
- ☐ ESC / 右键 / 再按 B 退出建造模式

---

### Day 4 必踩的坑

#### 坑1：BuildSlot 场景物体客户端不同步
**原因**：场景物体的 NetworkIdentity 必须 Mirror 启动时 Spawn
**解决**：检查 BuildSlot 的 NetworkIdentity 是 Scene Object（不是 Server Only），且 NetworkManager 启动时调用了 `NetworkServer.SpawnObjects()`（Mirror 默认会调）

#### 坑2：FindObjectsOfType 找不到 BuildSlot
**原因**：BuildSlotComponent 在 Awake 调用，Mirror 还没 Spawn 场景物体
**解决**：用懒加载（已在代码中），第一次调用 GetAllSlots 时 Find，此时 Mirror 已 Spawn 完

#### 坑3：CmdBuildTower 报 "No NetworkIdentity"
**原因**：BuildComponent 调用 CmdBuildTower 但 GamePlayer 不是 isLocalPlayer
**解决**：BuildComponent.Update 里第一行 `if (!isLocalPlayer) return;`（已在代码里）

#### 坑4：UI 点击穿透到地面
**症状**：点 UI 按钮也触发了地面 Raycast
**解决**：`!EventSystem.current.IsPointerOverGameObject()` 检查（已在代码里）

#### 坑5：金币 SyncVar 客户端 UI 不刷新
**原因**：UnityEvent.AddListener 在 EconomyManager 之前调用
**解决**：TowerCardUI.Start 里检查 Instance 非空再 AddListener，或改用 `OnStartClient` 时机

#### 坑6：服务端 GetSlotAtPosition 找不到 Slot
**原因**：浮点误差，Slot 实际位置和客户端发来的 worldPos 差几毫
**解决**：用 sqrTolerance（已在代码中），tolerance 0.5 足够

#### 坑7：占用 Slot 后 Slot 视觉没变
**原因**：SyncVar hook 没刷新视觉
**解决**：BuildSlot 的 OnOccupiedChanged hook 里调 `SetVisualState(当前模式)` ——但如何知道当前是建造模式？让 BuildSlotComponent 持有"当前是否在建造模式"标志，hook 通知 Component 重新刷新自己。**Day 4 简化**：暂不处理"建造时其他玩家也建塔"的视觉同步，反正自己点该 Slot 会被服务端拒绝。

#### 坑8：双端各自 GamePlayer Spawn 后没有 BuildSlot 引用
**原因**：BuildComponent.OnStartLocalPlayer 跑得太早，BuildSlotComponent 还没初始化
**解决**：OnStartLocalPlayer 里只缓存 GameEntry.GetComponent，第一次访问 slotComp.GetAllSlots 时再 Find

---

## Day 3-4 整体不要做的事

写在便利贴贴显示器上，每次想加东西时看一眼：

- ❌ UI（金币、波次显示，Day 5+）
- ❌ 多种敌人/塔（W2）
- ❌ Buff 系统（W2）
- ❌ Mutation 投票（W3）
- ❌ 死亡特效/音效（W2-W3 视觉打磨期）
- ❌ 对象池（Day 5 接入，Day 3 写 Factory 封装伏笔）
- ❌ 关卡选择/主菜单（W4）
- ❌ XLua / 资源热更（W4）
- ❌ asmdef 分离（不做）

---

## 卡住时的求救清单

按这个顺序问 AI：
1. **报错了**：粘贴报错+操作步骤
2. **客户端不同步**：检查清单（spawnPrefabs / NetworkIdentity / [Server]标记 / Layer / Rigidbody）
3. **想不通的设计**：直接问"X应该放Server还是Client"
4. **卡半天以上**：考虑降级（去掉这功能 / 用Host代替DS / 简化逻辑）

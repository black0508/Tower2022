# W1 Day 2-3 详细开发文档

> 目标：Day 2-3结束时，**双Editor能看到敌人沿路径走、塔自动开火、敌人扣血死亡**
> 工期：约4-6小时（Day 2约2小时，Day 3约3小时）
> 范围：**只做核心战斗循环**，不做UI、不做玩家建塔、不做金币

---

## 总览

```
Day 2: 敌人沿路径走 + Spawner定时生成
Day 3: 塔检测敌人 + 开火扣血（双端模拟视觉子弹 + 服务器权威伤害）
```

**Day 2-3 完成后能看到**：
- 主Editor跑Server，副Editor跑Client
- 敌人源源不断从SpawnPoint走向Base
- 路径上手动放的塔自动开火
- 敌人扣血、死亡、销毁，两端同步

---

## 核心概念速览

### NavMesh 原理（30秒理解）

NavMesh = "导航网格"。Unity 烘焙时把可走区域切成无数三角形。运行时 NavMeshAgent 用 A* 在三角形之间找路径。

- 烘焙是离线的（Editor里点一下，结果存进场景）
- 运行时不重新烘焙
- 障碍物烘焙时就决定，运行时移动 Cube 不影响

### 子弹同步策略（Day 3 核心）

**采用方案D：网络对象 + 视觉本地预测**（用户最终设计）

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

---

## Day 2 — 敌人沿路径走（约2小时）

### Step 1：搭场景（30分钟）

#### 1.1 创建地面
- Hierarchy → 3D Object → Plane
- 命名 `Ground`，Position 归零，Scale (3, 1, 3)（30×30）
- Inspector 右上角 **Static** 勾选 → 选择 `Navigation Static`

#### 1.2 创建SpawnPoint和Base
- 起点：Cube 命名 `SpawnPoint`，位置 (-12, 0.5, 0)，颜色绿色（建材质或调Albedo）
- 终点：Cube 命名 `Base`，位置 (12, 0.5, 0)，颜色红色
- **给Base加Tag**："Base"（Inspector → Tag 下拉 → Add Tag → 加 Base）

#### 1.3 调整摄像机
- Main Camera Position: (0, 25, -10)
- Rotation: (60, 0, 0)
- 俯视看整张地图

---

### Step 2：烘焙 NavMesh（10分钟）

- Window → AI → **Navigation (Obsolete)**（Unity 6 里这个旧版最简单）
- 切到 Bake 标签
- 默认参数：Agent Radius 0.5, Agent Height 2
- 点 **Bake**
- **验收**：Scene 视图能看到地面被蓝色覆盖，从 SpawnPoint 到 Base 蓝色连通

⚠️ 如果不连通，检查：
- Ground 是否勾了 Navigation Static
- 障碍物是否挡得太死

---

### Step 3：创建 Enemy Prefab（30分钟）

#### 3.1 创建 GameObject
- Hierarchy → 3D Object → Capsule，命名 `Enemy_Slime`
- 加组件：
  - `NetworkIdentity`
  - `NavMeshAgent`
  - `Rigidbody`（**勾 Is Kinematic**，为后面塔的Trigger准备）
  - `Capsule Collider`（保持默认，**不要 isTrigger**）
  - `NetworkTransformReliable`（Mirror自带，让客户端看到位置同步）
  - `Enemy.cs`（自己写，下面给代码）

#### 3.2 写 Enemy.cs

`Assets/Scripts/Gameplay/Enemy/Enemy.cs`：

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

    private NavMeshAgent agent;
    private Transform baseTarget;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public override void OnStartServer()
    {
        // 服务器：找Base设目标
        var baseGo = GameObject.FindWithTag("Base");
        if (baseGo != null)
        {
            baseTarget = baseGo.transform;
            agent.destination = baseTarget.position;
            agent.speed = baseSpeed;
        }
        else
        {
            Debug.LogError("[Server] Base not found! Tag the base object as 'Base'.");
        }
    }

    public override void OnStartClient()
    {
        // 客户端：禁用NavMeshAgent，靠NetworkTransform同步位置
        if (!isServer)
        {
            agent.enabled = false;
        }
    }

    void Update()
    {
        if (!isServer) return;

        // 走到基地
        if (baseTarget != null && Vector3.Distance(transform.position, baseTarget.position) < 1f)
        {
            ReachBase();
        }
    }

    [Server]
    void ReachBase()
    {
        Debug.Log("[Server] Enemy reached base!");
        // TODO: Day 4 接上 Base.TakeDamage(1)
        NetworkServer.Destroy(gameObject);
    }

    [Server]
    public void TakeDamage(int dmg)
    {
        hp -= dmg;
        if (hp <= 0)
        {
            Debug.Log($"[Server] Enemy {netId} died");
            NetworkServer.Destroy(gameObject);
        }
    }
}
```

#### 3.3 拖成 Prefab
- 把 Hierarchy 里的 Enemy_Slime 拖到 `Assets/Prefabs/`
- **删除 Hierarchy 里的实例**（运行时Spawn）

#### 3.4 注册到 NetworkManager
- 选 [Network] 物体
- Inspector 找 `Spawnable Prefabs`
- 点 + 加一行，把 Enemy_Slime Prefab 拖进去

---

### Step 4：写 EnemySpawner（30分钟）

#### 4.1 写 EnemySpawner.cs

`Assets/Scripts/Gameplay/Spawner/EnemySpawner.cs`：

```csharp
using Mirror;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject enemyPrefab;

    [Header("Settings")]
    public float spawnInterval = 3f;
    public int spawnCount = 5;

    [Header("Runtime State (Inspector可观察)")]
    [SerializeField] private bool isSpawning;
    [SerializeField] private float spawnTimer;
    [SerializeField] private int spawnedCount;

    public void StartSpawning()
    {
        isSpawning = true;
        spawnTimer = 0;
        spawnedCount = 0;
    }

    public void StopSpawning()
    {
        isSpawning = false;
    }

    void Update()
    {
        if (!NetworkServer.active) return;
        if (!isSpawning) return;
        if (spawnedCount >= spawnCount)
        {
            isSpawning = false;
            Debug.Log("[Server] Spawn batch finished");
            return;
        }

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0;
            var go = Instantiate(enemyPrefab, transform.position, Quaternion.identity);
            NetworkServer.Spawn(go);
            spawnedCount++;
            Debug.Log($"[Server] Spawned enemy {spawnedCount}/{spawnCount}");
        }
    }
}
```

**注意**：
- 继承 `MonoBehaviour` 不是 NetworkBehaviour（SpawnPoint 不需要联网身份）
- `NetworkServer.active` 早退保证客户端不跑生成逻辑
- Update + 字段，不用协程
- Inspector 可观察 `spawnTimer / spawnedCount` 实时变化

#### 4.2 部署
- 选中场景里的 SpawnPoint Cube
- 加 `EnemySpawner` 组件
- Inspector 把 Enemy_Slime Prefab 拖到 `enemyPrefab`
- spawnInterval = 3, spawnCount = 5

#### 4.3 在 NetworkManager 启动 Spawner

写 `Assets/Scripts/Network/GameNetworkManager.cs`（如果Day 1还没写）：

```csharp
using Mirror;
using UnityEngine;

public class GameNetworkManager : NetworkManager
{
    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("[Server] Server started");

        // 启动 EnemySpawner
        var spawner = FindObjectOfType<EnemySpawner>();
        if (spawner != null)
        {
            spawner.StartSpawning();
        }
    }
}
```

把 [Network] 物体上的 `NetworkManager` 替换成 `GameNetworkManager`（移除原组件，加新的）。

---

### Day 2 验收清单

- ☐ NavMesh 烘焙成功（蓝色覆盖正确，路径连通）
- ☐ Enemy_Slime Prefab 配置完整（NetworkIdentity / NavMeshAgent / Rigidbody-Kinematic / Collider / NetworkTransform / Enemy.cs）
- ☐ Enemy_Slime 在 spawnPrefabs 列表里
- ☐ Base Cube 的 Tag 是 "Base"
- ☐ EnemySpawner 挂在 SpawnPoint 上，配置正确
- ☐ 主 Editor Play → Start Server → 每3秒生成1只敌人
- ☐ 敌人沿NavMesh从起点走到Base，到达后销毁，Console 打印 "reached base"
- ☐ 副 Editor Start Client 连入 → 看到所有敌人同步移动

---

### Day 2 必踩的坑

#### 坑1：NavMeshAgent 在客户端"乱动"
**症状**：副 Editor 的敌人位置跳动
**原因**：客户端的 NavMeshAgent 也在跑寻路
**解决**：`OnStartClient` 里 `if (!isServer) agent.enabled = false;`（已在代码里）

#### 坑2：找不到 Base
**报错**：NullReferenceException
**解决**：检查 Base Cube 的 Tag 是否设为 "Base"

#### 坑3：敌人原地不动
**原因**：SpawnPoint 不在 NavMesh 上，Spawn 出来的敌人找不到路径
**解决**：把 SpawnPoint 移到 Plane 上方一点（Y=0.5），它的"投影"落在 NavMesh 区域内即可

#### 坑4：副 Editor 看不到敌人
**原因**：Enemy_Slime 没在 spawnPrefabs 列表
**解决**：检查 GameNetworkManager 的 Spawnable Prefabs

#### 坑5：客户端敌人位置抖动
**原因**：NetworkTransform 默认插值参数对慢速移动不友好
**解决**：先不管，看着不爽再调（NetworkTransform → Send Interval / Position Sensitivity）

---

## Day 3 — 塔自动攻击（约3小时）

### Step 5：创建 Tower Prefab（30分钟）

#### 5.1 创建 GameObject
- Hierarchy → 3D Object → Cube，命名 `Tower_Cannon`
- Scale (1, 2, 1)，让塔显得高一点
- 子物体：再加一个小 Cube 命名 `FirePoint`，放在塔顶（局部位置 (0, 1.2, 0)）

#### 5.2 加组件到 Tower_Cannon
- `NetworkIdentity`
- `SphereCollider`（**isTrigger ✅**, radius = 5，作为攻击范围检测）
- `Tower.cs`（自己写，下面给代码）

⚠️ **不要加 Rigidbody**。Trigger 触发要求"双方至少一方有Rigidbody"，**Enemy 已经有 kinematic Rigidbody**，所以塔不需要。

#### 5.3 注意 Layer Collision
- Edit → Project Settings → Physics → Layer Collision Matrix
- 默认 Default × Default 是勾选的，不用改
- 如果你给Tower/Enemy设了Layer，确保它们之间能碰撞

---

### Step 6：写 Tower.cs（1小时）

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

### Step 7：写 Projectile.cs（1小时）

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

### 重构原则（用户提出）

- **子弹自治**：Tower 只 Spawn 子弹，**不管子弹怎么飞**
- **双端共享代码**：Update 里 isServer 分支隔离权威动作（扣血 vs 假命中），追踪逻辑共享
- **职责单一**：Tower 选目标+开火，Projectile 飞行+命中，Enemy 受伤+移动

---

### Step 7：写 ProjectileVisual.cs（30分钟）

`Assets/Scripts/Gameplay/Projectile/ProjectileVisual.cs`：

```csharp
using UnityEngine;

// ⚠️ 故意继承 MonoBehaviour，不是 NetworkBehaviour
// 子弹是纯客户端视觉，不参与网络同步
public class ProjectileVisual : MonoBehaviour
{
    private Transform target;
    private float speed;

    public void Init(Transform target, float speed)
    {
        this.target = target;
        this.speed = speed;
    }

    void Update()
    {
        // 目标已被销毁（其他塔打死了/到Base了）
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        // 追踪飞行
        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime
        );

        // 视觉到达
        if (Vector3.Distance(transform.position, target.position) < 0.3f)
        {
            // TODO: 这里可以播命中特效（W2再加）
            Destroy(gameObject);
        }
    }
}
```

---

### Step 8：创建 ProjectileVisual Prefab（10分钟）

- Hierarchy → 3D Object → Sphere，命名 `ProjectileVisual_Bullet`
- Scale (0.3, 0.3, 0.3)，让子弹小一点
- **不要加** NetworkIdentity（关键）
- 加组件 `ProjectileVisual`
- 拖到 `Assets/Prefabs/`
- **不要注册到 spawnPrefabs**（不是网络对象）
- 删除 Hierarchy 里的实例

---

### Step 9：场景配置（20分钟）

#### 9.1 拖塔到场景
- 把 `Tower_Cannon` 拖到 Hierarchy（不需要拖到 Prefabs，Day 3 直接放场景）
- 位置：路径中间，比如 (0, 0.5, 2)（在敌人路径侧边一点，让 SphereCollider 能覆盖路径）
- 让塔的位置使其 attackRange=5 能覆盖到敌人路径

#### 9.2 配置塔的字段
- Inspector：
  - FirePoint：拖入塔身上的 FirePoint 子物体
  - ProjectileVisualPrefab：拖入 ProjectileVisual_Bullet Prefab
  - 其他字段保持默认

#### 9.3 验证场景
- 选 Tower → Scene视图能看到 SphereCollider 的绿色圈（5米范围）
- 圈应该覆盖敌人会经过的路径

---

### Step 10：测试（30分钟）

- 主 Editor Play → Start Server
- 副 Editor Play → Start Client
- 看到敌人沿路径走
- 敌人进入塔范围 → 塔每秒开火
- 子弹飞向敌人 → 视觉到达后销毁
- 敌人扣血到 0 → 销毁，两端同步

---

### Day 3 验收清单

- ☐ Tower_Cannon Prefab 配置正确（NetworkIdentity / SphereCollider isTrigger / Tower.cs）
- ☐ ProjectileVisual_Bullet Prefab 配置正确（**无 NetworkIdentity** / ProjectileVisual.cs）
- ☐ ProjectileVisual_Bullet **未在** spawnPrefabs 列表
- ☐ 场景里手动放了一座塔，attackRange 覆盖敌人路径
- ☐ Tower 字段：FirePoint / ProjectileVisualPrefab 都拖好
- ☐ Play 后塔每秒开一炮（Inspector 看 attackTimer 在变化）
- ☐ 子弹双端同步出现（一个服务器Fire，所有客户端Instantiate视觉子弹）
- ☐ 子弹追踪敌人，飞行流畅
- ☐ 命中后敌人扣血（Inspector 看 hp 变化）
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
- Tower 的 ProjectileVisualPrefab 字段是空的 → 拖入 Prefab
- ProjectileVisual_Bullet 不小心加了 NetworkIdentity → 移除（**它是普通 GameObject**）

#### 坑4：客户端看不到子弹
- ProjectileVisual_Bullet 不小心被注册到 spawnPrefabs → 移除（**它不需要**）
- RpcFireVisual 没正确触发 → Console 看是否有 `[Mirror] RPC` 警告

#### 坑5：currentTarget 死了但塔还在试图攻击
**已处理**：代码里 `currentTarget == null || currentTarget.hp <= 0 || !enemiesInRange.Contains(currentTarget)` 检查

#### 坑6：延迟伤害不准
- 用 `NetworkTime.time` 不是 `Time.time`
- `NetworkTime` 双端同步，`Time.time` 是本机的

#### 坑7：子弹追不上快速移动的敌人
**症状**：子弹在敌人后面追，永远到不了 0.3 距离
**原因**：子弹速度 < 敌人速度 + 0.3
**解决**：projectileSpeed 调到 20+（敌人 baseSpeed 才 3），不会出现

---

## Day 2-3 整体不要做的事

写在便利贴贴显示器上，每次想加东西时看一眼：

- ❌ 玩家建塔（Day 4）
- ❌ UI（金币、波次显示，Day 5）
- ❌ 多种敌人/塔（W2）
- ❌ Buff 系统（W2）
- ❌ Mutation 投票（W3）
- ❌ 死亡特效/音效（W2-W3 视觉打磨期）
- ❌ 对象池（W2 性能优化期）
- ❌ 关卡选择/主菜单（W4）
- ❌ XLua / 资源热更（W4）
- ❌ asmdef 分离（不做）

---

## Day 2-3 完成后的状态

主Editor Play → Start Server，副Editor Play → Start Client：
- 双端看到敌人源源不断生成（5只）
- 敌人沿路径走向Base
- 路上一座塔自动开火
- 子弹追踪敌人
- 敌人扣血、死亡、销毁
- 双端表现一致

**这就是核心战斗循环**。Day 4 在此基础上加"客户端建塔"和共享金币。

---

## 卡住时的求救清单

按这个顺序问 AI：
1. **报错了**：粘贴报错+操作步骤
2. **客户端不同步**：检查清单（spawnPrefabs / NetworkIdentity / [Server]标记 / Layer / Rigidbody）
3. **想不通的设计**：直接问"X应该放Server还是Client"
4. **卡半天以上**：考虑降级（去掉这功能 / 用Host代替DS / 简化逻辑）

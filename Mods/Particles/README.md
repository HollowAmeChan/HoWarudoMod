# 粒子 Mod（Particles）

## 是什么

一个文件夹 + 一个根 Prefab。蓝图里用 `Spawn Particle` 之类的节点时，
可以从粒子来源里选到这个 Mod。

## 硬性约定

| 项 | 值 |
|---|---|
| 入口资产 | **`Particle.prefab`** —— 根 GameObject 必须叫 `Particle` |
| 落点目录 | `<Warudo 数据目录>/StreamingAssets/Particles` |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） |

根节点建议是空的，`ParticleSystem` 挂在子节点上。如果粒子要跟着角色骨骼走，
**Simulation Space 设成 Local**，否则粒子会在世界空间里掉队。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Particle.prefab` | 入口。根节点 `Particle`，挂了一个默认的 `ParticleSystem` |

这是**纯资源 Mod 的最小形态**：一个资产、零脚本。

## 构建

窗口 `HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 →
拖入本目录 → 类型自动识别成 `粒子` → **「构建 Warudo Mod」**。

## 产物该长什么样

```
modinfo.dat            必有
sharedassets.bin/.meta 必有（资源容器）
assemblymodules.dat    ❌ 不该有 —— 本目录没有 .cs
```

**没有 `assemblymodules.dat` 是正常的**，不要当成构建失败。
复核器已经按类型判定了（只在工作区有 `.cs` 时才要求这个条目）。

## 怎么确认 Warudo 认了

新建蓝图 → 从节点面板拖一个 **Spawn Particle** 节点 → 它的粒子来源 →
弹出卡片选择器 → 找 `Particles` 这张卡。

Warudo 日志**不记录**粒子包的加载，只能这样确认。

# 粒子 Mod（Particles）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个根 Prefab。蓝图里用粒子节点时，可以从粒子来源里选到这个 Mod。
（具体是哪个节点、UI 长什么样——❓ 未验证。）

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | `Particle.prefab`，根 GameObject 叫 `Particle` | ✅ 实测——工坊的 Bonk Stars Particle 包里就是 `Assets/Bonk Stars Particle/Particle.prefab`；📖 官方也这么要求 |
| 落点目录 | `<数据目录>/StreamingAssets/Particles` | ✅ 实测 |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） | ✅ 实测 |

📖 "粒子要跟角色骨骼走就把 Simulation Space 设成 Local"是官方建议，本目录**没有验证**。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Particle.prefab` | 入口。根节点 `Particle`，挂了一个默认的 `ParticleSystem` |

这是**纯资源 Mod 的最小形态**：一个资产、零脚本。

## 构建

`HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 → 拖入本目录 →
类型自动识别成 `粒子` → **「构建 Warudo Mod」**。✅ 实测

## 产物该长什么样

| 条目 | 本目录产物 |
|---|---|
| `modinfo.dat` | ✅ 有 |
| `sharedassets.bin` / `.meta` | ✅ 有 |
| `assemblymodules.dat` | ✅ **没有，而且这是对的**（本目录没有 `.cs`） |

✅ 实测：`Particles.warudo` 就是前 3 个条目。

**没有 `assemblymodules.dat` 不要当成构建失败** —— 复核器只在工作区有 `.cs` 时才要求它。

## 怎么确认 Warudo 认了

❓ **未验证。** 我推测是"新建蓝图 → 加一个粒子节点 → 它的粒子来源 → 卡片选择器里找
`Particles`"，但**没有实机点过**，连节点名都是照文档写的。

✅ 可以确定的是：**Warudo 日志不记录粒子包的加载**，日志证明不了。

**需要你实机确认入口位置。**

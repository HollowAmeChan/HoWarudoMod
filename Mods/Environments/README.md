# 环境 Mod（Environments）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个 Unity **场景**。用户新建 **Environment** 资产后，在它的来源处选到这个
Mod，Warudo 就把整个场景加载进来替换当前环境。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | `Environment.unity`，场景文件名必须是 `Environment` | ✅ 本目录的文件就叫这个名；📖 官方也这么要求 |
| 必挂组件 | 场景里某个 GameObject 上要有 `EnvironmentSettings` | 📖 官方要求；❓ **本目录用反射挂上了它，但没验证过 Warudo 是否接受这个场景** |
| 落点目录 | `<数据目录>/StreamingAssets/Environments` | ✅ 实测 |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） | ✅ 实测 |

### 📖 该类型的额外限制（全部来自官方文档，本机**未逐条实测**）

- mod 文件夹里只能有一个场景
- 场景里最多一个 Camera
- 只能带一套 lightmap + `LightingSettings`
- Lighting 的 Directional Mode 必须是 `Directional`（不能是 Non-Directional）

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Environment.unity` | 入口场景，含 `EnvironmentSettings` |
| └ `HoWarudoModTests Marker` | 一个立方体，**故意放的标记物** |
| └ `HoWarudoModTests Light` | 一盏平行光 |

**为什么要放标记物**：空场景切过去和没切一样，光看画面没法判断加载成功没有。

## 构建

`HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 → 拖入本目录 →
类型自动识别成 `环境` → **「构建 Warudo Mod」**。✅ 实测

## 产物该长什么样

| 条目 | 本目录产物 | 说明 |
|---|---|---|
| `modinfo.dat` | ✅ 有 | |
| `sceneassets.bin` / `.meta` | ✅ 有 | 环境用场景容器 |
| `sharedassets.bin` / `.meta` | ❌ 本目录没有 | ✅ 实测——场景没引用工程内资产时不生成；📖 工坊那些复杂环境两个容器都有 |
| `assemblymodules.dat` | ❌ 没有 | 本目录没有 `.cs` |

✅ 实测：`Environments.warudo` 只有 `modinfo.dat` + `sceneassets.{bin,meta}`，
而工坊的 Classroom / Edge / Ruins / VR Room 四个环境包都是
`sharedassets.* + sceneassets.*` 两套都有。**两种都合法。**

复核器允许 `sharedassets.*` / `sceneassets.*` 任选其一成对出现。

## 怎么确认 Warudo 认了

❓ **未验证。** 我推测是"新建 Environment 资产 → 在它的来源处选择 → 卡片选择器里找
`Environments`"，**没有实机点过**。

✅ 可以确定的是：**Warudo 日志只写一行** `[LocalResourceMonitor] Started monitoring
Environments (<路径>)`，**不记具体文件**，日志证明不了。

**需要你实机确认入口位置。**

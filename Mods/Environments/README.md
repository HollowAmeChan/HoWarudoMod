# 环境 Mod（Environments）

## 是什么

一个文件夹 + 一个 Unity **场景**。用户新建 **Environment** 资产后，
在它的 **Source** 里选到这个 Mod，Warudo 就把整个场景加载进来替换当前环境。

## 硬性约定

| 项 | 值 |
|---|---|
| 入口资产 | **`Environment.unity`** —— 场景文件名必须是 `Environment` |
| 必挂组件 | 场景里某个 GameObject 上要有 **`EnvironmentSettings`** |
| 落点目录 | `<Warudo 数据目录>/StreamingAssets/Environments` |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） |

`EnvironmentSettings` 来自 Warudo SDK（`Warudo.Plugins.Core`）。官方流程是从当前环境
**Copy from current environment settings** 拷一份配置过来。

### 该类型的额外限制

- mod 文件夹里**只能有一个场景**
- 场景里**最多一个 Camera**
- 只能带**一套 lightmap + `LightingSettings`**
- Lighting 的 **Directional Mode 必须是 `Directional`**（不能是 Non-Directional）

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Environment.unity` | 入口场景，含 `EnvironmentSettings` |
| └ `HoWarudoModTests Marker` | 一个立方体，**故意放的标记物** |
| └ `HoWarudoModTests Light` | 一盏平行光 |

**为什么放标记物**：空场景切过去和没切一样，根本看不出加载成功没有。

## 构建

窗口 `HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 →
拖入本目录 → 类型自动识别成 `环境` → **「构建 Warudo Mod」**。

## 产物该长什么样

```
modinfo.dat            必有
sceneassets.bin/.meta  必有 —— 环境用场景容器，不是 sharedassets
sharedassets.bin/.meta 可选 —— 场景若引用了工程内的资产才会出现
assemblymodules.dat    ❌ 不该有（本目录没有 .cs）
```

**用 `sceneassets.*` 而不是 `sharedassets.*` 是环境 Mod 的特征**。
复核器允许两者任选其一成对出现。

## 怎么确认 Warudo 认了

新建一个 **Environment** 资产 → 它的 **Source** → 卡片选择器里找 `Environments` →
选中后场景应该换成带立方体标记物的环境。

Warudo 日志只写一行 `Started monitoring Environments (<路径>)`，**不记具体文件**，
只能这样确认。

# 环境 Mod（Environments）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个 Unity **场景**。用户新建 **Environment** 资产后，在它的来源处选到这个
Mod，Warudo 就把整个场景加载进来替换当前环境。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | `Environment.unity`，场景文件名必须是 `Environment` | ✅ 本目录的文件就叫这个名；📖 官方也这么要求 |
| 必挂组件 | 场景里某个 GameObject 上要有 `EnvironmentSettings`（脚本 guid `5119c7ec4a224016a35e372e38a47e39`） | ✅ 场景能被 Warudo 选中并切换过去（选中后画面全黑，见下节）；❓ 组件字段的实际生效语义未验证 |
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
| └ `Environment Settings` | 挂 `EnvironmentSettings` 组件的空物体 |
| └ `Directional Light` | 平行光（`!u!108`，`m_Type: 1`） |
| └ `Ground` | 10×10 的扁立方体当地面 |
| └ `Marker Cube` | 一个立方体，**故意放的标记物** |

**为什么要放标记物**：空场景切过去和没切一样，光看画面没法判断加载成功没有。

## ⚠️ 全黑问题（本次修复）

❓ **现象**：第一版场景切过去**全黑**。原因排查如下。

场景原本只有 `!u!29/104/157/196`（四个渲染设置块）+ `EnvironmentSettings` 空物体，
**既没有光源、也没有任何几何体**，并且 `EnvironmentSettings` 的这三个字段全是 0：

| 字段 | 修前 | 修后 | 依据 |
|---|---|---|---|
| `skyboxMaterial` | `{fileID: 0}` | Default-Skybox | ✅ `10304` 这个内置引用是 Unity 自己在 17 个场景里写的 |
| `sunSource` | `{fileID: 0}` | 指向本场景的平行光 | ❓ 字段语义是读场景序列化推断的，有效性待实测 |
| `environmentLightingAmbientColor` | `{0,0,0,0}` | `{0.5,0.5,0.5,1}` | ❓ 同上 |

同时给场景补了 **Directional Light** 和两个立方体。立方体的内置引用
`m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000}`
与 `m_Materials: {fileID: 10303, guid: 0000000000000000f000000000000000}`
**都是从本工程里 Unity 自己生成的 `Mods/Props/Prop.prefab` 上抄下来的**，
不是我猜的值；平行光的字段布局抄自本工程 `Assets/Hollow/ho宝石shader测试/Scene.unity`。

### ❓ 仍未验证

- Warudo 是否真的把 `skyboxMaterial` 应用到运行时天空盒
- 场景里的**几何体**会不会被渲染（工坊真实环境包 38 MB ~ 557 MB，几乎必然含几何体，
  但那是推断，不是实测）
- `environmentLightingScene` / `environmentReflectionsSource` 的枚举取值含义
- 是否需要有 `SceneRoots`(`!u!1660057539`) 块 —— 本场景**没有**也能被加载，故未加

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

## 怎么确认 Warudo 认了（✅ 已实机查验）

✅ **资源 → 环境 → 环境组件 → 源** → 在来源里能看到 `Environments`。

> 这不是唯一入口，但是一个可以直接验收的入口；其余入口未逐一查验。

✅ Warudo 日志只写一行 `[LocalResourceMonitor] Started monitoring Environments (<路径>)`，
**不记具体文件**，日志证明不了。

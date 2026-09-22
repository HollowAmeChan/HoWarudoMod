# 插件 Mod（Plugins）

## 是什么

一个文件夹，里面有一批 `.cs`，其中至少一个继承 `Warudo.Core.Plugins.Plugin`
并用 `[PluginType]` 注册。它是**唯一能往蓝图节点面板里加新节点的形态**。

纯蓝图只能"用节点"，不能"造节点" —— 往面板加节点的下限就是这个 Mod。

## 硬性约定

| 项 | 值 |
|---|---|
| 入口资产 | **没有**。不靠 Prefab 命名，靠 `[PluginType]` 类 |
| 落点目录 | `<Warudo 数据目录>/StreamingAssets/Plugins` |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） |

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `HoTestPlugin.cs` | `[PluginType]` 入口，`NodeTypes` 里登记了两个节点 |
| `Nodes/HoTestGreetNode.cs` | 节点 1：四种端口齐全（DataInput / DataOutput / FlowInput / FlowOutput） |
| `Nodes/Sub/HoTestAddNode.cs` | 节点 2：**嵌套子目录** + 纯数据节点 |

`NodeTypes` 必须把本 Mod 贡献的**所有**节点类型列全；漏掉的节点即使编译进了程序集，
也不会出现在节点面板里。

## 两个必须避开的编译坑

1. **命名空间不要起成 `...Plugin`。**
   `using Warudo.Core.Plugins;` 里的 `Plugin` 基类会被自己所在的命名空间遮蔽，
   报 `CS0118: "Plugin" 是命名空间，但此处被当做类型来使用`。
   本目录用 `HoWarudoModTests.Plugins` 就是为避开它。

2. **`[DataInput]` 字段名不要撞 `Node` 基类成员。**
   例如 `Name` 会报 `CS0108: xxx.Name 隐藏继承的成员 Node.Name`，
   数据输入请取业务名（本目录用 `Who`）。

## 构建

窗口 `HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 →
拖入本目录 → 类型自动识别成 `插件` → **「构建 Warudo Mod」**。

导出前**先关掉 Warudo**，并确认 `StreamingAssets/Playground` 下没有同名脚本，否则会冲突。

## 产物该长什么样

```
modinfo.dat            必有
assemblymodules.dat    必有（这就是编译后的节点程序集）
sharedassets.*         ❌ 可以没有
sceneassets.*          ❌ 可以没有
```

**纯脚本插件包只有这两个条目是合法的** —— 工作区里没有任何 Unity 资源时，
UMod 不生成资源容器。真实的工坊插件包通常带 Readme/本地化文件，所以它们会有
`sharedassets.*`；本目录是极简形态（连 Readme 之外的资源都没有）。
复核器已按此判定：`sharedassets.*` / `sceneassets.*` / `assemblymodules.dat` 三者至少有其一。

## 怎么确认 Warudo 认了（这条最好验）

打开 `Blueprints` → 节点面板 → 搜索 `Ho Test` → 应该出现分类
**`HoWarudoModTests`** 下的两个节点：`Ho Test Greet` / `Ho Test Add`。

**不需要建资产、不需要连线。节点面板里出现了 = 插件加载成功。**

这条也是唯一能从 Warudo 日志硬确认的：

```
[PluginMonitor] Loading .warudo file: ...\Plugins\Plugins.warudo
[ModHost (UMod.ModHost)]: Load mod:  Plugins
[ModHost<Plugins>]: Loading mod assembly ' umod-compiled-... '
[PluginMonitor] Found plugin type in .warudo file: HoTestPlugin
```

日志在 `%USERPROFILE%\AppData\LocalLow\HakuyaLabs\Warudo\Logs\*.log.gz`。

## ⚠️ 运行时安全审查

UMod 在 Warudo **加载时**会检查程序集的 API 引用，命中即整个 Mod 加载失败：

```
Illegal reference to disallowed namespace: System.Reflection
Assembly '...' has failed code security verification.
Failed to load and activate mod assembly
```

**构建期不会报，只有 Warudo 启动时才知道。** 禁用清单：`System.Reflection`
（含 `GetType().Name` 这种写法）、`System.IO`、`System.Runtime.InteropServices`、
`UnityEditor`、P/Invoke。

实例：创意工坊的 `LoopToggleCamera` 就是因为用了 `System.Reflection` 被拦下来的。

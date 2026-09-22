# 道具 Mod（Props）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个根 Prefab。用户新建 **Prop** 资产后，在它的来源处选到这个 Mod，
Warudo 就把 `Prop.prefab` 实例化到场景里。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | **必须**叫 `Prop.prefab`，根 GameObject 也叫 `Prop` | ✅ **实验结论**——换成两个别的名字后 Warudo 用不了并且报错（见文末）；📖 官方也这么要求；✅ 实测工坊的 Bonk Bat 包里就是 `Assets/Bonk Bat/Prop.prefab` |
| 落点目录 | `<数据目录>/StreamingAssets/Props` | ✅ 实测 |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） | ✅ 实测 |

📖 "根节点用空物体（Create Empty Parent）、模型放子节点"是官方建议，本目录就是这么搭的，
但**没有对比过不这么做会怎样**。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Prop.prefab` | 入口。根节点 `Prop`，子节点 `Body`（0.3 的立方体） |
| `HoTestPropSpinner.cs` | 挂在 `Prop` 根上的 MonoBehaviour，让立方体自转 |
| `HoTestPropUtils.cs` | **没有任何组件引用它** |
| `Internal/HoTestPropInternal.cs` | **在子目录里，也没人引用** |

后两个是**故意**的：用来验证 **UMod 会把工作区里所有 `.cs` 都编进 Mod 程序集**，
与"是否被组件引用"无关。✅ 实测——产物里 3 个类型都在。

## ✅ 定论：入口名**必须**是 `Prop.prefab`，一个 Mod 一个道具

**背景**：前面已经证实——**代码注册**的类型（`NodeTypes` / `AssetTypes`）
一个 `.warudo` 能装任意多个、还能跨分类。那**入口资产名决定**的类型
「一类一包」到底是**因为名字被写死**，还是**因为包里只取一个**？

**做法**：把 `Prop.prefab` 删掉，换成两个都不叫 `Prop` 的 prefab
（`HoPropAlpha`：0.3 立方体、自转；`HoPropBeta`：0.6 立方体、静止）。反过来做，
即"用一个肯定能进的名字 + 一个别的名字"，是问不出结论的。

**结果**：❌ **不能正常使用，并且报错。**

**结论**：

- 入口资产名是**硬约定**，必须叫 `Prop.prefab`（根 GameObject 也叫 `Prop`）
- **一个 Mod = 一个道具**，包里多塞 prefab 没有意义

这同时把上一层的规则补全了：

| 注册方式 | 一个包能装几个 | 名字自由吗 |
|---|---|---|
| 代码注册（`NodeTypes` / `AssetTypes`） | 任意多个，可跨分类 | 名字由代码决定 |
| **入口资产名决定**（Prop / Particle / Environment / Animation） | **一个** | ❌ **名字写死** |

⚠️ 顺带一条教训：改名后 FastBuild 的自动识别会失败，虽然
**「手动指定类型…」这条逃生通道能放行构建**（只给黄色警告），
但**构建出来的包 Warudo 用不了**。所以那条警告不是噪音，要认真看。

> 📌 那条"缺约定入口名只警告不拦"的放行逻辑是**故意保留**的 ——
> 它就是用来做这类实验的；只是默认路径上仍会明确告诉你会有什么后果。

## 构建

`HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 → 拖入本目录 →
类型自动识别成 `道具` → **「构建 Warudo Mod」**。✅ 实测

## 产物该长什么样

| 条目 | 本目录产物 |
|---|---|
| `modinfo.dat` | ✅ 有 |
| `sharedassets.bin` / `.meta` | ✅ 有 |
| `assemblymodules.dat` | ✅ **有**（本目录有 `.cs`） |

✅ 实测：`Props.warudo` 就是这 4 个条目。

## 怎么确认 Warudo 认了（✅ 已实机查验）

✅ **蓝图 → `道具源` 节点** → 在它的道具来源里能看到 `Props`。

> 道具类型的入口就是「道具源」节点——**不是在资产菜单里新建 Prop**（我原先猜错了）。
> 这不是唯一入口，但是一个可以直接验收的入口；其余入口未逐一查验。

✅ Warudo 日志**不记录**道具包的加载，只写一行
`[LocalResourceMonitor] Started monitoring Props (<路径>)`，所以日志证明不了。

# 道具 Mod（Props）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个根 Prefab。用户新建 **Prop** 资产后，在它的来源处选到这个 Mod，
Warudo 就把 `Prop.prefab` 实例化到场景里。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | 历史上用 `Prop.prefab`（根 GameObject 叫 `Prop`） | 📖 官方这么要求；✅ 实测工坊的 Bonk Bat 包里就是 `Assets/Bonk Bat/Prop.prefab`。**但"名字必须叫 Prop"这一条本目录正在做实验推翻/确认，见文末** |
| 落点目录 | `<数据目录>/StreamingAssets/Props` | ✅ 实测 |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） | ✅ 实测 |

📖 "根节点用空物体（Create Empty Parent）、模型放子节点"是官方建议，本目录就是这么搭的，
但**没有对比过不这么做会怎样**。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `HoPropAlpha.prefab` | 根节点 `HoPropAlpha`，子节点 `AlphaBody`（**0.3** 立方体），**会自转** |
| `HoPropBeta.prefab` | 根节点 `HoPropBeta`，子节点 `BetaBody`（**0.6** 立方体），**不自转** |
| `HoTestPropSpinner.cs` | 挂在 Alpha 根上的 MonoBehaviour，让立方体自转 |
| `HoTestPropUtils.cs` | **没有任何组件引用它** |
| `Internal/HoTestPropInternal.cs` | **在子目录里，也没人引用** |

后两个是**故意**的：用来验证 **UMod 会把工作区里所有 `.cs` 都编进 Mod 程序集**，
与"是否被组件引用"无关。✅ 实测——产物里 3 个类型都在。

## 🔬 正在进行的实验：`Prop.prefab` 这个名字是必须的吗

**背景**：前面已经证实——**代码注册**的类型（`NodeTypes` / `AssetTypes`）
一个 `.warudo` 能装任意多个、还能跨分类；而**入口资产名决定**的类型一类一包。
问题是「一类一包」到底是**因为名字被写死**，还是**因为包里只取一个**。

**做法**：把原来的 `Prop.prefab` 删掉，换成**两个都不叫 `Prop`** 的 prefab
（`HoPropAlpha` / `HoPropBeta`）。构建后看蓝图 `道具源` 的来源列表里有几个 `Props`：

| 观察到 | 结论 |
|---|---|
| **0 个** | 名字**必须**叫 `Prop`，节名是硬约定 |
| **1 个** | 名字不必须，但**一个包只取一个** prefab |
| **2 个** | 名字不必须，且**一个包能装多个** —— 道具也该归到「代码注册」那一类 |

为了在只出现 1 个时还能分清是哪个，两个 prefab 故意做得不一样：
**Alpha 是 0.3 的立方体且自转，Beta 是 0.6 的立方体且静止**。

⚠️ 改了名之后 FastBuild 的**自动识别会失败**（它靠 `rootAssetName = "Prop"` + `t:GameObject` 找入口）。
这时「其他 Mod」页的「类型」一行会出现 **「手动选择」按钮** → 点它 → 下拉里选 **道具**，就能正常构建。

> 实验做完后如果结论是"名字必须叫 Prop"，把 `HoPropAlpha.prefab` 改名回 `Prop.prefab` 即可。

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

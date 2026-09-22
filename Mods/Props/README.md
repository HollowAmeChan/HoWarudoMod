# 道具 Mod（Props）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

📖 一个文件夹 + 一个根 Prefab。用户新建 **Prop** 资产后，在它的来源处选到这个 Mod，
Warudo 就把 `Prop.prefab` 实例化到场景里。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | `Prop.prefab`，根 GameObject 叫 `Prop` | ✅ 实测——工坊的 Bonk Bat 包里就是 `Assets/Bonk Bat/Prop.prefab`；📖 官方也这么要求 |
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

## 怎么确认 Warudo 认了

❓ **未验证。** 我推测是"新建 Prop 资产 → 在它的来源处选择 → 卡片选择器里找 `Props`"，
但**没有实机点过**——之前我在角色动画上就是这么猜错的（那里实际是带搜索框的卡片选择器）。

✅ 可以确定的是：**Warudo 日志不记录道具包的加载**，只写一行
`[LocalResourceMonitor] Started monitoring Props (<路径>)`，
所以日志证明不了"它认了 `Props.warudo`"。

**需要你实机确认入口位置。** 找到之后告诉我，我把它改成 ✅。

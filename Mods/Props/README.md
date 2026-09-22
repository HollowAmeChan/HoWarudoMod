# 道具 Mod（Props）

## 是什么

一个文件夹 + 一个根 Prefab。用户新建 **Prop** 资产后，在它的 **Source** 里选到这个 Mod，
Warudo 就把 `Prop.prefab` 实例化到场景里。

## 硬性约定

| 项 | 值 |
|---|---|
| 入口资产 | **`Prop.prefab`** —— 根 GameObject 必须叫 `Prop` |
| 落点目录 | `<Warudo 数据目录>/StreamingAssets/Props` |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） |

`Prop.prefab` 的根节点建议是空的（Create Empty Parent），把真正的模型作为子节点 ——
这样道具的 Transform 由根节点统一控制，挂到角色骨骼上时不会跟模型自身的偏移打架。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Prop.prefab` | 入口。根节点 `Prop`，子节点 `Body`（0.3 的立方体） |
| `HoTestPropSpinner.cs` | 挂在 `Prop` 根上的 MonoBehaviour，让立方体自转 |
| `HoTestPropUtils.cs` | **没有任何组件引用它** |
| `Internal/HoTestPropInternal.cs` | **在子目录里，也没人引用** |

后两个是**故意**的：用来证明 **UMod 会把工作区里所有 `.cs` 都编进 Mod 程序集**，
与"是否被组件引用"无关。

## 构建

窗口 `HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页：

1. 把本目录拖进 **「Mod 资产目录」**
2. 类型会自动识别成 `道具`
3. 点 **「构建 Warudo Mod」**

或走官方流程：`Warudo → New Mod` 建工作区 → `Warudo → Build Mod`。

## 产物该长什么样

```
modinfo.dat            必有
sharedassets.bin/.meta 必有（资源容器）
assemblymodules.dat    有，因为本目录有 .cs
```

## 怎么确认 Warudo 认了

新建一个 **Prop** 资产 → 它的 **Source** → 弹出卡片选择器 → 找 `Props` 这张卡。
选中后场景里应该出现那个自转的立方体。

Warudo 日志**不记录**道具包的加载（只写一行 `Started monitoring Props`），
所以只能这样确认。

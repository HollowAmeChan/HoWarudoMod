# 角色动画 Mod（CharacterAnimations）

## 是什么

一个文件夹 + 一个 AnimationClip。选中角色后，在**待机动画**选择器里选到这个 Mod，
角色就会播放它。

## 硬性约定

| 项 | 值 |
|---|---|
| 入口资产 | **`Animation.anim`** —— 切片文件名必须是 `Animation` |
| 落点目录 | `<Warudo 数据目录>/StreamingAssets/CharacterAnimations` |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） |

切片**只能驱动骨骼**。非骨骼动画（材质、Transform 位移之类）不适用这个类型，
要走其它方案。

## ⚠️ 实测结论：一个 Mod 只能承载一个动画

这条是**验证过**的，不是推测：

把 `Animation.anim` 复制成 `Wave.anim` 一起构建，产物里**两个切片都在**
（`sharedassets.meta` 的打包清单里两行都列着，`sharedassets.bin` 里两个名字都能扫到），
但 Warudo 的待机动画选择器里**只出现一张以 Mod 名命名的卡片，没有第二级切片列表**，
`Wave` 无论怎么搜都找不到。

结论：

- **打包端不过滤** —— 工作区里的资产全部进包，不看名字；
- **加载端按名字找入口** —— 只认 `Animation`，其余切片躺在包里没人用；
- **想要多个动画 = 建多个 Mod**，一个 Mod 一个 `Animation`。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Animation.anim` | 入口切片。带一条无害的曲线，让切片不是空的 |

只有一个切片 —— 因为上面那条结论。

## 构建

窗口 `HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 →
拖入本目录 → 类型自动识别成 `角色动画` → **「构建 Warudo Mod」**。

## 产物该长什么样

```
modinfo.dat            必有
sharedassets.bin/.meta 必有
assemblymodules.dat    ❌ 不该有（本目录没有 .cs）
```

## 怎么确认 Warudo 认了

选中一个角色 → 打开**待机动画**选择器 → 卡片网格里应该出现一张
**以 Mod 名（`CharacterAnimations`）命名的卡片**。卡片左上角是缩略图，
没设工作区的 **Mod Icon** 时显示"无预览" —— 设了就有图。

Warudo 日志**不记录**动画包的加载，只能这样确认。

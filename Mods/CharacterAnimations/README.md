# 角色动画 Mod（CharacterAnimations）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 是什么

✅ 一个文件夹 + 一个 AnimationClip。选中角色后，在**待机动画**选择器里会出现一张
**以 Mod 名命名的卡片**，选中它角色就会播放这个切片。

## 硬性约定

| 项 | 值 | 依据 |
|---|---|---|
| 入口资产 | `Animation.anim`，切片文件名必须是 `Animation` | ✅ 实测（见下） |
| 落点目录 | `<数据目录>/StreamingAssets/CharacterAnimations` | ✅ 实测 |
| 工作区 | 就是本目录（`modAssetPath` 指向这里） | ✅ 实测 |

📖 "切片只能驱动骨骼，非骨骼动画（材质/位移等）不适用这个类型"——文档所述，**本机未验证**。

## ✅ 实测结论：一个 Mod 只能承载一个动画

这条是**验证过**的，不是推测：

把 `Animation.anim` 复制成 `Wave.anim` 一起构建，产物里**两个切片都在**：

```
sharedassets.meta 打包清单：
    Assets/HoWarudoModTests/Mods/CharacterAnimations/Animation.anim
    Assets/HoWarudoModTests/Mods/CharacterAnimations/Wave.anim

sharedassets.bin 里能扫到两个名字：Animation、Wave
```

但 Warudo 的待机动画选择器里**只出现一张卡片**（标签是 Mod 名 `CharacterAnimations`），
**没有第二级切片列表**，`Wave` 无论怎么搜都找不到。

所以：

- **打包端不过滤** —— 工作区里的资产全部进包，不看名字；
- **加载端按名字找入口** —— 只认 `Animation`，其余切片躺在包里没人用；
- **想要多个动画 = 建多个 Mod**，一个 Mod 一个 `Animation`。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `Animation.anim` | 入口切片。带一条无害的曲线，让它不是空的 |

只有一个切片 —— 因为上面那条结论。

## 构建

`HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 → 拖入本目录 →
类型自动识别成 `角色动画` → **「构建 Warudo Mod」**。✅ 实测

## 产物该长什么样

| 条目 | 本目录产物 |
|---|---|
| `modinfo.dat` | ✅ 有 |
| `sharedassets.bin` / `.meta` | ✅ 有 |
| `assemblymodules.dat` | ✅ **没有，而且这是对的**（本目录没有 `.cs`） |

## 怎么确认 Warudo 认了

✅ **实测**：选中一个角色 → 打开**待机动画**选择器（带搜索框的卡片网格）→
出现一张以 Mod 名命名的卡片。

卡片左上角的缩略图来自工作区的 **Mod Icon**；❓ 没设时显示"无预览"——观察到的现象，
但**没验证过设了 Icon 就一定有图**。

**这是 5 个类别里唯一一个 UI 位置已经实测确认的。**

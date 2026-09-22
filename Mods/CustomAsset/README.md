# 资源类型 Mod（自定义资源 / 自定义追踪器）

> **本文标记**：✅ 本机实测 · 📖 官方文档（未实测）· ❓ **未验证**（我的推断，可能是错的）

## 一句话结论

✅ **Warudo 没有「资源类型」这个独立打包类型。**

一个能往 Warudo 的「添加资源」菜单里加新条目的 Mod，打包上就是一个**普普通通的插件类 Mod**
（落点 `StreamingAssets/Plugins`），只不过它的程序集里除了 `[PluginType]` 还多了一个 `[AssetType]` 类。

所以**本目录的构建方式、产物结构、落点，和 `Mods/Plugins/` 完全一样**，区别只在 `.cs` 里写了什么。

## 决定性证据（✅ 本机实测）

工坊 `3780922560` = `Plugins\VRM1SpringBoneWind.warudo`（23,676 B），就是你说的那个"VRM1.0 风"的 Mod。

剥掉 12 字节 `UMOD` 头当 zip 拆开，条目是
`modinfo.dat` + `sharedassets.bin/.meta` + `assemblymodules.dat`。

把 `assemblymodules.dat` 里的内嵌 PE（从 **offset 73** 开始）抽成 DLL 后用
`.warudo-mod-research/.tools/WarudoApiScan` 扫描，结果是：

```
TOP-LEVEL public types: 3
NODE TYPES ([NodeType]): 0
ASSET TYPES ([AssetType]): 1      -> Vrm1SpringBoneWindAsset
PLUGIN CLASSES (Plugin subclass): 1 -> Vrm1SpringBoneWindPlugin : Warudo.Core.Plugins.Plugin
custom attribute vocabulary: AssetTypeAttribute x1, PluginTypeAttribute x1
```

类型继承关系（转储原文）：

```
[type] Vrm1SpringBoneWindAsset : Warudo.Core.Scenes.Asset
[type] Vrm1SpringBoneWindPlugin : Warudo.Core.Plugins.Plugin
```

✅ **同时验证到的 `assemblymodules.dat` 格式**（这次顺带破出来的）：

```
int32  模块数 = 1
str    程序集名，长度前缀，值 = umod-compiled-<GUID>
str    名字，长度前缀，值 = <Unknown>
...    若干索引字段
PE     裸 PE 镜像（MZ 开头），偏移 73
```

程序集名就是 `umod-compiled-cc3e9d75-fe55-48f1-93f6-817badab9fcd`
—— 和我们之前在别的插件 Mod 上测到的 `umod-compiled-<GUID>` 命名一致。

## 官方规范（📖 docs.warudo.app/zh/docs/scripting/api/assets）

```csharp
[AssetType(
    Id = "c6500f41-45be-4cbe-9a13-37b5ff60d057",  // 必填，这个「类型」的唯一 GUID
    Title = "Hello World",                          // 必填，显示在「添加资源」菜单里的名字
    Category = "CATEGORY_DEBUG",                    // 选填，菜单分组
    Singleton = false)]                             // 选填，true = 场景里只能有一个
public class HelloWorldAsset : Asset { }
```

- `Id` 是**类型**的 GUID，不是实例的 UUID（实例是 `asset.Id`），每个新类型都要自己生成一个
- 常用内置 `Category`：`CATEGORY_INPUT` `CATEGORY_CHARACTERS` `CATEGORY_PROP` `CATEGORY_ACCESSORY`
  `CATEGORY_ENVIRONMENT` `CATEGORY_CINEMATOGRAPHY` `CATEGORY_EXTERNAL_INTERACTION` `CATEGORY_MOTION_CAPTURE`
  （✅ 本机字符串堆确认 `Warudo.Plugins.Core.dll` 里确实有 `CATEGORY_DEBUG` 与 `CATEGORY_MOTION_CAPTURE`）

### 基类选哪个

| 基类 | 什么时候用 | 全名 |
|---|---|---|
| `Asset` | 最裸的资源，自己管 GameObject 生死 | `Warudo.Core.Scenes.Asset` |
| `GameObjectAsset` | **用户能在场景里挪动的东西**，自带 Transform 数据输入 | `Warudo.Plugins.Core.Assets.GameObjectAsset` |
| `GenericTrackerAsset` | 追踪器（自定义面捕/动捕的起点） | `Warudo.Plugins.Core.Assets.MotionCapture.GenericTrackerAsset` |

✅ `Warudo.Plugins.Core.Assets.GameObjectAsset : Warudo.Core.Scenes.Asset`（扫描转储确认）
✅ `...MotionCapture.GenericTrackerAsset : ...Character.CharacterDaemonAsset`（扫描转储确认）

### 资源能挂什么组件

📖 资源只有 **数据输入 `[DataInput]`** 和 **触发器 `[Trigger]`**。
**资源没有 `DataOutput`，也没有 `FlowInput` / `FlowOutput`** —— 这点和蓝图节点不一样。

📖 生命周期：`OnCreate()` / `OnDestroy()` / `OnUpdate()`（每帧，等于 Unity 的 `Update()`）。
📖 事件：`OnActiveStateChange` / `OnSelectedStateChange` / `OnNameChange`。
📖 资源**默认不是 active 的**，要靠 `SetActive(bool)` 置位。

## 本目录里有什么

| 文件 | 作用 |
|---|---|
| `HoTestAssetPlugin.cs` | `[PluginType]` 入口，`AssetTypes = new[] { typeof(HoTestCubeAsset) }` |
| `HoTestCubeAsset.cs` | `[AssetType]` 本体，继承 `GameObjectAsset`，造一个每帧自转的立方体；`OnCreate` 里 `SetActive(true)`，`Status` 实时显示 active 状态与帧计数 |

✅ **两份 `.cs` 都用 Roslyn 对着真实 Warudo 0.15.0 程序集编译通过**
（脚本：`.warudo-mod-research/.tools/compile-check-mod.ps1`，引用 `<游戏>/Warudo_Data/Managed`）。
也就是说 `[PluginType(..., AssetTypes = ...)]`、`[AssetType(...)]`、
`protected override GameObject CreateGameObject()`、`[Markdown]`、`[Trigger]` 这些签名都是对的。

❓ 编译通过 ≠ 运行时生效。**运行时的验收要靠下面那一节。**

## 构建

`HoUnityTools → FastBuildWarudoMod` → **「其他 Mod」** 页 → 选到 `CustomAsset` 工作区 →
类型应识别成 `插件` → **「构建 Warudo Mod」**。

| 项 | 值 |
|---|---|
| 工作区名 | `CustomAsset`（`ExportSettings.asset` 里的第 6 个 profile，`activeProfile` 索引 5） |
| 源目录 | `Assets/HoWarudoModTests/Mods/CustomAsset` |
| 落点目录 | `<数据目录>/StreamingAssets/Plugins` |
| 产物 | `Plugins/CustomAsset.warudo` |

> 落点是 `Plugins` 而不是新目录，是因为它本来就是插件类 Mod。
> 一个目录下可以放任意多个 `.warudo`（工坊里 `Plugins` 目录就有 6 个），互不干扰。

## 产物该长什么样

| 条目 | 有吗 | 说明 |
|---|---|---|
| `modinfo.dat` | ✅ 必有 | |
| `assemblymodules.dat` | ✅ 必有 | 本目录有 `.cs`，编译成 `umod-compiled-<GUID>` |
| `sharedassets.bin` / `.meta` | ❓ 待实测 | 场景没引用工程内资产时可能不生成；参照 VRM1SpringBoneWind 是**有**的 |

## 怎么在 Warudo 里验收（本轮待你实测）

1. 把 `CustomAsset.warudo` 构建进 `StreamingAssets/Plugins`
2. 重启 Warudo（插件类 Mod 只在启动时加载）
3. **资源 → 添加资源 → `CATEGORY_DEBUG` 分组** → 应该能看到 `Ho Test Cube`
4. 添加它 → 场景里出现一个小的立方体，**应当持续自转**（不碰它也在转）
5. 选中它 → 面板上应有 `Transform`、`Status`、`SpinSpeed` 三个数据输入
   和一个 `ResetRotation` 触发器按钮；点按钮立方体转正
6. 重点看 `Status`：应当是 `active=True  frames=<一直涨>  rotY=<一直变>`

> 为什么用「每帧自转」当验收标志：静止的立方体分不清是资源活着还是场景里本来就有东西，
> 转起来就说明 `OnUpdate()` 真的在跑。`frames` 计数是第二道防线 ——
> 就算看起来没转，`frames` 涨不涨也能直接区分「没跑」和「跑了但被覆盖了」。

## ★ 实测定论：`GameObjectAsset` 的 transform 归**数据输入**所有

这一节是本目录最有价值的东西 —— 踩了两轮坑，靠帧计数 + 元数据转储定出来的。

### 症状（✅ 你实测）

> 立方体在场景里能看到，但**平时静止**，只有在编辑器里**手动拖动**的时候才会转。

### 定位过程

1. 先在 `OnUpdate()` 里加了个帧计数显示到 `Status` 上 → **`frames` 一直在涨**
   → ✅ 说明 `OnUpdate()` 每帧都在跑，`Rotate()` 也确实执行了
2. 那「不转」就只剩一种解释：**转完当场被覆盖回去**
3. 用元数据转储查 `GameObjectAsset` 的公开字段，答案直接写在那儿：

```
[type] Warudo.Plugins.Core.Assets.GameObjectAsset : Warudo.Core.Scenes.Asset
    $bool                                  Enabled      [DataInput]
    $Warudo.Core.Data.Models.TransformData Transform    [DataInput]

[type] Warudo.Core.Data.Models.TransformData : Warudo.Core.Data.StructuredData
    $UnityEngine.Vector3 Position   [DataInput]
    $UnityEngine.Vector3 Rotation   [DataInput]   ← 欧拉角，单位是度
    $UnityEngine.Vector3 Scale      [DataInput]
    .UnityEngine.Quaternion get_RotationQuaternion() / set_RotationQuaternion(...)
    .void ApplyAsLocalTransform(UnityEngine.Transform)
    .void CopyFromLocalTransform(UnityEngine.Transform)
    .void ApplyAsWorldTransform(UnityEngine.Transform)
    .void CopyFromWorldTransform(UnityEngine.Transform)
```

`GameObjectAsset` **每帧都拿 `Transform` 这个数据输入回写 GameObject 的 transform**。
所以 `GameObject.transform.Rotate(...)` 写完，同一帧就被数据输入里的旧值盖掉了。
平时看不出来；一旦你在编辑器里拖动它，交互期间那条回写让位，旋转才露出来 ——
于是症状就成了「只有拖动时才转」。

### 正确写法

**旋转要写进资源自己的 `Transform` 数据输入，不要直接转 GameObject：**

```csharp
// ❌ 会被每帧覆盖
GameObject.transform.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);

// ✅ 写数据输入，GameObjectAsset 自己会把它应用到 GameObject 上
var euler = Transform.Rotation;                       // Vector3，欧拉角
euler.y = Mathf.Repeat(euler.y + spin * Time.deltaTime, 360f);
Transform.Rotation = euler;
```

> 这条适用于**任何**继承 `GameObjectAsset` / `FromSourceGameObjectAsset` 的资源，
> 包括道具（`PropAsset`）。想让它们动，改**数据输入**，别改 `transform`。

### 顺带记住

- `GameObjectAsset` 还自带一个 `[DataInput] bool Enabled` —— 面板上的显示/隐藏开关就是它
- `TransformData` 提供了 `ApplyAsLocalTransform` / `CopyFromLocalTransform` /
  `ApplyAsWorldTransform` / `CopyFromWorldTransform` 四个方法，
  需要手动在「数据输入」和「Unity Transform」之间同步时用得上

### 关于 `SetActive(true)`

📖 官方文档说资源**默认不是 active 的**，官方示例就是在 `OnCreate` 里 `SetActive(true)`，
本目录照做了（注意必须写 `protected override`，见下一节）。

❓ **但本机没有单独验证过它是否必需**：加它、去掉它，`frames` 都涨 ——
因为我们没做「去掉 `SetActive` 单独跑一次」这个对照实验。所以：

- ✅ 确定的：`frames` 在涨 → `OnUpdate()` 在跑
- ❓ 不确定的：这是 `SetActive(true)` 的功劳，还是本来就会跑

要补这个对照实验说一声。

## ⚠️ 踩坑：官方文档的 `OnCreate` 签名对 `GameObjectAsset` 是错的

📖 官方 Assets 文档的示例写的是：

```csharp
public override void OnCreate() { ... }   // ← 照抄到 GameObjectAsset 上编译不过
```

❌ 继承 `GameObjectAsset` 时这样写会报：

```
error CS0507: 当重写"protected"继承成员"GameObjectAsset.OnCreate()"时，无法更改访问修饰符
```

✅ 正确写法是 **`protected override void OnCreate()`**（本目录实测）。
文档那个 `public` 只对**直接继承 `Asset`** 成立（那一条未验证）。
这是 `.warudo-mod-research/.tools/compile-check-mod.ps1` 本地编译检查抓出来的 ——
**光看文档看不出来**。

## 其余未验证项（别当成结论）

- ❓ `[Markdown]` 字段在资源面板上的实际显示效果
- ❓ 资源实例的序列化：改过的参数会不会存进场景、重启后还在不在
- ❓ `sharedassets.*` 到底生不生成（见上表）
- ❓ `SetActive(true)` 是否真的必需（见上一节的对照实验说明）

## 未来要做的事：自定义面捕追踪器

你说的「专门放自定义追踪器类型的仓库」是
**[HakuyaLabs/WarudoPluginExamples](https://github.com/HakuyaLabs/WarudoPluginExamples)**，
里面正好有现成模板：

| 文件 | 用途 |
|---|---|
| `VMC/VMCFaceTrackingTemplate.cs` | **面捕**追踪模板，继承 `FaceTrackingTemplate` |
| `VMC/VMCPoseTrackingTemplate.cs` | 姿态追踪模板 |
| `VMC/Assets/VMCReceiverAsset.cs` | **一个真实的自定义追踪器资源**，`[AssetType] VMCReceiverAsset : GenericTrackerAsset` |
| `VMC/Nodes/GetVMCReceiverDataNode.cs` | 配套的取数节点 |
| `StreamDeck/` | 插件 + 节点 + Service 的完整例子 |

✅ 已读源码，要点：

```csharp
[AssetType(Id = "78fac3fd-...", Title = "VMC_RECEIVER", Category = "CATEGORY_MOTION_CAPTURE")]
public class VMCReceiverAsset : GenericTrackerAsset
{
    protected override bool UseHeadIK => false;
    protected override bool UseCharacterDaemon => false;
    protected override bool CanCalibrate => false;
    public override List<string> InputBlendShapes => ...;
    protected override bool UpdateRawData() { ... }   // 往 RawBlendShapes / RawBoneRotations 里填
}
```

而 `FaceTrackingTemplate` 负责把它挂到角色的追踪方案列表里
（`AssetDependencyTypes` / `CreateReceiver` / `BlendShapeMapper`）。

❓ 这些都还没在本机试过，属于下一步调研。

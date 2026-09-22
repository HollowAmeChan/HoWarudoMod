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

| 文件 | 作用 | 继承的基类 |
|---|---|---|
| `HoTestAssetPlugin.cs` | `[PluginType]` 入口，`AssetTypes` 里列出全部资源类型 | — |
| `HoTestCubeAsset.cs` | 立方体，`Transform` 数据输入驱动自转 | `GameObjectAsset` |
| `HoTestCounterAsset.cs` | 纯逻辑计数器，**没有 GameObject** | `Asset`（最裸） |
| `HoTestFaceTrackerAsset.cs` | **面捕追踪器**，输出合成正弦混合形状 | `GenericTrackerAsset` |
| `HoTestFullBodyTrackerAsset.cs` | **全身追踪器**，两只大臂正弦摆动 | `GenericFullBodyTrackerAsset` |
| `HoTestLightAsset.cs` | 青色点光源 | `LightAsset` |
| `HoTestCharacterDaemonAsset.cs` | 骨骼驱动型，**无抽象成员**也能注册 | `CharacterDaemonAsset` |
| `HoTestSourceAsset.cs` | 从「来源」下拉里加载 | `FromSourceGameObjectAsset` |

> ✅ 一个插件 Mod 可以一次注册**多个**资源类型 —— 全部列在
> `[PluginType(..., AssetTypes = new[] { ... })]` 里即可，缺一个就不出现在「添加资源」菜单。
> 这和 `NodeTypes` 是同一个道理。

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

## 怎么在 Warudo 里验收

构建 `CustomAsset` 工作区 → 重启 Warudo（插件类 Mod 只在启动时加载）→ 然后：

### A. 七个资源类型都出现了吗

**资源 → 添加资源**，应当看到：

| 分组 | 条目 | 来自 |
|---|---|---|
| `CATEGORY_DEBUG` | `Ho Test Cube` | `GameObjectAsset` |
| `CATEGORY_DEBUG` | `Ho Test Counter` | `Asset`（最裸） |
| `CATEGORY_DEBUG` | `Ho Test Light` | `LightAsset` |
| `CATEGORY_DEBUG` | `Ho Test Character Daemon` | `CharacterDaemonAsset` |
| `CATEGORY_PROP` | `Ho Test Source Prop` | `FromSourceGameObjectAsset` |
| `CATEGORY_MOTION_CAPTURE` | `Ho Test Face Tracker` | `GenericTrackerAsset` |
| `CATEGORY_MOTION_CAPTURE` | `Ho Test Pose Tracker` | `GenericFullBodyTrackerAsset` |

七个都在 = `AssetTypes` 注册成功；少一个就是漏列了。

### A2. `Ho Test Light` / `Ho Test Pose Tracker`

- `Ho Test Light`：添加后场景里出现一盏**青色点光源**，选中可调颜色 / 强度 / Range
- `Ho Test Pose Tracker`：加进场景并指定 `Character` 后，角色**两只大臂前后摆**
- `Ho Test Source Prop`：加的瞬间 `Source` 下拉是空的（❓ 正常，来源体系未验证），
  能选中这个资源本身就算通过

### B. `Ho Test Cube`（GameObjectAsset）

- 添加后场景里出现小立方体，**不碰它也持续自转**
- 选中它：`Transform` / `Status` / `SpinSpeed` 三个数据输入 + `ResetRotation` 触发器
- `Status` 显示 `active=...  frames=<一直涨>  rotY=<一直变>`

### C. `Ho Test Counter`（最裸 Asset，没有 GameObject）

- 添加后场景里**不会出现任何东西**（它本来就没有 GameObject，这是正常的）
- 选中它：`Target` / `Interval` / `Count` 三个数据输入 + `Reset` / `Step` 两个触发器
- 默认 `Target=10, Interval=1` → 每秒 `Count` +1，到 10 停下，`Status` 变成 `reached target 10`
- 点 `Reset` 归零重新数

> 这一条验的是「资源不一定有实体」+「纯逻辑资源的 `BroadcastDataInput` 能让 UI 跟着动」。

### D. `Ho Test Face Tracker`（面捕追踪器）★ 重点

1. 添加这个资源
2. 它自带的数据输入里找到 **`Character`**，选定一个角色
3. 角色的脸应当**不接任何硬件就动起来**：
   - 嘴巴 2 秒开合一次
   - 每 3 秒眨一次眼
   - 嘴角 8 秒一个来回的笑容

看到脸动 = 整条链路（`UpdateRawData()` → `RawBlendShapes` → 角色）通了。
看不到动 = ❓ 说明还需要配 tracking template 或手动连线，见「下一步」。

> `TestSpeed` 可以调快慢；设 0 就冻住。

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

## ⚠️ 踩坑：官方示例/文档里的访问修饰符在 0.15.0 上基本都过时了

这是本目录最有复用价值的一条。**官方文档和官方示例里的 `OnCreate` / `UseHeadIK`
签名与 0.15.0 的实际程序集不一致**，照抄必报 CS0507。
下面全部是本地 Roslyn 编译实测（`compile-check-mod.ps1`），**光看文档看不出来**。

| 成员 | 官方文档/示例写的 | ✅ 0.15.0 实际是 | 出处 |
|---|---|---|---|
| `OnCreate()`（`Asset` 及所有子类） | `public override` | **`protected override`** | 定义在 `Warudo.Core.Data.Entity.OnCreate()` |
| `GameObjectAsset.OnCreate()` | `public override` | **`protected override`** | 同上 |
| `GenericTrackerAsset.UseHeadIK` | `protected override` | **`public override`** | 官方 VMC 示例写的是 protected（0.14.x） |
| `GenericTrackerAsset.UseCharacterDaemon` | `protected override` | **`public override`** | 同上 |
| `GenericTrackerAsset.CanCalibrate` | `protected override` | **`public override`** | 同上 |
| `GenericTrackerAsset.InputBlendShapes` | `public override` | `public override` ✅ 一致 | |
| `GenericTrackerAsset.UpdateRawData()` | `protected override` | `protected override` ✅ 一致 | |
| `Asset.OnUpdate()` | `public override` | `public override` ✅ 一致 | |

结论：**拿到任何 Warudo 示例代码，先本地编译一遍再抄。**
`compile-check-mod.ps1` 就是干这个的，几秒钟出结果，不用开 Unity。

## 已做的面捕追踪器

你说的那个「放自定义追踪器类型的仓库」是
**[HakuyaLabs/WarudoPluginExamples](https://github.com/HakuyaLabs/WarudoPluginExamples)**，里面：

| 文件 | 用途 |
|---|---|
| `VMC/VMCFaceTrackingTemplate.cs` | **面捕**追踪模板，继承 `FaceTrackingTemplate` |
| `VMC/VMCPoseTrackingTemplate.cs` | 姿态追踪模板 |
| `VMC/Assets/VMCReceiverAsset.cs` | 真实的自定义追踪器资源，`[AssetType] VMCReceiverAsset : GenericTrackerAsset` |
| `VMC/Nodes/GetVMCReceiverDataNode.cs` | 配套的取数节点 |
| `StreamDeck/` | 插件 + 节点 + Service 的完整例子 |

本目录的 `HoTestFaceTrackerAsset.cs` 就是照这个形态写的，但它**只依赖 Plugins.Core**，
不碰游戏本体，所以能本地编译验证。

### 关键发现：混合形状名字是 UTF-16 字面量，ASCII 扫 DLL 扫不到

Warudo 认的名字是 ARKit 那 52 个的**小驼峰**写法（`jawOpen` / `eyeBlinkLeft` / `mouthSmileLeft` …）。

✅ **证据获取方式**（这个坑值得记住）：`.NET` 元数据里
**类型/字段/方法名是 UTF-8（`#Strings` 堆），而字符串字面量是 UTF-16（`#US` 堆）**。
用 ASCII 扫 `Warudo.Plugins.Core.dll` 完全找不到这批名字，改成按 UTF-16 每两字节取一个
`char` 再扫就全出来了。

✅ 但**不要手抄这份清单** —— 基类已经给了现成的：
`Warudo.Plugins.Core.Utils.BlendShapes.ARKitBlendShapeNames`（`public static string[]`，本机元数据实测）。
本目录就是这么用的：

```csharp
public override List<string> InputBlendShapes => BlendShapes.ARKitBlendShapeNames.ToList();
```

### 基类已经替你做完了大部分

✅ 元数据转储实测，`GenericTrackerAsset` **自带一大堆 `[DataInput]`**，我们一行都不用写：
`MirroredTracking` / `BlendShapeSensitivity` / `HeadMovementIntensity` / `MaximalHeadTranslation` /
`BodyMovementIntensity` / `BodyRotation*` / `HeadRotationIntensity` / `HeadRotationOffset` /
`EyeMovementIntensity` / `EyeBlinkSensitivity` / `LinkedEyeBlinking` / `BlendShapesMapping` …
也自带可读的 `IsTracked` / `LatestBlendShapes` / `LatestHeadPosition` 等。

子类只要提供两样：

1. `InputBlendShapes` —— 本追踪器提供哪些混合形状
2. `UpdateRawData()` —— 每帧往 `RawBlendShapes` / `RawBoneRotations` / `RawBonePositions` /
   `RawRootTransform` 里填原始数据；返回 `false` 表示这帧没数据

### 本目录的追踪器输出的是**合成正弦数据**

不接任何硬件：`jawOpen` 2 秒一个来回、每 3 秒眨一次眼、`mouthSmile*` 8 秒一个来回。
目的是把 **添加资源 → 指定角色 → 脸动** 这条链路先验通。
以后接真硬件，只改 `UpdateRawData()` 里的赋值，其余都不用动。

❓ **还没实测过**：它到底能不能独立工作（不配 template），以及混合形状是否真能到达角色脸。

## ✅ 全部可派生的资源基类，已 7/7 覆盖

用元数据把 `Warudo.Core.dll` + `Warudo.Plugins.Core.dll` 里继承自
`Warudo.Core.Scenes.Asset` 的类型**全部**求了一遍闭包，一共 18 个，其中：

- **7 个 abstract** → 想加自己的资源就必须派生它们（就是下面这 7 个，已全覆盖）
- **11 个是 sealed 或内置具体类** → 直接用内置的就行，Mod 不需要管

### 7 个可派生基类

| # | 基类 | 本目录的最小实现 | 要求子类实现 |
|---|---|---|---|
| 1 | `Warudo.Core.Scenes.Asset`（最裸） | `HoTestCounterAsset` | 无 |
| 2 | `...Assets.GameObjectAsset` | `HoTestCubeAsset` | `CreateGameObject()` |
| 3 | `...Assets.FromSourceGameObjectAsset` | `HoTestSourceAsset` | `GetSources()` |
| 4 | `...Assets.Character.CharacterDaemonAsset` | `HoTestCharacterDaemonAsset` | **无**（最省事） |
| 5 | `...Assets.Environment.LightAsset` | `HoTestLightAsset` | `CreateGameObject()` + `IsRangeSupported()` |
| 6 | `...Assets.MotionCapture.GenericTrackerAsset` | `HoTestFaceTrackerAsset` | `UpdateRawData()` |
| 7 | `...Assets.MotionCapture.GenericFullBodyTrackerAsset` | `HoTestFullBodyTrackerAsset` | `UpdateRawData()` |

> 「要求子类实现」这一列是用**探针**问出来的：写一个什么都不实现的空子类去编译，
> 编译器会把缺的抽象成员一个个列出来。比读文档可靠得多 —— 文档是旧版本的。

### 那 11 个不用派生的

`CharacterAsset`(sealed) `EnvironmentAsset`(sealed) `PropAsset` `ScreenAsset` `AnchorAsset`
`CameraAsset` `FPSCounterAsset` `DirectionalLightAsset` `PointLightAsset`
`MagicaClothWindAsset` `MagicaCloth2WindAsset` `VRMWindAsset`

—— 这些都是开箱能用的具体资源，不需要 Mod。

## ★ 关于「看放 mod 的文件夹就懂分类」——查证结果

你说得对，**分类就是目录**。但查完之后有个结论要修正认知：

### `StreamingAssets` 下的目录分两类

`Warudo.Plugins.Core.dll` 的 **UTF-16 字面量堆**里存在这些目录名：

```
Props  Characters  Particles  Environments  Images  Sounds  Videos  LUTs
CharacterAnimations  Playground
```

看起来有 10 个分类，但其中 **`Images` / `Sounds` / `Videos` / `LUTs` 不是 `.warudo` 分类**，
它们是**直接丢文件的媒体目录**。证据：

1. ✅ 全部历史日志里 `[LocalResourceMonitor] Started monitoring` 只出现过
   `Characters` / `Environments` / `Props`（以及 `Plugins` / `Playground` / `Localizations`
   走的是另外的 monitor）—— **`Images` / `Sounds` / `Videos` / `LUTs` 一次都没出现过**
2. ✅ 它们对应的数据输入是**字符串文件名**而不是资源引用：
   `CameraAsset.LUTTexture [DataInput] string`、`LUTIntensity [DataInput] float`
3. ✅ 配套的是「打开文件夹」按钮而不是「选择资源」：
   `CameraAsset.LUTOpenImagesFolder()` / `ScreenAsset.OpenImagesFolder()` /
   `OpenVideosFolder()` / `OpenMaskImageFolder()`
4. ✅ `CameraAsset.LUTOpenImagesFolder()` 打开的是 **`Images`** 目录
   —— 说明 LUT 是 `Images/` 下的图片，`LUTs/` 甚至不是主入口

所以现阶段的结论是：

| 目录 | 性质 | 我们覆盖了吗 |
|---|---|---|
| `Props` / `Characters` / `Particles` / `Environments` / `CharacterAnimations` | **`.warudo` 资源分类** | ✅ 五个工作区 |
| `Plugins` | **`.warudo` 插件分类**（含资源类型 / 节点类型） | ✅ 第六个工作区 |
| `Playground` | 脚本热重载 | 不适用 |
| `Images` / `Sounds` / `Videos` / `LUTs` | ❓ **疑似纯文件目录** | 见下 |
| `HandPoses` / `Motions` / `Scenes` / `MMD` / `Music` / `LipSyncProfiles` … | ❓ 未在 Plugins.Core 里出现 | 未查 |

❓ **上表最后两行还没做实机定论。** 零成本的决定性实验（不用猜任何东西）：

1. 把现成的 `Props.warudo` **复制一份**到 `StreamingAssets/Images/`（随便叫什么名）
2. 重启 Warudo
3. 看日志里有没有 `[LocalResourceMonitor] Started monitoring Images`

有 → `Images` 是 `.warudo` 分类，我们得补；没有 → 确认是纯文件目录，到此为止。
`Sounds` / `Videos` / `LUTs` / `HandPoses` 同理，一次重启可以全放进去一起测。

## 还没做的：`CharacterTrackingTemplate`

追踪器资源加进去之后，用户还得手动指定角色、手动连线。官方做法是配一个
**tracking template**，让 Warudo 在角色的「追踪方案」里直接列出 `Ho Test Face Tracking`，
选中就自动建资源 + 连好蓝图。

❓ **障碍**：官方示例用的 `Warudo.Bootstrap.Templates.FaceTrackingTemplate` 在
**`Assembly-CSharp.dll`（游戏本体，3.3 MB）**里，不在 `Warudo.Core.dll` /
`Warudo.Plugins.Core.dll` 里。Plugins.Core 里只有它更底层的基类
`Warudo.Plugins.Core.Assets.Character.CharacterTrackingTemplate`。

所以有两条路，**都没试过**：
- 直接继承 `CharacterTrackingTemplate`（Plugins.Core 里的，肯定引用得到），
  自己实现 `Apply(CharacterAsset)` —— 但 `CreateReceiverResult` / `IBlendShapeMapper` /
  `IdentityBlendShapeMapper` 这些辅助类型也在 `Assembly-CSharp.dll` 里
- 赌 UMod 编 Mod 时也引用 `Assembly-CSharp.dll`（我们的编译检查脚本会把它算进去，
  因为它在 `Managed` 目录里 —— 但**这不代表 UMod 也这么干**）

最快的验证是：写一个最小 template 文件，**在 Unity 里真构建一次**，
看 UMod 报不报引用错误。一次构建就能定性。

## `GetSources()` 的来源体系（✅ 元数据实测）

```csharp
// 0.15.0：Context 是 Warudo.Core.Context 上的**静态**成员，不是 Asset 的成员。
// 官方 VMC 示例里的 `Context.PluginManager` 是 0.14.x 写法。
Context.ResourceManager.ProvideResources(string kind)   // -> List<ResourceProviderResult>
Context.ResourceManager.ResolveResourceUri(string)      // 由基类负责调用
```

| 类型 | 成员 |
|---|---|
| `ResourceProviderResult` | `string providerName` / `List<Resource> resources` |
| `Resource` | `string category` / `string label` / `Uri uri` |
| 扩展方法 | `ResourceUriProviderResultExtensions.ToAutoCompleteList(List<ResourceProviderResult>)` |

`kind` 取值（✅ 从 Plugins.Core 的 UTF-16 字面量堆里实测到）：
`prop` / `character` / `particle` / `environment` / `image` / `sound` / `video` / `LUT`

❓ 本目录用的是 `ProvideResources("prop")`，**没实机验证过下拉框是否真能列出道具来源**。
如果下拉是空的，第一个要试的就是换 kind。

## 其余未验证项（别当成结论）

- ❓ `[Markdown]` 字段在资源面板上的实际显示效果
- ❓ 资源实例的序列化：改过的参数会不会存进场景、重启后还在不在
- ❓ `sharedassets.*` 到底生不生成（见上表）
- ❓ `SetActive(true)` 是否真的必需（见上一节的对照实验说明）

❓ 这些都还没在本机试过，属于下一步调研。


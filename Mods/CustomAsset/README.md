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

## ✅ 实测发现：资源**必须**自己 `SetActive(true)`，否则不会被每帧驱动

第一次构建后你看到的**症状**：

> 立方体在场景里能看到，但**静止不动**，只有在编辑器里**手动拖它**的时候才会转。

✅ 这是**本机实测**到的现象。原因是 📖 官方文档写的那条默认行为：

> By default, assets are **NOT** active when they are created.
> You can set the active state of an asset by calling `SetActive(bool state)`.
> 官方给的写法就是在 `OnCreate` 里 `SetActive(true)`。

也就是说：**资源创建出来默认是「未就绪」状态，Warudo 不会每帧去驱动它**，
`OnUpdate()` 自然也不跑。修法就是照官方文档在 `OnCreate` 里显式置位（本目录已加上）：

```csharp
protected override void OnCreate()
{
    base.OnCreate();
    SetActive(true);
}
```

为了能一眼定性，本目录的 `Status` 字段现在会实时显示：

```
active=True  frames=1230  rotY=214
```

- `frames` 一直涨 → `OnUpdate()` 真的在跑
- `frames` 不动 → `OnUpdate()` 压根没被调用（资源没 active）

❓ **是否彻底解决，等你这一轮复验**。如果加了 `SetActive(true)` 还是只有在拖动时才转，
那说明成因不在 active 状态，得换方向查（下一个怀疑对象是 `GameObjectAsset` 的
`OnLateUpdate` / `BroadcastTransformOptimized` 会用数据输入里的 Transform 覆盖 GameObject 的 transform）。

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
- ❓ 资源是不是「被选中才激活」——你描述的「手动拖它才转」有没有可能是选中触发的，
  和 active 状态是两回事。加了 `SetActive(true)` 之后如果**常态就转**，
  就说明这条不成立

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

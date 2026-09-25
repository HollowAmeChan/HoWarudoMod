# Core/ 里哪些文件是**搬来的**，哪些是这里写的

> 2026-09-26 按源码与同步脚本核对。**权威清单是下面第 1 节那张表**（它在仓库里，能跟着代码走）。
> 同步脚本 `D:\Unity_Fork\HoUnityTools\.research\sync-modcore.ps1` 的 `$files`（第 22–44 行）
> 是**执行同一张表的工具**，它按 `.research/` 的惯例不入库 —— 所以别把它当唯一出处：
> **改了清单要同时改脚本，改了两边都要能对上。**

## 1. 搬来的 11 份（**以本表为准**；同步脚本只是执行它的工具）

| 这里的文件 | 出处（HoUnityTools 包内） | 干什么的 |
|---|---|---|
| `HoFaceExpression.cs` | `Runtime/FaceTracking/HoFaceExpression.cs` | 表达式求值器（解析一次、逐帧求值；求值**绝不抛异常**） |
| `HoFaceMiddleware.cs` | `Runtime/FaceTracking/HoFaceMiddleware.cs` | 行对象、曲线端点夹取、修饰符种类（**没有"内置默认配置"了**：`HoFaceMiddlewareDefaults` 2026-09-26 删） |
| `HoFaceProfile.cs` | `Runtime/FaceTracking/HoFaceProfile.cs` | `*.hoface.json` 的**格式名 + 入口**（真正的读写在 `HoFaceProfileJson.cs`） |
| `HoJson.cs` | `Runtime/FaceTracking/HoJson.cs` | 极小的 JSON 读取器——我们**所有**数据路径共用的那一份 |
| `HoVtsPacket.cs` | `Runtime/FaceTracking/HoVtsPacket.cs` | VTS 手机线格式：请求包构造 + 载荷解析（纯静态、不碰 socket，可离线测） |
| `HoFaceProfileJson.cs` | `Runtime/FaceTracking/HoFaceProfileJson.cs` | profile 的 JSON 读写（替代 `JsonUtility`） |
| `HoFaceTrackingChannels.cs` | `Runtime/FaceTracking/HoFaceTrackingChannels.cs` | 52 个规范形态键名（外带区域/模式/平滑分组等枚举） |
| `HoFaceNaming.cs` | `Runtime/FaceTracking/HoFaceNaming.cs` | 参数命名规则（`Ho/Drive/...`） |
| `HoFaceSemanticConnector.cs` | `Runtime/FaceTracking/HoFaceSemanticConnector.cs` | **语义名字表**：槽表（`key` / `note`）+ **直接引用** Hub；**顺序即下标**。2026-09-26 替掉原来的 `HoFaceSemanticAsset`（ScriptableObject 资产，已删：不做跨角色共享词表） |
| `HoFaceSemanticHub.cs` | `Runtime/FaceTracking/HoFaceSemanticHub.cs` | **语义运行期槽**：`float[] values` + `string[] names`，取值/写值/**按名字开槽**（`ClaimSlot`）。没有长度政策、不认识表 |
| `HoFaceSemanticWriterBehaviour.cs` | `Runtime/FaceTracking/HoFaceSemanticWriterBehaviour.cs` | **语义写手**（`StateMachineBehaviour`，挂在控制器的状态上）：按表达式从 Animator 参数算值、按**名字**写进 Hub。2026-09-26 替掉"用动画曲线写 `values.<i>`"（那条要求作者预先知道下标） |

**⚠️ 在两边各写一份，是本项目的既定做法 —— 而且不止 Hub 这一处。**
所以别为这一对单独找"少写一份"的路子：**不搞"只留包侧一份、让 build 填进来"，
也不搞子 asmdef 那套绕法**。两份就是两份，靠 §1 那张清单 + 同步脚本保证不漂；
清单之外的重复留给各自的机制（不是本文件管的范围）。
（原因：两边是**两个 Unity 工程、两个程序集**，Unity 工程之间不能互相引用源码；
而"Unity 面板里预览到什么，Warudo 里就输出什么"要求这份代码是同一份 —— 见 §3。）

实测（2026-09-25）：`MonoBehaviour` 与 `ScriptableObject` 在 mod 程序集里**编译通过且 lint clean**
（2026-09-26 起这 11 份里**已经没有 `ScriptableObject` 了** —— 语义词表从资产改成了 `HoFaceSemanticConnector` 组件；
另外多了个 `StateMachineBehaviour`：`HoFaceSemanticWriterBehaviour`。两类基类都在 UnityEngine 里，mod 侧编译已验）；
FastBuild 会把 mod 源码复制进包、由 UMod 编译（`Editor/FastBuildWarudoMod`，另有产物校验器专门查这件事）。
⚠️ 这三个文件里**故意没有 `#if UNITY_EDITOR`** —— 同步脚本只加文件头、换命名空间，条件编译块会让两边不一致。
⚠️ 这三个文件里的组件是**要在预制件 / 控制器资产上序列化的**，所以字段一律 **public**、
不用"私有字段 + `[SerializeField]`"（能不能在 mod 程序集里序列化没实测过）。

**唯一被改的是命名空间**：`Hollow.HoUnityTools.FaceTracking` → `HoFaceTracking.Core`
（脚本第 33–34 行定义、第 63 行做全文替换）。代码本身一个字节都没动。

每份文件顶上还加了 **6 行 ASCII 标记 + 一个空行**（脚本第 37–45 行），
例：`Core/HoFaceMiddleware.cs:1-7`。看到这个头就知道"别在这儿改"。

**2026-09-26 实测：这 11 份与"重跑一遍同步"的输出逐字节一致**
（用脚本的同一套规则——去 BOM、换命名空间、归一成 LF、拼上头——重算一遍再比字节；
11 份全 identical，包侧主本也是无 BOM UTF-8）。所以现在两边没有分叉。

## 2. 这里自己写的 8 份（**不在**同步清单里）

| 文件 | 干什么的 |
|---|---|
| `HoFaceChain.cs` | **参数层**的运行期求值器（Warudo 侧）：裸线名 → 规范参数，出口是一份字典 |
| `HoFaceSolver.cs` | **控制求解**：从参数反求动画输出（融合形状 / 头姿 / 头位 / 根位 / 骨骼数组）。**零配置** |
| `HoFaceController.cs` | **控制器模式（唯一求值路径）**：从插件沙箱读 bundle（`ReadFileBytes` → `LoadFromMemory`）、跑真 `AnimatorController` + 它绑定的 rig、在隐藏影子上采结果。**✅ 已实测**：沙箱放行 AssetBundle、参数写进去、混合树解算、形状采回来整条通（见 mod `README.md` §1.1.2） |
| `HoFaceInputState.cs` | 最近一帧的共享状态 + 接收器统计（接收器写、节点读，唯一交接点）；**两条来源互斥**（手机 / VTS 服务端） |
| `HoVtsIphoneReceiver.cs` | VTS 手机接收器：UDP + `iOSTrackingDataRequest`，主线程轮询、**没有线程** |
| `HoFaceProfileStore.cs` | 插件沙箱目录里 `*.hoface.json` 的列表与读取（只能用 `GetFileEntries`） |
| `HoVtsApiServer.cs` | **VTS 公开 API 的服务端**（给本机 VB 用）：UDP `47779` 状态广播 + WebSocket 服务端 + 握手 + 收注入；握手用的 SHA-1 是手写的（`System.Security.Cryptography` 被禁） |
| `HoVtsApiPacket.cs` | 上面那个的报文解析与应答（纯静态、不碰 socket，跟 `HoVtsPacket` 一个路子） |

改这 8 份**不用**跑同步；它们不会被脚本碰。

## 3. 为什么是"搬"而不是"引用"

Unity 工程之间不能互相引用源码；junction 又不可靠（Unity 的资源数据库不跟着走）。
但这份代码**必须是同一份**——它是"Unity 面板里预览到什么，Warudo 里就输出什么"的**唯一保证**。
两边各写一份求值器，迟早会在某次改动后悄悄分叉，而分叉的表现是"Warudo 里的表情和面板里不一样"，
极难定位。

## 4. 怎么重新同步 / 改哪边为准

**主本在 HoUnityTools 包**（`Runtime/FaceTracking/`）。规则是：

1. 改**包**里的那 11 份中的任何一份；
2. 跑脚本（它会覆盖 mod 侧副本）；
3. 包侧与 mod 侧一起交给人提交 —— **本工作区的提交统一由人处理，不要在同步后自己 commit**。

```powershell
& D:\Unity_Fork\HoUnityTools\.research\sync-modcore.ps1
```

脚本会：重新拷贝这 11 份 → 换命名空间 → 加标记头 → 写成**不带 BOM 的 UTF-8 + LF**
（Mod 的 `.cs` 不带 BOM；Unity 包的文档才带 BOM），最后再把整个 mod 工作区里**所有** `.cs`
的行尾统一成 LF（脚本第 83–94 行）。脚本是幂等的。

⚠️ **在 mod 的副本里改会被无声冲掉**——下次同步直接覆盖。

⚠️ **行尾必须统一**：一个文件里混用 CRLF 和 LF，Unity 每次导入都会报
`There are inconsistent line endings in the '…' script`，而且行号会不准。这个工程的约定是
**全部 LF**。源文件是 LF，所以脚本里的头注释也必须是 LF —— 之前那里用过 CRLF 拼头，正好踩中这个警告。

⚠️ **脚本是 ASCII-only 的**，不要往里面写中文：PowerShell 5.1 在 `.ps1` 没有 BOM 时按 ANSI 读，
任何非 ASCII 字节都会变成乱码。中文说明就放在本文里。

## 5. 包里有、这里**没有**搬的东西

`Runtime/FaceTracking/` 下另外 4 个文件（**不在**同步清单里），以及编辑器的会话：

| 包里的文件 | 是什么（照它自己的头注释） |
|---|---|
| `HoFaceBlendTreePeek.cs` | 混合树观察台：把**正在生效的面捕会话**（影子台）的参数每帧抄到一个可见 Animator 上，好在 Animator 窗口里看那棵树 |
| `HoFaceJelly.cs` | 一维阻尼谐振子（果冻 / 摆锤）状态；"中间层的**参数生产**里产出带物理的那个参数" |
| `HoFaceOutputOwnership.cs` | 形态键写入的**预留**表：防止我们的写入者（含清理）覆盖已被接管的形态键 |
| `HoFaceShadowLink.cs` | 会话与调试器之间的**唯一联系点**：会话登记"正在生效的影子 Animator"，调试组件从这儿取 |

* 上面 4 个在 mod 的 `.cs` 里**一处引用都没有**（grep 整个 mod 目录：一个都没出现）。
  Warudo 侧的求值不需要它们：那边没有 Animator、没有代理渲染器、也没有通道模式
  （`Core/HoFaceChain.cs:3-13`）。
* `Editor/FaceTracking/HoFaceAnimationSession.cs`（编辑器那个会话，不在 `Runtime/` 下）—— 它一半在干
  影子 Animator 和代理渲染器的活，Warudo 侧没有也不该有。**逐帧求值的语义**由
  `Core/HoFaceChain.cs`（这个文件是这里写的）实现：`HoFaceChain.cs:14` 明说求值顺序与
  `HoFaceAnimationSession.Tick` 一致。
* 编辑器面板 / 调试器 / 输入 Hub / 接收器 —— 全是 Unity 侧的事。

## 6. Warudo 侧比编辑器版**少**的那一层

编辑器版取变量是三层：**形态键通道**（带模式 / 输入曲线 / 断流回中性）→ 输入行 → 原始线名。
Warudo 侧**没有通道**：那套是"这台设备、这张脸"的校准，属于 Unity 里的组件，不属于运行期。
所以这里是两层：**输入行的结果（缺键保持上一帧）→ 原始线名**（`HoFaceChain.cs:16-22`）。
求值、缺键语义、修饰符顺序、曲线端点夹取仍由上面那 11 份（与包同源）实现。

## 7. 数据路径的硬规则（跟"搬"这件事直接相关）

**我们的数据一律不用 `JsonUtility`** —— 它在 Warudo 播放器里会静默丢掉 `List<嵌套类/内部类>` 字段，
而编辑器里一切正常。已经被咬过三次（写 profile、读 profile、收 VTS 包：52 个形态键全丢）。
所以 `HoJson.cs` / `HoVtsPacket.cs` / `HoFaceProfileJson.cs` 这三份**必须**跟着一起搬，
不能只搬"看起来像中间层"的那几份。规则原文见 `Core/HoJson.cs:18-25`。

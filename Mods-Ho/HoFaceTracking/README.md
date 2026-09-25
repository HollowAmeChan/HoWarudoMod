# HoFaceTracking（Mods-Ho）

面捕的 Warudo 插件 Mod：**接收 + 处理**两半都在这一个 Mod 里。

* **接收器节点**：把来源发来的原始数据收进来，**原样**交出去（不改名、不换算）。
* **参数处理节点**：拿一份 `*.hoface.json` 中间层配置，把原始线名转成规范参数，
  只交出一份字典（`参数`）与一个判断（`有脸`）。
* **控制求解节点**：**从参数反求动画输出**（零配置），输出的 5 个端口与官方接收器取数节点**同形**：
  `IsTracked` / `BlendShapes` / `HeadPosition` / `RootPosition` / `BoneRotations`。角色不在这边 ——
  挂到角色上由**官方那三个应用节点**在图上选
  （设置角色面部追踪 BlendShape 列表 / 覆盖角色骨骼旋转偏移列表 / 覆盖角色根位置），
  所以本 Mod 一个角色引用都没有。

> **文档状态：2026-09-25 按源码逐条核对。** 分三段：**现状** → **目标形态** → **待清理 / 未实测**。
> * `Core/` 里哪些文件是从 HoUnityTools 包搬来的、怎么重新同步 → [`Core/PORTED.md`](Core/PORTED.md)
> * 方案总览（权威路线图）→ HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md`

---

## 1. 现状（源码里是什么）

插件身份（`HoFaceTrackingPlugin.cs:32-46`）：

| 项 | 值 |
|---|---|
| `[PluginType] Id` | `hollow.hofacetracking`（也是沙箱目录名） |
| Name / Version | `Ho Face Tracking` / `0.2.0` |
| NodeTypes | 3 个（见下表） |
| 命名空间 | `HoFaceTracking.PluginMod`（**不要**叫 `...Plugin`：会遮蔽 `Plugin` 基类，CS0118，见 `HoFaceTrackingPlugin.cs:20-21`） |

### 1.1 节点（4 个，一个 Mod 全包了）

| 节点（代码） | 面板标题 | 状态 | 干什么 |
|---|---|---|---|
| `Nodes/HoFaceReceiverStatusNode.cs` | HoFaceVTS接收器 | **正式** | 收来源的数据：手机（UDP 直连）或**本机的 VB**（VTS 服务端模式）；Connect / Disconnect + `原始值`（字典）+ `新鲜` + 一行 `状态`（**3 个输出口：`状态` 排在最下面**） |
| `Nodes/HoFaceParameterNode.cs` | HoFace参数处理 | **正式** | 读 `*.hoface.json`：裸线名 → 规范参数（+ `有脸` 判定）。**只交一份字典**，不装配（**3 个输出口**） |
| `Nodes/HoFaceSolverNode.cs` | HoFace控制求解 | **正式** | **从参数反求动画输出**（零配置）：官方同形的 5 个口 + 一个 `状态`（**6 个输出口**） |
| `Nodes/HoDebugLogNode.cs` | Ho调试日志 | **正式（通用件，跟面捕无关）** | 一个入口 + 一块只读显示 + 一个「复制」按钮（`[Trigger]`，**没有任何输出口**） |

**📖→✅ 2026-09-25 的拆分**：原来的 `Nodes/HoFaceMiddlewareNode.cs`（「Ho Face 处理链」= 中间层 + 控制器合一）
拆成了 `HoFaceParameterNode` + `HoFaceSolverNode`，**中间只隔一份字典**（`参数`）与一个判断（`有脸`）。
好处：别的来源（VB 走 VTS 服务端模式、以后别的面捕源）可以**跳过参数处理**直接喂控制求解。
完整理由、接口契约、迁移方法见 HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §2.0.1。

**2026-09-25 清掉的三个临时节点**（摸底用完就删了，别在旧蓝图里找）：

| 删掉的 | 曾经干什么 | 结论落在哪 |
|---|---|---|
| `HoFaceValueNode`（Ho Face 原始值（按线名）） | 按线名读一个原值 | 接收器节点的「原始值」口就够了 |
| `HoFaceCharacterProbeNode`（Ho Face 角色探针） | 层级 / 空间 / ARKit 对照 / Mod 资产 / 复刻 | 已写进路线图 §3–§5 与本文 §1.4–§1.5；**它的观察窗没了**（见 §3.2） |
| `HoFaceDebugNode` 的旧形态（Ho Face 调试台） | 多行框 + 自动抓取 / 抓取 / 追加 / 清空 + 摘要 | 收成通用件「Ho调试日志」`HoDebugLogNode`（一个入口 + 一块只读显示 + 一个复制按钮） |

`NodeType.Id`（在场景 json 里认节点用）：
接收器 `1f4b7c2e-9a3d-4e51-b8c7-2d6a0f9e4b73`、
**参数处理 `a41d0c86-6f52-4b19-8d3a-5e2c71b904af`**、
**控制求解 `7c3a91d6-4f2b-48e7-9a15-63d8f0b2c47e`（沿用旧「处理链」那个）**、
调试日志 `e2a47f83-5d19-4c6b-a07e-91b3c58d4f26`。分类都是 `Ho Face Tracking`。

⚠️ **Id 的这段安排是有意的**：老「处理链」的 Id 给了**控制求解** —— 于是升级之后，
指官方三个应用节点的那 **5 根线原样保住**；参数处理是新 Id，需要重接的只有接收器过来那几根
（< 5 根）。另外：Id 或口名一变，老连线就是孤儿线（见 HoUnityTools `docs/pitfalls/WARUDO_INSPECTION.md` §7）。

**接收器节点端口**（`HoFaceReceiverStatusNode.cs`）

* 输入：`手机 IPv4` / `手机端口`（默认 `21412`）/ `本机端口`（默认 `49985`）
  —— 手机模式用；**`VTS 服务端模式（本机 VB）`**（勾选框）+ `API 端口`（默认 `8002`）—— 本机模式用
* flow：`Connect` / `Disconnect`（Connect 按上面那个勾选框决定起哪个模式，两个模式**互斥**）
* 输出（**3 个**，**数据口在前、`状态` 在最后**）：
  * `原始值`(10)（`Dictionary<string,float>`，**列表语义 → 单独一个口**，喂参数处理）；
  * `新鲜`(20)（布尔，喂参数处理的「输入新鲜」；断流回中性靠它 —— 这是信号，不是给人看的）；
  * `状态`(30)（一行文本：**来源** / 状态说明 / 本帧键 / 帧·坏帧 / 距上帧；手机模式**只在真丢包时**多一行来源提示，
    本机模式改显示"客户端几个 / 累计注入几次 / 谁连上来的"）。
* ⚠️ **轮询是这个节点驱动的**：`OnUpdate` 里调 `HoFaceInputState.Poll()`（`:42-46`）——
  它不在图里，就没有人收包 / 没人应答 VB。
* **2026-09-25 收口**：原来九个口里的 `运行中` / `本帧键数` / `距上帧秒` / `累计帧·坏帧` / `外来来源` /
  `本帧原始值` 都只是"给人看一眼"、不驱动任何节点（`本帧原始值` 还是 `原始值` 的文本版），全并进 `状态`。
  想看整张表就把 `原始值` 接「Ho调试日志」（那边摊成 `线名 = 值`）。

### 1.1.1 VTS 服务端模式（本机 VB 直连，2026-09-25 加）

**为什么**：VBridger 的「发送到 VTube Studio」模式里 **VB 是客户端**，它只会去连一个 VTS **服务端**；
我们想收这份数据，就得**扮演那个服务端** —— 这样用户**不用改自己的 VB 用法**（不必切成 VMC 模式）。
和手机那条路**方向是反的**：手机是我们发请求、手机发回来（UDP）；这里是 VB 主动连我们（TCP + WebSocket）。

实现分两个文件（都是本 Mod 自己写的，不在同步清单里）：

| 文件 | 干什么 |
|---|---|
| `Core/HoVtsApiServer.cs` | UDP `47779` 每 2 秒广播一份 `VTubeStudioAPIStateBroadcast`（**unsolicited 广播**，VB 靠它列出"可用的 VTS 客户端"）+ TCP 上的 WebSocket 服务端（握手 / 帧解析 / 分片 / ping-pong）+ 插件握手 + 收 `InjectParameterDataRequest` |
| `Core/HoVtsApiPacket.cs` | 报文解析与应答（**纯静态、不碰 socket**，跟 `HoVtsPacket` 一个路子）：解析 `parameterValues[].id/value` 与 `data.faceFound`；应答 `AuthenticationTokenResponse` / `AuthenticationResponse` / `InjectParameterDataResponse` / `APIStateResponse` |

关键取舍（都写在文件头）：

* **无线程**：跟手机接收器同一套架构（socket 全非阻塞、每帧 `Poll`）—— 少一类崩法，也不碰安全审查边界。
* **`faceFound` 直接用**：VTS 的注入请求自带这个字段，正好就是处理链要的「有脸」，不用我们猜。
* **参数名照收**：真 VTS 对"不存在的参数"会报错，我们没有参数表这个概念，所以一律回成功。
* **端口从 `8002` 起、被占就往后挪**：8001 是 VTS 自己的；挪了也没关系，因为**广播里带的是真实端口**。
* **SHA-1 是手写的**：WebSocket 握手要 `base64(sha1(key + GUID))`，而 `System.Security.Cryptography`
  **被 UMod 安全校验禁掉**（本机拿 `Trivial.CodeSecurity` 的默认规则集实测：只用 `SHA1.Create()` 的探针
  → `Illegal namespace = 1`，同条件控制组 = 0）。手写版本用 RFC 6455 的官方向量自证过
  （`dGhlIHNhbXBsZSBub25jZQ==` → `s3pPLMBiTxaQ9kYGzzhZRbK+xOo=` ✓，另加 `abc` / 空串两个向量）。
* ⚠️ **`8001` 上如果 VTS 也在跑**：两边都会广播，VB 的列表里会出现两条 —— 选我们那条（
  `Ho Face Tracking (Warudo)`，见 `HoVtsApiPacket.WindowTitle`）。
* ❓ **未在 Warudo 里跑过**：VB 的客户端列表**认不认一个"自称 VTS"的服务端**还没验（这是唯一的外部未知，
  代码这边该做的都做了：广播字段、握手、token、应答形态都照官方文档）。

**参数处理节点端口**（`HoFaceParameterNode.cs`）

* 输入（顺序用**显式 `[DataInput(order)]` 定死**，不靠声明顺序的默契）：
  `输入新鲜`(10) ← 接收器「新鲜」；`原始值`(20) ← 接收器「原始值」；`配置文件`(30)（沙箱里的文件名，可留空）。
* 输出（**3 个**）：
  * `参数`（`Dictionary<string,float>`，列表语义）—— 输出行的结果，**键已去掉 `ARKit/` 前缀**，
    保留名（`Head/RotX`…）原样；配置里写了别的名字（比如眼睑两根轴）也原样在这儿；
  * `有脸`（布尔）—— 判据是 `新鲜` 且 来源报了 `FaceFound ≠ 0`（**协议知识，所以在这一层**）；
  * `状态`（合并后的诊断，就这一个）—— 四行：配置行（配置名 · 输入/输出行数 · 原始键 · 新鲜度）·
    `问题：` · `沙箱：`（配置文件放哪儿）· `可用配置：`（沙箱里现成的几份，逗号分隔）。
    **2026-09-25 收口**：原来那五个诊断口（`沙箱目录` / `可用配置` / `配置问题` / `发出内容` / `数值预览`）
    全并进这一条；`数值预览` 那份长文本改成**按「重读配置」按钮时写进 `Player.log`**（不再常驻一个口），
    而且现在摊开的就是**出口那份参数**（按名字排序），跟下游拿到的完全一致。
* flow：`重读配置`（`Enter`，也有同名的 `[Trigger]` 按钮）。**没有 flow 触发** —— 输出端口惰性求值、
  一帧只算一次（`Ensure()` 用 `Time.frameCount` 兜），因为 Warudo 没承诺节点之间的执行顺序。
* `配置文件` 留空 = **这一层不做事**（**没有内置默认** —— 2026-09-25 按用户要求去掉）。
  ⚠️ 读不到指定的配置时**也不再沿用上一次编译好的那份**：链会被清掉，`参数` 变空，
  原因写在 `状态` 的 `问题：` 那行。以前的"留空用内置默认 + 读不到就留着旧链"造成过
  **"把配置文件删了、点重读，参数还在照旧输出"**（用户实测，极难查）。
* 状态变了就写一行 `Debug.Log`，所以不接调试节点也能从 `Player.log` 看参数处理状态。

`有脸` 的判据是来源那条 `FaceFound` 线名，**不是**"有没有收到键"：手机丢追时照样发 15 个标量、
只有 `BlendShapes` 没了（本帧键 65 → 15），所以 `Raw.Count > 0` 在丢追时依然是 true。

**控制求解节点端口**（`HoFaceSolverNode.cs`）

* 输入：`参数`(10) ← 参数处理的 `参数`（**任何能给出同形字典的来源都行**）；`有脸`(20)（**默认 `true`**：
  不接线时按"一直有脸"处理 —— VB 那条路通常只接 `参数`，而官方那张图要靠 `Is Tracked` 才肯应用）；
  `控制器（可选，AssetBundle 路径）`(30) —— 见 §1.1.2，**留空就是今天的行为**。
* 按钮：`重读控制器`（`[Trigger(200)]`，同一路径下文件被换掉时用）。
* 输出（**与官方取数节点同形的 5 个**）：`Is Tracked`（= 上游那个 `有脸` 原样传出）/ `BlendShapes`
  （字典，列表语义）/ `Head Position` / `Root Position` / `Bone Rotations`（数组，列表语义）
* 输出（诊断，就这一个）：`状态` —— 一行：`参数 N 个键 · 形状 N 个 · 有脸=是/否 · 头姿 (x, y, z)°`；
  填了控制器时再加一行 `控制器：…`（载入结果 / 失败在哪一步 / 对上几个参数）。
* ⚠️ **默认零配置**（红线）：不填控制器时只做装配，映射/曲线/平滑全在参数处理那一层。
* ⚠️ **缺键 = 中性**：字典里没有的键按 0 / identity 处理（VB 那条路不给 `Head/RotX`，头姿就是 identity）。
  保留名以外的键**原样进 `BlendShapes`**（官方那个应用节点只认角色身上真有的形态键，所以是安全的）。
* 节点销毁时会把影子与 bundle 放掉（`OnDestroy`），别留垃圾。

### 1.1.2 控制器模式（可选，读 AssetBundle，2026-09-25 加）

**想干什么**：让这个节点能"跑一个**真的 AnimatorController**"（混合树那套），于是这个 Mod 变成通用的 ——
用户给一个控制器，我们喂参数、把结果采出来交给官方那三个应用节点，**完全解耦**。

**⚠️ 两条硬约束（Unity 的，不是我们的选择）**：

1. **`.controller` 文件本身读不了**：它是**编辑器格式**（YAML，靠 GUID/fileID 引用别的资源），
   播放器里既没有 `UnityEditor.Animations`、也没有运行时反序列化器。运行时能拿到
   `RuntimeAnimatorController` 的容器只有两种：mod 自带资源（`SharedAssets`，不是解耦）与
   **AssetBundle**（`AssetBundle.LoadFromFile` + `LoadAllAssets<RuntimeAnimatorController>()`）← 走这条。
   所以输入是**一个 bundle 文件的路径**。
2. **bundle 里必须带"控制器原配的那套 rig"**：clip 是按**层级路径**（`Body/Head`）与**属性名**
   （`blendShape.JawOpen`）绑定的，而**运行时没有 API 能枚举一个 `AnimationClip` 的绑定**
   （`AnimationUtility` 是编辑器专属）。所以我们**造不出代理**去接住输出 —— 必须用它原配的层级。
   → bundle 里要打：**一个 GameObject 预制体（rig）+ 一个 `RuntimeAnimatorController`**。

**怎么读结果**（`Animator` **没有**"读混合树输出"的 API —— `GetFloat` 读的是你写进去的输入）：
`SkinnedMeshRenderer.GetBlendShapeWeight`（Unity 是 0..100 → 我们 /100 成 0..1）+
`Animator.GetBoneTransform(...)` 取代理骨骼**相对控制器默认姿势**的偏移（`Inverse(rest) * current`，
因为官方那个口的语义是"偏移：单位四元数 = 不改那根骨头"）。
**头/根位置仍由保留名装配** —— 控制器多半只管表情与骨骼，位置继续走数据，lipsync 类的控制器才不会把头部追踪弄没。

**几个实现细节**：影子对象用 `HideFlags.HideAndDontSave`；热更新会把静态引用丢掉而对象还活着，所以开跑前
`Resources.FindObjectsOfTypeAll<Animator>()` 扫一遍清同名旧影子（`GameObject.Find` 找不到这类对象）；
端口参数只写控制器**真有**的口（`Animator.parameters` 运行时可读），对不上的数量会显示在 `状态` 里；
`cullingMode = AlwaysAnimate`（影子在屏幕外，默认 culling 会让它不动）。

**❓ 还没验证的（第一次真机跑就看这几条）**：

1. ~~UMod 的安全校验放不放行 `UnityEngine.AssetBundle`~~ → **✅ 没被拦**（2026-09-25 那次构建报告里
   `Illegal Assembly Reference = '0'`，被点名的只有 `System.IO`，见下面 §「System.IO 被禁」那条）。
   本地问不出来这件事本身也值得记：`Trivial.CodeSecurity` 的默认规则集**连 `System.Net.Sockets` 都误判**
   （而它明明放行），那套规则只能当"否定信号"，见 HoUnityTools `docs/pitfalls/BUILD_AND_TOOLING.md` §4.2。
2. ~~用户自己打的 bundle 能不能 `LoadFromFile`~~ → **✅ 已实测**（用 `tools/Editor/HoDebugBundleBuilder.cs` 打的那份：
  打开 ✓ 载入控制器 ✓ 载入 rig ✓）。**UMod 导出的 `sharedassets.bin` 能不能直接读**仍然 ❓ 没验。
3. ~~运行期给隐藏对象加 `Animator` 后的 `parameters` / 求值 / 采样是否照常~~ → **✅ 已实测**（2026-09-25：bundle 载入、参数对上 2 个、形状值采到并随输入变）。

失败时**`状态` 会逐条点名**失败在哪一步（打不开 bundle / 里面没有控制器 / 里面没有 rig），
`Player.log` 里也有异常本体。先用一个最小 bundle 试通，再上真控制器。

⚠️ **`System.IO` 整个命名空间被禁（2026-09-25 实测）**：这次构建的**唯一真因**就是这个 ——
状态文字里用了 `System.IO.Path.GetFileName(path)`，直接
`Illegal reference to disallowed namespace: System.IO` + `Illegal reference to disallowed type: System.IO.Path`。
现在改成**自己按分隔符切**。本地 `compile-check.ps1` 的 `UMod sandbox lint` 名单**也加上了 `System.IO`**
（连带 `System.Reflection` 一族），以后这类引用本地就红。读写文件仍然只能走插件的沙箱 API
（`Plugin.PersistentData`，见 `HoFaceProfileStore`）。

### 1.2 `Core/` 里有什么

16 个 `.cs` = **8 份从 HoUnityTools 包同步过来** + 8 份这里自己写
（`HoFaceChain.cs` 参数层求值器 / `HoFaceSolver.cs` 控制求解器 / **`HoFaceController.cs` 控制器模式（AssetBundle）** /
`HoFaceInputState.cs` 共享状态 / `HoVtsIphoneReceiver.cs` UDP 接收器 / `HoFaceProfileStore.cs` 沙箱读写 /
`HoVtsApiServer.cs` VTS 服务端 / `HoVtsApiPacket.cs` 它的报文）。
清单、主本在哪边、怎么重新同步 → **`Core/PORTED.md`**。

### 1.3 中间层配置与插件沙箱

* 配置文件放**插件沙箱**：`Warudo_Data/StreamingAssets/Plugins/Data/<pluginId>/`
  —— 本 Mod 就是 `…/hollow.hofacetracking/`（路线图 §4.4 ✅ 已跑通）。节点上的「状态」口直接给路径，不用猜。
* 后缀 `*.hoface.json`（`Core/HoFaceProfileStore.cs:29`）。`Core/HoFaceProfile.cs` 现在只是
  "格式的名字 + 入口"，真正读写走自写的 `Core/HoFaceProfileJson.cs`。
* ~~沙箱里一份都没有时，插件会写一份内置默认当样板~~ **2026-09-25 去掉**：不再自动写任何东西
  （"删了它又自己冒出来"是坑）。沙箱里那份 `ho-2d-test1.hoface.json` 是**以前自动写的旧样板**（iFacialMocap 那套，已过时），
  不需要就自己删；删掉之后不会再生成
  （`HoFaceProfileStore.cs:32`、`:118-142`）。
* 列目录**只能用** `GetFileEntries`：`GetFiles` 的第三个参数是 `System.IO.SearchOption`，
  UMod 的构建期审查禁止引用 `System.IO.*`，写了直接构建失败（`HoFaceProfileStore.cs:9-13`）。
* 配置按文件时间戳失效重读；运行中新丢进去的文件靠"找不到就重列一次（1 秒冷却）"才看得见
  （`:46-56`、`:172-178`）。
* ⚠️ **数据路径一律不用 `JsonUtility`**（咬过三次：写 profile / 读 profile / 收 VTS 包 —— 52 个形态键静默全丢）
  → 全走 `HoJson` / `HoVtsPacket` / `HoFaceProfileJson`（`Core/HoJson.cs:18-25`）。

### 1.4 输入协议：现在只有 VTS 手机

* **请求式，不是手机主动推流**：每约 1 秒往 `手机:21412` 发
  `{"messageType":"iOSTrackingDataRequest","time":5,…}`（买 5 秒，协议只允许 0.5–10 秒），
  手机把数据发回**请求包的源 IP**、端口用请求里 `ports` 指定 —— 所以手机那边除开关没有要填的东西
  （`Core/HoVtsIphoneReceiver.cs:8-20`、`:44-51`）。
* `手机 IPv4` 填错时包被**静默丢掉**，现象和"手机没发 / 防火墙挡了 / 不在同一网段"一模一样 →
  **只在真丢包时**，接收器的「状态」口会多一行把最近被丢的来源 IP:端口摆出来（`HoFaceReceiverStatusNode.cs`）。
* 线名是**设备发来的原样**，而**实测「安卓版 VTS」发的是混合命名**（2026-09-25 拿到完整 65 键，逐条抄在这里）：

  | 类别 | 实际键名 | 说明 |
  |---|---|---|
  | **形态键** | `jawOpen`、`mouthSmile_L`、`mouthSmile_R`、`browInnerUp_L/R`、`eyeBlink_L/R`、`eyeSquint_L/R`、`cheekPuff`、`noseSneer_L/R`、`tongueOut`、`mouthFunnel`、`mouthPucker` … | **iFacialMocap 那套**：小驼峰 + `_L`/`_R`（`mouthLeft/Right`、`jawLeft/Right` 4 个不带后缀）|
  | **标量** | `Rotation_x/y/z`、`Position_x/y/z`、`EyeLeft_x/y/z`、`EyeRight_x/y/z`、`FaceFound`、`Hotkey`、`Timestamp` | **VTS 手机那套**（`iOSTrackingDataRequest` 的原生字段）|
  | 另有 | `EyeBlinkLeft`、`EyeBlinkRight`（PascalCase）| VTS 风格的眨眼，**探针里它到过 1.000** —— 真正在眨的是它 |
  | 另有 | `headUp` / `headDown` / `headLeft` / `headRight` / `headRollLeft` / `headRollRight` | iFacialMocap 的离散头姿标志（这台设备上基本恒为 0）|

  ⚠️ 所以**别假设"VTS 手机就是 PascalCase 形态键"**（`JawOpen`）—— 那是**另一种**方言，这台设备不发。
  改名与量纲**全在配置的输入行**里（`规范名 = 曲线(表达式(线名…))`）；
  内置默认表给每个规范名配**两行**（iFacialMocap 命名 + VTS 命名，**同名最后一行生效**），
  就是为了让这台混合方言的设备也能一张表跑通 —— 这两行不是啰嗦，是**方言兼容层**。
* **iFacialMocap 的接收端还没有**（它是设备侧的另一套协议，端口/握手都不同）：但它的**线名**已经出现在这台设备的 payload 里，
  所以内置默认表里那套 `_L/_R` 行**并不是废行**（曾经的判断是错的，见上表）。
  接收器这条链只有 VTS（手机 / 本机 VB）。

### 1.5 调试用的东西（2026-09-25 加，临时）

**① 一份透明的调试配置 `ho-debug-android.hoface.json`**（按**安卓版 VTS** 的输出结构命名；`Profiles/` 里一份、沙箱里一份）：

* 存在的理由：旧的内置默认表（= 沙箱里那份 `ho-2d-test1.hoface.json`，它的 notes 写着 "generated by probe for
  read-path test"）**不适合调试** —— 它给 52 个形状各配了**两条**输入行（iFacialMocap + VTS），
  而且**一行保留名都没有** ⇒ `Head Position` / `Root Position` / 头姿**永远是 0**（`ARKit/` 之外的输出只喂了 4 根眼睑轴）。
* 它是什么：**单一方言的透明直通** —— **65 个输入行**（52 个形态键 = 安卓 VTS 的 `jawOpen`/`mouthSmile_L`…，
  头/眼 12 个分量 ← `Rotation_x…` / `Position_x…` / `EyeLeft_x…` / `EyeRight_x…`；外加 `faceFound` ← `FaceFound`），
  58 个输出行（52 个 `ARKit/` 直通 ⇒ **`BlendShapes` 与输入一模一样，好逐键对照** + 6 个 `Head/` 保留名 ⇒ 头姿/头位真的会动）。
* **为什么调试配置是"单一方言"**（用户质问过，我改了）：一开始我把它写成"两种命名都收"（每个规范名两行），
  理由是"插件插哪台设备都能用"。**这是错的设计**：调试配置的目的是**透明**，混写会带来"一行永远不触发的噪音"，
  而且**正是它制造了下面那个"值恒 0"**。现在一份文件对一台设备，**一行一个规范名，不重复**。
  换 iPhone 时**别改这份**：照它把 52 个形态行的 `expression` 换成首字母大写（`jawOpen` → `JawOpen`）、标量行不动，
  另存成 `ho-debug-iphone.hoface.json` 即可；真拿不准就把探针打出的「raw 键（N 个）」那一行贴出来，按它生成。
* ✅ **引擎那条"同名多行 = 最后一行生效"是对的，别去改它**（2026-09-25 我一度当成 bug 改掉，已恢复）：
  它是有意的**覆盖 / 优先级**机制（想在别人的表上盖一行，就写在后面），而且**缺数据时不回退** ——
  覆盖行没数据 ⇒ 那一格就是 0。**"吵"比"偷偷拿下面那行的值"好**，静默回退才是最难查的那种。
  那次真正的错在**配置**：同一格混写了两种方言（安卓命名在前、iPhone 命名在后），设备只发安卓那套 ⇒
  后声明的那行没数据、把有数据的行顶掉 ⇒ 值恒 0；症状极具误导性：**只有 head 系与眨眼在动**
  （head 系是单行；眨眼是因为安卓 payload 里**恰好也有** PascalCase 的 `EyeBlinkLeft`，它那行是活的）。
  ⇒ 修法是**让配置只写它真会发的方言**（现在这份就是单方言、一行一个规范名），不是改语义。
* 安卓 VTS 的形状值是 **0..1**（实测 `jawOpen = 0.069`、`mouthSmile_L = 0.164`）⇒ 这份配置的形状行**不乘 0.01**；
  ⚠️ 而**内置默认表**里那套 iFacialMocap 行写的是 `* 0.01`（那是按 iFacialMocap **App** 的 0..100 写的）——
  拿内置默认表接安卓 VTS 会小 100 倍。
* **特意没有**：`Root/` 保留名（VTS 手机不报根位）。
  头姿/头位的**单位与符号未标定**（VTS 裸值直通），写在那一行的 `notes` 里 —— 调试要看的是"有没有流过去"，
  不是"好不好看"；标定好之后把它抄成自己的配置。
* ⚠️ **头/眼那些行必须带自己的 `curve`（第一版就是栽在这）**：不写 `curve` 时默认是 **`0..1 → 0..1` 线性**，
  而且 `HoFaceCurve.Transfer` 会**先按端点夹取**再求值 —— 度数（`Rotation_x ≈ ±3`）与位置（`Position_x ≈ ±2..5`）
  会被夹成 0/1，负值全变 0。表现是"**头姿恒为 `(0,0,0)°`、`Head Position` 恒为 `(0,0,0)`，而形态键一切正常**"，
  看着像"头那部分没接线"，其实是曲线把值夹没了。现在给的是 **±180（角度）/ ±100（位置）的宽范围恒等曲线**。
  完整规则也写进了 HoUnityTools `docs/FACE_TRACKING_MIDDLE_LAYER.md` §5 的 `curve` 那一段。
* 用法：在「HoFace参数处理」的 `配置文件` 里填 `ho-debug-android.hoface.json`（沙箱里那份；沙箱路径见 `状态` 口）。
  **改这个文件不用重启**：配置按文件时间戳失效重读（`HoFaceProfileStore`），存盘后一秒内自动生效。

**② 一个调试用的控制器 bundle**：菜单 **`HoWarudoModTests/造调试用控制器 bundle（AssetBundle）`**
（脚本 `tools/Editor/HoDebugBundleBuilder.cs`，**Editor 专用、不进 mod 包**）。

* 它造什么：一个三角形网格 + 两个 blend shape（名字故意 = 规范参数名 `jawOpen` / `mouthSmileLeft`）、
  一个 rig 预制体（`Animator` + `SkinnedMeshRenderer`、`updateWhenOffscreen`）、
  一个**单层控制器**：一条 `FreeformDirectional2D` 混合树、四个角各一条 clip（每个 clip **同时**写两个形状），
  打成一个 `_hodebug/hoface-controller-test.bundle`（**ChunkBasedCompression**；`LoadFromFile` 读得了，别用默认 LZMA）。
* ⚠️ **第一版是"两层、每层一条 1D 树"（一层一个参数），它是个坑，别改回去**：两层都是 `Override` +
  状态默认 `WriteDefaultValues = 1`，两层同时往**同一批属性**上写（各自还把自己的默认值写回去），
  谁赢取决于层的混合语义 —— 实测现象是 `mouthSmileLeft` 恒 0、`jawOpen` 也只有极小值，
  而且完全分不清是"参数没写上"还是"第二层没生效"。单层一条树就没有这个歧义：所有绑定在**同一个 motion** 里。
* 为什么必须在 **2021.3.45f2** 那个编辑器里打：AssetBundle 与 Unity 版本绑定，而 **Warudo 本体就是 2021.3.45f2**
  （读自 `Warudo_Data/globalgamemanagers`）；mod 工程也正好是它（`ProjectSettings/ProjectVersion.txt`）✓。
* 期望结果（粘进「HoFace控制求解」的 `控制器（可选，AssetBundle 路径）` 后看 `状态`）：
  `已载入：hoface-controller-test.bundle（参数 2 个，形状 2 个，网格 1 个）`，
  `对上参数 2 个`，并多一行 **`写入 jawOpen 0.123 / mouthSmileLeft 0.946`**（← 这一行是"参数真的写进去了"的证据），
  然后 `BlendShapes` 里出现 `jawOpen` / `mouthSmileLeft`，值随输入变（Unity 权重 0..100 → 我们 /100）。
* ⚠️ `写入` 那行报的是**我们写给 Animator 的数**，它到位 ≠ 形状到位：形状那一侧只看 `BlendShapes`。
  两行并排就能定性 —— 见 §1.6。

* ⚠️ 它验不到**骨骼**那条路：`Animator.GetBoneTransform` 要 **Humanoid Avatar**，这个最小 rig 没有，
  所以 `Bone Rotations` 会全是 identity（我们代码里 null → identity）—— 要验骨骼得塞一个带 Avatar 的人形模型。

### 1.6 现场：控制器里 `mouthSmileLeft = 0.000` 而参数层是 0.946（2026-09-25）

用户报的是"一直笑，参数层明明有值，控制器那格恒 0 —— 是不是你没写映射"。**不是映射的问题**，链条如下：

* 参数层（「HoFace参数处理」`参数` 口）**有值**：`Player.log` 里那份 `参数（输出行那份字典，按名字排序）`
  逐帧写着 `mouthSmileLeft = 0.946`（实测那一份里 `mouthSmileLeft` 在 0.09~1.00 之间动）⇒
  `ARKit/mouthSmileLeft → mouthSmileLeft` 的映射、以及它进 `Parameters` 字典都没问题
  （`HoFaceChain.cs` 的 `OutputKey` + `HoFaceTrackingChannels.Names`）。
* 控制器模式（`HoFaceSolverNode` 里 `controllerActive` 时）`BlendShapes` **只来自代理网格**（2 个键），
  所以那一格恒 0 只有两种可能：**参数没写进 Animator**（名字对不上）或 **控制器没把那格推到网格上**。
* 当时**看不出来是哪种** —— `状态` 只报了 `对上参数 2 个`（个数，不含值）。**这是观测缺口，不是代码 bug**。
  ⇒ 现在 `状态` 多一行 `写入 jawOpen 0.123 / mouthSmileLeft 0.946`（`HoFaceController.MatchedText`）：
  只要这行里 `mouthSmileLeft` 不是 0，就说明**输入到位了**，问题 100% 在控制器那一侧。
* 控制器那一侧的嫌疑已经定位到**第一版 bundle 的两层结构**（`Override` + `WriteDefaultValues = 1` 互写），
  `tools/Editor/HoDebugBundleBuilder.cs` 已改成**单层一条 2D 混合树**。
  ⚠️ **旧的 `hoface-controller-test.bundle` 不会自己变**：必须在 Unity 里重按一次菜单重造，
  再在节点上按 `重读控制器`（`HoFaceController.Prepare` 对同一路径是直接返回的，认不出文件被换过）。

---

## 2. 目标形态（**2 个 mod / 我们的 3 + 官方 3 = 6 个节点**）

```
[HoVtsTrack mod]                       [HoVtsTrackController mod]                [官方节点 ×3]
  HoVts 接收器   ──原始值/新鲜/状态──▶  HoFace参数处理 ──参数/有脸──▶ HoFace控制求解 ──▶ 设置角色面部追踪 BlendShape 列表
                （裸线名原样交出）        （读 *.hoface.json：            （零配置：从参数        覆盖角色骨骼旋转偏移列表
                                        裸线名→规范名 + 曲线/修饰符）    反求动画输出）          覆盖角色根位置
  〔同一个接收器的另一个模式〕VTS 服务端模式：VB 的 VTS 输出 ──参数/有脸──▶ HoFace控制求解
```

✅ **2026-09-25 已落地：`Ho Face 处理链` 拆成了 `HoFace参数处理` + `HoFace控制求解`**（完整理由、
接口契约、Id 归属见 HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §2.0.1）。要点：

* 拆点本来就是现成的：`HoFaceChain.Evaluate` 原来那三行里，`EvaluateInputs` / `EvaluateOutputs` 留在
  **参数处理**（`Core/HoFaceChain.cs`，出口多一份 `Parameters` 字典），`Assemble` 搬去
  **控制求解**（`Core/HoFaceSolver.cs`，零配置）。
* 两层之间**唯一的接口**：`参数`（`Dictionary<string,float>`，键 = **裸规范名**（`JawOpen`…）+ 保留名
  `Head/RotX|Y|Z`、`Head/PosX|Y|Z`、`Root/PosX|Y|Z`）与 `有脸`（bool）——
  **口径对齐官方 `BlendShapes`**，所以别的源（VB 的 VTS 输出、官方面捕源）可以不接参数处理，直接喂控制求解。
* **`有脸` 由上游算**（VTS 的判据依赖裸线名 `FaceFound` + 新鲜度，那是协议知识，求解看不到）；
  控制求解那个输入**默认 `true`**（不接线时按"一直有脸"处理）。
* **控制求解保持零配置**（一旦塞进曲线/平滑，"跳过参数处理"就没意义了）。
* 老 `处理链` 的 `NodeType.Id` 已经给了**控制求解**，所以指官方三个节点的那 5 根线原样保住。

✅ **第三条来源（同一天落地）：接收器加了「VTS 服务端」模式，不新增节点。**
目的是让用户**继续用 VB 原来的 VTS 输出模式**（不逼他换模式）。注意这跟今天的手机路**方向相反**：
今天是我们发请求、手机发回来（UDP）；VB 的 VTS 模式是 **VB 当客户端去连一个 VTS 服务端**
（WebSocket + 插件握手 + `InjectParameterDataRequest`），所以我们要实现的是 VTS API 的**服务端**那一侧
（UDP 47779 广播 + WebSocket 服务端 + 握手 + 解析注入请求；`data.faceFound` 正好就是「有脸」，
`parameterValues[].id` 正好就是那套 ARKit 名）。
❓ **做之前要先验**：VB 的客户端列表认不认一个"自称 VTS"的服务端。
⚠️ **VMC 那条不走**：Warudo 自带 VMC（`GET_VMC_RECEIVER_DATA`，输出 `IsTracked` + `BlendShapes`，形状也对齐），
技术上最省，但**要用户把 VB 切到 VMC 模式** —— 成本不该转嫁给用户（2026-09-25 否掉，只作后备）。

* 完整版见 HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §2（**6 = 我们的 3 个 + 官方那 3 个**）。
* 现状离目标的差距：两个 mod 还是一个（`hollow.hofacetracking`）；"控制器"还是**数据树**
  （`Core/HoFaceChain.cs`，没有影子 Animator）。（三个临时节点 2026-09-25 已经清掉，见 §1.1。）
* 为什么现在没拆成两个 mod：Warudo **每个 Mod 各自编译成一个程序集**，同名类型跨 Mod 是不同 `Type`，
  拆开就得复制代码 + 靠端口通信；边界应该是"**Mod 的种类**"（角色 / 插件），不是"功能模块"
  （`HoFaceTrackingPlugin.cs:12-15`）。
* 混合树为什么不是 `.controller`：插件 Mod 不能读盘、不能带已编译资源，而 Unity 播放器**无法从文件
  加载 `AnimatorController`**（只有 AssetBundle 能）—— 所以混合树改成**数据**，由 `HoFaceChain` 求值
  （`Core/HoFaceChain.cs:3-8`、`Nodes/HoFaceSolverNode.cs:11-16`）。角色身上也确实没有 controller
  （早前由角色探针实测：`controller=null`；那台探针已删，见 §1.1）。

---

## 3. 清理记录 / 未实测

### 3.1 已经清掉的（2026-09-25）

| 删掉的 | 为什么 |
|---|---|
| `Nodes/HoFaceValueNode.cs`（Ho Face 原始值（按线名）） | 接收器节点的「原始值」口给的信息更多，这个只是中间产物 |
| `Nodes/HoFaceCharacterProbeNode.cs`（Ho Face 角色探针） | 摸底完成，结论已经落进路线图 §3–§5；**它的观察窗也一起没了**（§3.2 有两条判据因此要另找工具） |

同步收缩了 `NodeTypes`（现在 3 个，`HoFaceTrackingPlugin.cs:38-43`）：漏列的节点即使编译进程序集
也不会出现在节点面板里（`:17-18`）。

`HoFaceDebugNode` 没删，但**重写成了一个通用件 `HoDebugLogNode`（面板名「Ho调试日志」）**
（`Nodes/HoDebugLogNode.cs`，2026-09-25 定案）。它跟面捕无关，谁都能用 —— 一个入口、一块只读显示、一个复制按钮：

| 端口 | 说明 |
|---|---|
| `[DataInput] object 写入` | 上游接这里，**什么类型都能接**（字符串 / 整张表 / 数组） |
| `[Markdown] [Transient] 日志` | **只读显示**（选不中）；**上游直接接在这一行上也可以**（那种情况下节点不碰它） |
| `[Trigger(30)] 复制` | 按钮：把**当前这段原文**整份写进系统剪贴板（`GUIUtility.systemCopyBuffer`） |

**为什么显示用只读的 `[Markdown]`（照抄官方「查看值」），而不是能选中的框**：
值在动的时候**框每帧重画，选区就被冲掉**（用户实测：Ctrl+A 之后还没来得及复制就没了）——
所以"能选中的框"这条路是死路，复制必须交给按钮。
官方 `InspectValueNode` 的显示字段就是 `[Markdown(13, False, False)] public String Text`（`warudo-knobs --attrs` 读的），
我们**原样照抄这一行**：控件由特性决定、不由类决定，这就是"复用内置节点那套玩意儿"。
（`InspectValueNode` 本身是 **public 非 sealed、`OnUpdate` 是 virtual**，继承技术上可行；
但它的 `OnUpdate` 靠"字段被推"喂值 —— 对我们不灵，还是得 override，继承只剩"基类端口会不会被发现"这个未验证风险。）

显示版会把换行补成 Markdown 硬换行（行尾两空格），**复制走的是没被改写过的原文**。

**按钮为什么是 `[Trigger]`、剪贴板为什么是 `UnityEngine.GUIUtility`**：
Warudo 的**纯按钮**就是 `[Trigger(order)]` —— 官方节点一大堆（`CommentNode.Edit/Done`、
`SetAssetPositionNode.AlignTargetWithAsset`…，`warudo-knobs --find-attr TriggerAttribute` 一抓一大把），
它**不占任何口**。`[FlowInput]` 也能点（接收器的「连接/断开」就是），但它会多一个 flow 出口 socket，
对"日志"这种节点是多余的 —— 第一版就是那样写的，收口时换成了 `[Trigger]`，现在这个节点**没有任何输出口**。
⚠️ 查官方用法时属性名要写全：`--find-attr TriggerAttribute`（**带 `Attribute` 后缀**）；
写成 `Trigger` 会**静默返回空**，我曾据此写出过"Core 里没有 `[Trigger]`"的错结论。
剪贴板则**只有** `UnityEngine.GUIUtility.systemCopyBuffer` 一家：Warudo 自己没有剪贴板 API
（两个 DLL 的 `--list Clipboard` 都是空），整个 Managed 目录里也只有 `UnityEngine.IMGUIModule.dll` 带这个名字。
⚠️ 本地 `tools/compile-check.ps1` 的引用表为此加回了 `UnityEngine.IMGUIModule.dll`。

**两条必须照抄，否则界面不重画**（都实测过，别再改回去）：
1. **写显示字段要「字段赋值 + `BroadcastDataInput`」**：官方 IL 就是 `stfld Text` 紧接着 `BroadcastDataInput("Text")`。
   只调 `SetDataInput` 时端口里有新值、**但界面上那块纹丝不动** —— 这一条是最贵的坑，查了两轮。
2. **输入口用 `object`**：用 `string` 的话，非字符串上游（整张 BlendShapes 表、骨骼数组）根本接不进来。

另外两条踩过的坑：`[Disabled]` 的口**收不到上游写入**（同图上处理链那个普通 `[DataInput]` 收得到）；
`[Markdown]` 那块**只调 `SetDataInput` 时不会刷新**（v6 实测：端口里有 45 字符、屏上还是旧文字）——
补上第 1 条的两句才活。完整证据链见 HoUnityTools `docs/pitfalls/WARUDO_INSPECTION.md` §7。

**值是怎么拿到的：不等推，顺着连线直接调上游那个口**（2026-09-25 被逼出来的，定案）。
实测现场：线**确实**接在「写入」上（`Player.log`：`输入连线：「A」←Ho Face 接收器（VTS 手机）.RawValues`
—— 那是**当时的节点标题**，2026-09-25 已改名为 `HoFaceVTS接收器`），
可 `A` 与端口一直是空，而同一根上游喂官方「查看值」有数据。所以不再赌"上游什么时候灌进字段"：

1. `Graph.GetInputDataConnections(this)` → 上游 `DataConnection` → `OutputNode` + `OutputPort`；
2. 口上挂着求值器：`Warudo.Core.Graphs.DataOutputPort.ComputedValue` 是 **`public Func<Object>`**，
   `port.ComputedValue()` 一行就是这一帧的值（`connection.OutputPort` 本身就是 `DataOutputPort`）；
3. 接收器的 `RawValues()` 就是 `HoFaceInputState.Snapshot()` 这种**纯读**，调一次就有值。

端口/字段那条老路留着当**兜底**（真被推过来时照样认）。四点要知道：
* **显示认几类值**（`Describe`）：字符串原样、"名字 → 值"的表（排序后摊平）、**数组/列表逐项**
  （`[i] = (x, y, z, w)`）、`Vector3` / `Quaternion` 用 F3；其余才交给 `ToString()`。
  ⚠️ 数组这一条是**修过的**：`object` 口拿到 `Bone Rotations`（`Quaternion[]`）时，
  只靠 `ToString()` 屏上只有 `UnityEngine.Quaternion[]` 一行 —— 而官方「检查值」把整个数组序列化成 JSON
  打了出来，所以"官方的能出值"。差的不是口、是**显示**。
* **直读的代价（承认的副作用）**：直读 = **替流程图求值一次上游那个口**。接收器的 `RawValues()` 就是
  `HoFaceInputState.Snapshot()`，**每调一次新建一个字典**（处理链本来要一次，这是额外的第二次）；
  我们还得把整张表摊成文本（排 65 个键 + 拼 ~1.3 KB 字符串）才能比出"变没变"。
  → 所以**不是每帧读**，而是每 `ReadInterval`（默认 **0.1 s = 10 Hz**）读一次：观感没差别、垃圾少 6 倍。
  **真要看每一帧的值，用官方「查看值」节点**（它读字段、不替谁求值）。
  另外求值时机变成由我们决定（我们的 `OnUpdate`）——我们自己的口都是现场算的纯读、处理链的 `Ensure()`
  还有 `Time.frameCount` 护栏，所以"时机"没有可观察后果；**但接有副作用的口进来，就等于让我们替你触发它**。
* **断流不清空**：保持最后一次内容，方便回头复制；重新有值就跟着变。

⚠️ **第 2 步绝不能写成反射**（第一版就是）：`Type.GetMethod(...)` + `MethodInfo.Invoke(...)` 会让 UMod 的安全校验
**直接毙掉构建** —— `Illegal reference to disallowed namespace: System.Reflection` → `BUILD FAILED!`。
`ComputedValue` 就是为此存在的非反射入口。

⚠️ **连 `value.GetType().Name` 都不行**（第二版栽在这）：它编译成 `MemberInfo::get_Name`，属于"间接非法引用"
（`Illegal Type References = '1'` / `Member References = '1'`）。想要类型信息就用 `is` 模式自己列几种认得的。
本地 `tools/compile-check.ps1` 现在有 **`UMod sandbox lint`** 阶段专门拦这类引用（`-LintOnly` 秒回）——
这两条都是真机构建烧出来的，完整报错见 HoUnityTools `docs/pitfalls/BUILD_AND_TOOLING.md` §4 / §4.1。

（顺带：官方 `InspectValueNode.OnUpdate` 里那句 `InvokeFlow(null, false)` **不是拉上游** ——
`warudo-knobs --il` 读出来是 `Graph.InvokeFlow(node, null, false)` → `invokedFlow.Invoke(node, null)`，
即"把自己接回流程往下游传"。照抄它并不能解释字段为什么空。）

⚠️ **"线明明接了却没值"**：这个节点的端口改过名/类型（`Content`/`Source`(string) → `A`(object)），
蓝图里**在改名之前接的那条线就是孤儿** —— UI 上可能还画着它（看着接在「写入」上），但求值找不到端口。
**有了直读之后这不再致命**（孤儿线也读得出值），但线还是重接一遍干净：
删掉这条线、重新从「接收器 · 原始值」拖到「写入」。

**现在这件事不用猜了 —— 节点自己会说**（2026-09-25 加，`Nodes/HoDebugLogNode.cs`）：
`OnUpdate` 里每 0.5 秒问一次 `Graph.GetInputDataConnections(this)`（**键就是输入口名**，
值带 `DataConnection.OutputNode` / `OutputPort` / `InputPort`），只写 Player.log、界面上看不见：

| 日志行 | 意思 |
|---|---|
| `[Ho 调试日志] 输入连线：（没有任何输入连线）` | 线根本没接上（或者接在别的节点上） |
| `[Ho 调试日志] 输入连线：「A」←HoFaceVTS接收器.RawValues` | 接对了（口名是**字段/方法名**，`RawValues` 而不是标签「原始值」；节点名取的是 `Node.Name`） |
| `[Ho 调试日志] 输入连线：「Content」← …（这个输入口已经不存在了 → 删掉这条线重接）` | 孤儿线（`InputPort` 为 `null`）—— 界面看着接了，其实废了 |
| `[Ho 调试日志] 还没有值：直读 「…」.RawValues = 0 个键 · 端口 = 空` | 线接对了、口也调到了，**上游自己现在没数据**（每秒一行，只在内容变了时写） |
| `[Ho 调试日志] 第一次拿到值：431 个字符（直读 「…」.RawValues）` | 通了（有值之后就不再报那两行） |

（`Node.Name` 填的是标题还是内部名**没验过** —— 只用来认人，不参与任何逻辑。孤儿线判定靠的是
`InputPort == null`，这条是确定的。）

**官方「查看值」是怎么拿值的**（`warudo-knobs --il` 读的它方法体，2026-09-25 本机 DLL）：
`OnUpdate` 里 `ldfld A` → `JsonConvert.SerializeObject` → `stfld Text` → `BroadcastDataInput("Text")`，
外加两道 early-return（**只在自己那张图**、**只在本机有 WebSocket 会话**时更新）。
它**读的是自己的字段** —— 那到底是"谁在什么时候把值写进这个字段"，官方代码里没承诺，
我们也不再去猜了（见上面的直读方案）；唯一确定的是**字段能拿到值**这件事在官方那张图上成立。
完整证据链（含 `InvokeFlow` 的 IL、直读的定规）见 HoUnityTools `docs/pitfalls/WARUDO_INSPECTION.md` §7–§8。

### 3.2 未实测（必须标着的）

* **`.controller` 随 mod 打包 + 取回**：一次都没试过（路线图 §7 待办 #2）。
  已知的那点证据来自**已经删掉的角色探针**：`Plugin.ModHost.SharedAssets`（`UMod.IModAssets`）的
  `CanLoadAssets=True / AssetCount=2`；**名字枚举不到**（`IModAssets` 没有枚举接口，别人的实现类型
  也不公开，cast 会 `CS0122`）。已知出路是 `Load<T>(int assetID)` 那一族重载 ——
  用 `0..AssetCount-1` 逐个试就不必猜名字（**还没试过**，见路线图 §7 待办 #2）。
  ⚠️ **探针删了之后，暂时没有现成的观察窗**：再验这件事得先加一次性探针、或者直接在做 `HoVtsTrackController`
  时顺手试。
  ⚠️ "取回来的运行期类型就是 `RuntimeAnimatorController`"这句**也没验过**（`Load("HoFaceTree")` 从没成功过）。
* ✅ **这几个节点的实际连线效果已实测跑通一半**（2026-09-25，安卓 VTS + `ho-debug-android.hoface.json`）：
  接收器 → 参数处理 → 控制求解 **全都在动**（52 个形态键、`Head Position` 有真值），
  **控制器模式也实测成立**（bundle 载入 ✓ 参数对上 2 个 ✓ 形状采到 ✓）。
  **仍未测**：① 控制求解那 5 个输出口接**官方三个应用节点**（路线图 §7 待办 #4）；② **VTS 服务端模式**（本机 VB 连我们）；
  ③ 控制器的**骨骼**那条路（`GetBoneTransform` 需要 Humanoid Avatar，测试 bundle 里没有 ⇒ 骨骼全是 identity）。
* **「覆盖角色根位置」的权重语义**（值是绝对还是增量）：路线图 §2.2 是**推断**，
  那一节写了实测判据（权重填 1 + 值填原点，看带位移的动画还动不动）。
* 已经量出来的那三条（别再当未知）：骨骼数组长度 = `(int)HumanBodyBones.LastBone`、
  **下标就是 `HumanBodyBones` 枚举值**、`InitialBoneLocalPositions[i] == localPosition`。
  ⚠️ **但"基准 / 局部还是世界"这半条并没有量出来**：那次量的时候角色**全身旋转都是 identity、也没有根位移**，
  局部/世界、当前/初始**全都相等**，区分不开（路线图 §5 已按此改正为 ❓）。
  要量就得让角色带**非 identity 旋转 + 根位移**再跑一次 —— **探针已经删了**，得先加个临时的观察窗。

### 3.3 已知限制（源码里写明的"没做"）

* 修饰符 `Delay` 只有枚举和字段，**没实现**（`Core/HoFaceMiddleware.cs:58`；`Core/HoFaceChain.cs:370`）。
* `BoneRotations` **只有 `Head` 不是单位四元数**，其余保持 identity = "不改那根骨头"
  （我们只有脸；`Core/HoFaceChain.cs:293-295`）。
* 接收器**不线程化**：socket 非阻塞，每帧 `Poll` 把排队的包收掉（`Core/HoVtsIphoneReceiver.cs:27-31`）。
* 接收器**不做断流回中性**：它只负责收 + 原样交；回中性由下游图按处理链的 `IsTracked` 做
  （`Nodes/HoFaceReceiverStatusNode.cs:137-140`）。
* 本机端口用 `49985`：官方 iFacialMocap 接收器资源占 `49983`，要用那个得先关掉它（路线图 §4.7）。
  **端口被占时会自动往后挪**（49985 → 49986 → …，最多 10 个）—— 见下面的"热更新"那条。
* **热更新会掐断已连上的接收器**（用户实测：Build 完 Warudo 热更新 → 接收器断连，"重新点连接"绑不上、
  只能重启 Warudo）。原因是**旧的那个 `UdpClient` 属于被换掉的旧程序集**，它不一定被回收，
  端口就一直是它占着。两条对策都已落地：
  1. `HoFaceTrackingPlugin.OnDestroy()`（插件被卸载时）主动 `HoFaceInputState.Stop()` 关掉 socket；
  2. 接收器自己**端口回退**：绑不上就往后挪一格再试 —— VTS 手机是往**我们请求里列出的端口**回的，
     所以本机换端口对手机完全透明。状态行会写明："监听 49986 ← …（49985 被占，已自动换端口）"。
  另外「Connect」现在会把结果写一行 `Player.log`（`Connect → 监听…/启动失败…`），
  热更新之后"点了没反应"时有现场可看；接收器的状态日志也不再每帧刷（累计帧数不再参与去重比较，
  实测它曾经把一份 `Player.log` 刷掉一万五千行）。
* 搁置项：不注册 `CharacterTrackingTemplate`、不做 tracker asset、不依赖官方"一个 mod 带一堆资源"
  的打包方式（路线图 §6）。

---

## 4. 怎么装 / 怎么验收

### 装

1. Unity 工程（BreakWarudo）里打开 **Mod Settings** 窗口（`UMod.Exporter.SettingsWindow`，
   见 `Assets/HoWarudoModTests/docs/打包与脚本规范.md:206`）把本 Mod 的**工作区**指到
   `Assets/HoWarudoModTests/Mods-Ho/HoFaceTracking`，**导出目录**指到 `StreamingAssets/Plugins`
   （插件类 Mod 的落点，同文档 `:52`）。
2. 导出前关掉 Warudo、清 `StreamingAssets/Playground` 下的同名脚本（同文档 `:300`）。
3. 本地检查：`Assets/HoWarudoModTests/tools/compile-check.ps1`
   —— 它跑两件事：(1) **`UMod sandbox lint`**，拦安全校验会毙掉的引用（`System.Reflection` 一族，含
   `value.GetType().Name` 这种间接引用；`-LintOnly` 只跑这一步，秒回）；(2) 对着真机 DLL 编一遍。
   **交付前先跑它** —— 这两步都过不了的东西，真机构建一定过不了。

### 验收（收通 → 参数 → 求解）

1. 手机开 VTS，设置第一页底部打开 **3rd Party PC Clients**（它监听的端口默认 `21412`）。
   （或者：**本机 VB** 那一路 —— 勾上接收器的「VTS 服务端模式（本机 VB）」，见 §1.1.1。）
2. 图上放「HoFaceVTS接收器」：填 `手机 IPv4` → `Connect`。
   ⚠️ 轮询由**这个节点**驱动，它不在图里就收不到包（VTS 服务端模式同理：不在图里就没人应答 VB）。
3. 看接收器的**「状态」**这一个口（`来源=… · … · 本帧键 … · 帧 …/坏 … · 距上帧 …`）：
   * **键数在变** = 收通了（`原始值` 那张表也在随脸变）；
   * 收不到：先看 `状态` 里有没有"**⚠ 丢了 N 个包，最近来自 …**"那行（手机 IP 填错时会静默丢包）；
     `距上帧` 变大 = 断流，`状态` 里会写具体原因。
4. 「HoFace参数处理」：`输入新鲜` ← 接收器「新鲜」，`原始值` ← 接收器「原始值」（**输入口从上到下就是这个顺序**），
   `配置文件` 填 **`ho-debug-android.hoface.json`**（留空 = 这一层不做事，见 §1.1）。
5. 「HoFace控制求解」：`参数` ← 参数处理的「参数」，`有脸` ← 参数处理的「有脸」
   （**只接 `参数` 也行** —— `有脸` 默认 `true`）。
   * 想看输出与诊断：把两边的「状态」、以及控制求解的 `BlendShapes` / `Bone Rotations` 接到
     「Ho调试日志」的**「写入」**口 —— 上面那块只读文本就会跟着变，点一下**「复制」**整份进剪贴板
     （字典排成 `键 = 值`、数组排成 `[i] = (x, y, z, w)`）。要摊开整张**原始**表就把接收器的
     `原始值` 也接过去。
   * 不接也能在 `Player.log` 里看状态：参数处理每变一次写一行，另有点「重读配置」写进去的整份参数清单。
6. 下游把控制求解那 5 个输出口接到**官方三个应用节点**上 —— **这一步未实测**（§3.2）。

---

## 5. 工程约定与坑（别重踩）

1. **我们的数据一律不用 `JsonUtility`** —— 三次事故都是"编辑器里好好的、播放器里静默丢字段"
   （写 profile、读 profile、收 VTS 包丢 52 个形态键）。见 `Core/HoJson.cs:18-25` 的原始记录。
2. **行尾全 LF、`.cs` 无 BOM**：混用行尾 Unity 每次导入都报警告且行号不准；
   `.research/sync-modcore.ps1` 会强制（`Core/PORTED.md` §4）。
3. **`tools/compile-check.ps1` 必须保持纯 ASCII**：PowerShell 5.1 对无 BOM 的 `.ps1` 按 ANSI 读，
   一个中文字节就报 `意外的标记")"`。同一类坑见 HoUnityTools `docs/pitfalls/DOCS_ENCODING.md` §2.2。
4. **compile-check 的引用表**：`tools/compile-check.ps1:50-68`。里面几项（`AnimationModule` /
   `JSONSerializeModule` / `UniTask` / `UMod.dll` / `UMod-Interface.dll`）当年是**角色探针**在用
   （它碰 `CharacterAsset` 与 `Plugin.ModHost.SharedAssets`）；探针删掉之后**本 Mod 已经没人用它们了**，
   留着是因为"加节点时不用每次都改这张表"。真要收紧就删掉那几行再跑一次 —— 缺引用会报 `CS0234/CS0246`，
   不会静默出错。（`UnityEngine.IMGUIModule.dll` 为「Ho调试日志」的复制按钮（`GUIUtility.systemCopyBuffer`）
   重新加回来了 —— 它是整个 Managed 目录里**唯一**带剪贴板 API 的程序集。）
5. **端口规则**：`[DataInput]` = public 字段、`[DataOutput]` = public 方法、`[FlowInput]` 返回
   `Continuation`、`[FlowOutput]` 是 `Continuation` 字段、**`[Trigger(order)]` = 纯按钮**（不占任何口）。
   数据输入别叫 `Name`（撞 `Node` 基类，CS0108）；字段名 `Plugin` 也别用（撞 `Node.Plugin`）。
   ⚠️ 查官方怎么用某个特性：`warudo-knobs --find-attr <特性全名>` —— **要带 `Attribute` 后缀**
   （写 `Trigger` 会静默返回空，我曾据此写出过"Core 里没有 `[Trigger]`"的错结论）。
6. **沙箱列目录只能用 `GetFileEntries`**（`GetFiles` 会引入 `System.IO.SearchOption`，构建被拒）。
   **整个 `System.IO` 命名空间都被禁**（2026-09-25 实测：连 `System.IO.Path.GetFileName` 都毙，
   见 §1.1.2 末尾）；`System.Reflection` 整个命名空间同理，连 `value.GetType().Name`
   （→`MemberInfo::get_Name`）都算 —— 名单与现场在 HoUnityTools `docs/pitfalls/BUILD_AND_TOOLING.md`
   §4 / §4.1 / §4.3；`tools/compile-check.ps1` 的 `UMod sandbox lint` 就是拿这份名单在本地拦
   （**加新规则就往那里面加**）。
7. `[PluginType]` 的 **`NodeTypes` 必须列全**，漏掉的节点不会出现在面板里
   （`docs/打包与脚本规范.md` §3、§9）。
8. **Editor 专用脚本不在 `compile-check` 范围内**（它只编 `Mods/` 与 `Mods-Ho/` 里的运行时代码），
   所以 `tools/Editor/*.cs` 的 API 名写错只能等 Unity 报错。**省一轮的办法**：对着本机那套编辑器 DLL
   反射核一遍 —— `D:\Unity\Unity 2021.3.45f2\Editor\Data\Managed\UnityEditor.dll` 与
   `…\Managed\UnityEngine\UnityEngine.*Module.dll`（2026-09-25 就是这么抓到
   `BuildAssetBundleOptions.ForceRebuild` 应为 **`ForceRebuildAssetBundle`** 的；
   ⚠️ 核的时候用 `GetMember` 别用 `GetMethod` —— 重载上 `GetMethod` 会抛 `AmbiguousMatchException`，
   那样会把"存在"误判成"不存在"）。

---

## 6. 下一步

按权威路线图 `docs/FACE_TRACKING_WARUDO_ROUTE.md` §7 的待办推进；当前顺序是：

1. ~~清掉三个临时节点 + 收缩 `NodeTypes`~~ —— **2026-09-25 已做**（§1.1 / §3.1）。
2. `.controller` 随 mod 打包与 `Load<RuntimeAnimatorController>` 取回：实测（§3.2；探针已删，
   要验得先加一个临时观察窗）。
3. 这几个节点的实际连线：接收器 → 参数处理 → 控制求解 → 官方三个应用节点，在 Warudo 里跑通（§3.2）；
   顺手验 VTS 服务端模式（VB 的客户端列表里认不认我们）。
4. 之后才是拆成 `HoVtsTrack` / `HoVtsTrackController` 两个 Mod 与"中间层配置 + 控制器"那套内部实现。

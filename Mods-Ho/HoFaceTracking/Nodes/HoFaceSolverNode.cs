// HoFaceSolverNode.cs  --  控制求解（本 Mod 的"第三步"，2026-09-25 从「Ho Face 处理链」拆出来）
//
// 【它干的事】**从参数反求动画输出**：吃一份"参数名 → 值"，吐出 Warudo 官方那 5 个口要的形状：
//     Is Tracked / BlendShapes / Head Position / Root Position / Bone Rotations
// 于是下游不需要知道我们是谁 —— 直接接官方的
//     `Set Character Tracking BlendShapes` / `Override Character Bone Rotation Offsets` /
//     `Override Character Root Position`，**角色在那个节点上选**。
//
// 【零配置（红线）】这一层**不读任何配置文件**：映射（改名/量纲/曲线/平滑）全在「HoFace参数处理」那一层。
// 一旦往这里塞配置，"别的来源跳过参数处理直接喂这里"这条唯一的卖点就没了。
//
// 【接口（唯一约定，见 Core/HoFaceSolver.cs）】
//   键 = **裸规范名**（`JawOpen`…）+ 9 个保留名（`Head/RotX|Y|Z` 度、`Head/PosX|Y|Z` 米、`Root/PosX|Y|Z` 米）。
//   缺键 = 中性（0 / identity）。保留名以外的键原样进 `BlendShapes`。
//
// 【`有脸` 是输入，不是这里算的】判据依赖**协议里的裸线名**（`FaceFound` 等），
// 那是「HoFace参数处理」的知识；别的来源（VB 等）按自己的规矩给这个 bool。
// **不接线时默认 `true`** —— 因为 VB 那条路通常只接 `参数`，而官方那张图要靠 `Is Tracked` 才肯应用。
//
// 【为什么没有"影子 Animator"】见 `Core/HoFaceSolver.cs` 与 Unity 侧的控制器结构文档：
// 插件 Mod 不能读盘、不能带已编译资源，而 Unity 播放器无法从文件加载 AnimatorController。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法。
// ⚠️ 数据输入别叫 Name（撞 Node 基类成员，CS0108）。字段名 `Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using HoFaceTracking.Core;
using HoFaceTracking.PluginMod;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "7c3a91d6-4f2b-48e7-9a15-63d8f0b2c47e",   // 沿用旧「Ho Face 处理链」的 Id：指向官方三节点的线不会断
        Title = "HoFace控制求解",
        Category = "Ho Face Tracking")]
    public class HoFaceSolverNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>「HoFace参数处理」的 `参数` 接这儿（键 = 裸规范名 + 9 个保留名）。</summary>
        [DataInput(10)]
        [Label("参数")]
        public Dictionary<string, float> Parameters = new Dictionary<string, float>();

        /// <summary>
        /// 这一帧还算不算数。默认 **true**：不接线时按"一直有脸"处理
        /// （VB 那条路通常只接 `参数`；官方那张图要靠 `Is Tracked` 才肯应用）。
        /// </summary>
        [DataInput(20)]
        [Label("有脸")]
        public bool Tracked = true;

        /// <summary>
        /// **必填**：沙箱里的一个 **AssetBundle 文件名**（下拉列表选，列表 = 沙箱里的 `*.bundle`）。
        /// 里面装着「控制器 + 它绑定的那套 rig 预制体」——见 `Core/HoFaceController.cs`。
        ///
        /// ⚠️ **不填（或载入失败）这个节点不吐任何输出**：5 个口全中性 + `状态` 里写明原因。
        /// 为什么不给"没有控制器就直接把参数当结果端出去"的退路（2026-09-25 用户定）：
        /// 那样等于把量纲/曲线/名字的锅全甩给下游，而且**它不会报错** —— 画面看着像在工作。
        ///
        /// ⚠️ 为什么是 bundle 而不是 `.controller`：`.controller` 是**编辑器格式**，运行时读不了；
        /// 而且控制器的 clip 按层级路径绑定，运行时**枚举不了绑定**，所以 bundle 里必须带原配 rig。
        /// ⚠️ 为什么是**沙箱文件名**而不是绝对路径：和中间层配置同一个目录、同一套约定，
        /// 不用手填路径也不会放错文件夹（见 `README.md` §1.7：放错目录白查两轮）。
        /// </summary>
        [DataInput(30)]
        [Label("控制器")]
        [AutoComplete(nameof(AutoCompleteController), true, "")]
        public string ControllerFile = "";

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly HoFaceSolver solver = new HoFaceSolver();
        private readonly HoFaceController controller = new HoFaceController();
        private bool controllerActive;
        private int evaluatedFrame = -1;
        private string loggedState;

        /// <summary>
        /// 一帧只算一次，且**谁先读谁触发**（同参数处理节点：Warudo 没承诺节点之间的执行顺序，
        /// 所以不在 OnUpdate 里算，而是在输出端口里惰性求值）。
        ///
        /// **控制器是唯一的求值路径**（2026-09-25 用户定：没控制器就不准输出）：
        ///   · 在隐藏影子上跑那个控制器 —— **形状与骨骼**从代理上采；
        ///   · **头/根位置**仍由参数里的保留名装配（控制器多半只管表情与骨骼，位置继续走数据，
        ///     lipsync 类的控制器才不会把头部追踪弄没）。
        ///
        /// 载入失败（没选 / 沙箱里没这个文件 / 里面没有控制器或 rig）时 `controllerActive` 为 false，
        /// 5 个输出口**全部中性**（空字典 / identity / 零位），`状态` 里点名原因 —— **不用旧值兜底**。
        /// </summary>
        private void Ensure()
        {
            if (evaluatedFrame == Time.frameCount) return;
            evaluatedFrame = Time.frameCount;

            // 每帧接一次句柄：同一个就直接返回，插件被重建或节点先跑一帧都能自愈
            // （与「HoFace参数处理」里 `HoFaceProfileStore.Attach` 同样的做法）。
            var owner = this.Plugin as HoFaceTrackingPlugin;
            controllerActive = controller.Prepare(owner != null ? owner.Files : null, ControllerFile);

            // 只跑一条路：控制器负责形状与骨骼，`solver` 只负责**头/根位置**（参数里的保留名）。
            // 控制器没就绪就什么都不算 —— 输出口那边按 `controllerActive` 一律给中性值，
            // 所以这里不需要"算一遍中性"（更不需要旧值兜底：`solver` 的字段保持上一次的值也没关系，
            // 它们读不出来）。
            if (controllerActive)
            {
                controller.Solve(Parameters);
                solver.Solve(Parameters);
            }

            // 结构一变就写一行日志（**不含会每帧变的东西** —— 头姿那种每一帧都不一样，
            // 写进去日志就会被刷屏，接收器那边踩过这个坑：一份 Player.log 被刷掉一万五千行）。
            string state = LogState();
            if (state != loggedState)
            {
                loggedState = state;
                Debug.Log("[Ho 面捕] 控制求解 " + state);
            }
        }

        /// <summary>
        /// 下拉列表的数据源（`[AutoComplete]` 指到这儿）：沙箱里的 `*.bundle`。
        ///
        /// ⚠️ **签名必须是 `async UniTask&lt;AutoCompleteList&gt;`** —— Warudo 的注册器会检查返回类型，
        /// 不是 `UniTask&lt;AutoCompleteList&gt;` 就**整个节点注册失败**（2026-09-25 实测：面板上直接少两个节点，
        /// `Player.log` 里是 `Exception: Method …::AutoCompleteController does not return UniTask`1`
        /// → `Could not register node type …`）。我第一版就写成同步返回 `AutoCompleteList`，栽在这儿。
        /// （查证据时别只看返回类型名：官方那些"返回 `AutoCompleteList`"的样本其实是**字段**不是方法。）
        /// `AutoCompleteEntry.value` = 要填进字段的字符串（文件名，不是绝对路径）。
        /// </summary>
        public async UniTask<AutoCompleteList> AutoCompleteController()
        {
            var owner = this.Plugin as HoFaceTrackingPlugin;
            await UniTask.CompletedTask;
            return AutoCompleteList.Single(HoFaceController.SandboxBundles(owner != null ? owner.Files : null));
        }

        /// <summary>写进日志的那份状态：只放"结构"（控制器状态 / 参数键数 / 形状数 / 有脸），不放头姿。</summary>
        private string LogState()
        {
            return "参数 " + (Parameters != null ? Parameters.Count : 0) + " 个键"
                + "  ·  形状 " + ShapeCount() + " 个"
                + "  ·  有脸=" + (Tracked ? "是" : "否")
                + "  ·  控制器：" + controller.Status;
        }

        /// <summary>吐出去几个形状（没控制器时是 0）。</summary>
        private int ShapeCount()
        {
            return controllerActive ? controller.BlendShapes.Count : 0;
        }

        /// <summary>把控制器换掉（**同名文件被替换**时靠这个按钮重读）。</summary>
        [Trigger(200)]
        [Label("重读控制器")]
        [Description("丢掉已经载入的控制器，下一帧重新从沙箱读一次（同名文件被换过时用）。")]
        public void ReloadController()
        {
            controller.Dispose();
            evaluatedFrame = -1;
        }

        /// <summary>节点没了（或者图被关掉）就把影子与 bundle 放掉，别留垃圾。</summary>
        protected override void OnDestroy()
        {
            controller.Dispose();
        }

        // ── 输出：与官方取数节点同形的 5 个 ───────────────────────────────────────

        /// <summary>
        /// **丢追判定** —— "断流回中性"整条机制的开关，别按"收到包就算追到"写。
        /// 这里就是上游那个 `有脸` 原样传出（协议知识在参数处理那一层）。
        ///
        /// ⚠️ **与 `controllerActive` 相与**：没有可用的控制器时这一帧什么都不吐，
        /// 那 `Is Tracked` 也必须跟着假 —— 否则官方那张图会拿"全中性"当"追到了"去应用
        /// （头/根回零 = 角色突然弹回原姿势，比不应用更难看）。
        /// </summary>
        [DataOutput]
        [Label("Is Tracked")]
        public bool IsTracked()
        {
            Ensure();
            return Tracked && controllerActive;
        }

        /// <summary>
        /// 融合形状字典。键是**代理网格上的形状名**（= 控制器里那些 `blendShape.xxx`）。
        /// 没控制器就是**空字典**（不是"参数原样端出去"——那条退路 2026-09-25 砍了）。
        /// </summary>
        [DataOutput]
        [Label("BlendShapes")]
        public Dictionary<string, float> BlendShapes()
        {
            Ensure();
            // 给副本：端口的值会被下游一直拿着，接内部那个字典就成了活引用。
            var copy = new Dictionary<string, float>();
            if (!controllerActive) return copy;
            foreach (var pair in controller.BlendShapes) copy[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>头部位置（米）。来自保留名 <c>Head/PosX|PosY|PosZ</c>；没控制器时零。</summary>
        [DataOutput]
        [Label("Head Position")]
        public Vector3 HeadPosition()
        {
            Ensure();
            return controllerActive ? solver.HeadPosition : Vector3.zero;
        }

        /// <summary>根位置（米）。来自保留名 <c>Root/PosX|PosY|PosZ</c>；没控制器时零。</summary>
        [DataOutput]
        [Label("Root Position")]
        public Vector3 RootPosition()
        {
            Ensure();
            return controllerActive ? solver.RootPosition : Vector3.zero;
        }

        /// <summary>
        /// 骨骼旋转，按 <see cref="HumanBodyBones"/> 索引。
        /// **偏移语义**：单位四元数 = 不改那根骨头。
        /// 来自代理骨骼相对"控制器默认姿势"的偏移；没控制器时**全 identity**。
        /// </summary>
        [DataOutput]
        [Label("Bone Rotations")]
        public Quaternion[] BoneRotations()
        {
            Ensure();
            var source = controllerActive ? controller.BoneRotations : solver.BoneRotations;
            var copy = new Quaternion[source.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }

        // ── 输出：诊断（就这一条）────────────────────────────────────────────────

        /// <summary>
        /// 多行状态：收到几个参数、吐了几个形状、有脸没有、控制器怎么样。
        /// **控制器没就绪时这一段就是"为什么不吐"的说明书**（它同时也是唯一的报错出口 ——
        /// 这个节点没有 flow 口，没法"抛异常"，所以错误一律走这儿 + `Player.log`）。
        /// 想看得更细：把 `BlendShapes` / `Bone Rotations` 接到「Ho调试日志」（那边会把字典与数组摊开）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Ensure();

            string text = "参数 " + (Parameters != null ? Parameters.Count : 0) + " 个键"
                + "  ·  形状 " + ShapeCount() + " 个"
                + "  ·  有脸=" + (Tracked ? "是" : "否")
                + "  ·  头姿 " + EulerText();

            text += "\n控制器：" + controller.Status
                + (controllerActive ? "  ·  对上参数 " + controller.MatchedParameters + " 个" : "");

            // 没就绪时把"这一帧什么都没吐"说清楚（下游看到的是全中性）
            if (!controllerActive)
                text += "\n⚠ 没有可用的控制器 —— 这一帧 5 个输出口全是中性（BlendShapes 空 / 位置零 / 骨骼 identity），"
                    + "更下游不会被应用。修好上面那句再按「重读控制器」。";

            // 写进去的参数名 → 值。**这是"输入到位没有"的唯一证据**：
            // 形状恒 0 时靠它把"参数没写上（名字对不上）"与"控制器没把那格推到网格上"分开。
            if (controllerActive && !string.IsNullOrEmpty(controller.MatchedText))
                text += "\n写入 " + controller.MatchedText;

            // 载入时的一次性自检：代理网格上有哪些形状名 / 网格能不能被写 / 状态机在不在跑。
            // 形状恒 0 时的另外两条线索都在这儿（网格上没有那个形状名 = 写了也没人接）。
            if (controllerActive && !string.IsNullOrEmpty(controller.Report))
                text += "\n自检 " + controller.Report;

            return text;
        }

        /// <summary>头姿那三个欧拉角（度），一行看得见。</summary>
        private string EulerText()
        {
            Vector3 euler = solver.HeadRotation.eulerAngles;
            return "(" + euler.x.ToString("F1") + ", " + euler.y.ToString("F1") + ", " + euler.z.ToString("F1") + ")°";
        }
    }
}

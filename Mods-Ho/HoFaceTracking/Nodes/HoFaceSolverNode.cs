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
using HoFaceTracking.Core;
using UnityEngine;
using Warudo.Core.Attributes;
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
        /// **可选**：一个 **AssetBundle 文件**的路径，里面装着「控制器 + 它绑定的那套 rig 预制体」。
        /// 留空 = 走"纯装配"（今天的行为，也是 VB 那条路要的）。填了 = 控制器模式（见 `Core/HoFaceController.cs`）。
        ///
        /// ⚠️ 为什么是 bundle 而不是 `.controller`：`.controller` 是**编辑器格式**，运行时读不了；
        /// 而且控制器的 clip 按层级路径绑定，运行时**枚举不了绑定**，所以 bundle 里必须带原配 rig。
        /// </summary>
        [DataInput(30)]
        [Label("控制器（可选，AssetBundle 路径）")]
        public string ControllerPath = "";

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
        /// 两条路：
        ///   · `控制器` 留空 → 纯装配（`HoFaceSolver`）；
        ///   · `控制器` 填了 → 在隐藏影子上跑那个控制器，**形状与骨骼**从代理上采，
        ///     **头/根位置**仍由保留名装配（控制器多半只管表情与骨骼，位置继续走数据，
        ///     lipsync 类的控制器才不会把头部追踪弄没）。
        /// </summary>
        private void Ensure()
        {
            if (evaluatedFrame == Time.frameCount) return;
            evaluatedFrame = Time.frameCount;

            solver.Solve(Parameters);                       // 位置（以及控制器缺席时的形状/骨骼）
            controllerActive = controller.Prepare(ControllerPath);
            if (controllerActive) controller.Solve(Parameters);

            // 结构一变就写一行日志（**不含会每帧变的东西** —— 头姿那种每一帧都不一样，
            // 写进去日志就会被刷屏，接收器那边踩过这个坑：一份 Player.log 被刷掉一万五千行）。
            string state = LogState();
            if (state != loggedState)
            {
                loggedState = state;
                Debug.Log("[Ho 面捕] 控制求解 " + state);
            }
        }

        /// <summary>写进日志的那份状态：只放"结构"（控制器状态 / 参数键数 / 形状数 / 有脸），不放头姿。</summary>
        private string LogState()
        {
            return "参数 " + (Parameters != null ? Parameters.Count : 0) + " 个键"
                + "  ·  形状 " + (controllerActive ? controller.BlendShapes.Count : solver.BlendShapes.Count) + " 个"
                + "  ·  有脸=" + (Tracked ? "是" : "否")
                + (string.IsNullOrEmpty(ControllerPath) ? "" : "  ·  控制器：" + controller.Status);
        }

        /// <summary>把控制器换掉（同一路径下文件被替换时，靠这个按钮重读）。</summary>
        [Trigger(200)]
        [Label("重读控制器")]
        [Description("丢掉已经载入的控制器，下一帧按路径重新读一次。")]
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
        /// </summary>
        [DataOutput]
        [Label("Is Tracked")]
        public bool IsTracked()
        {
            return Tracked;
        }

        /// <summary>
        /// 融合形状字典。键是**规范名**（Warudo 认的就是这批）。
        /// 控制器模式下来自代理网格上的形状名；否则来自参数装配。
        /// </summary>
        [DataOutput]
        [Label("BlendShapes")]
        public Dictionary<string, float> BlendShapes()
        {
            Ensure();
            // 给副本：端口的值会被下游一直拿着，接内部那个字典就成了活引用。
            var source = controllerActive ? controller.BlendShapes : solver.BlendShapes;
            var copy = new Dictionary<string, float>();
            foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>头部位置（米）。来自保留名 <c>Head/PosX|PosY|PosZ</c>。</summary>
        [DataOutput]
        [Label("Head Position")]
        public Vector3 HeadPosition()
        {
            Ensure();
            return solver.HeadPosition;
        }

        /// <summary>根位置（米）。来自保留名 <c>Root/PosX|PosY|PosZ</c>。</summary>
        [DataOutput]
        [Label("Root Position")]
        public Vector3 RootPosition()
        {
            Ensure();
            return solver.RootPosition;
        }

        /// <summary>
        /// 骨骼旋转，按 <see cref="HumanBodyBones"/> 索引。
        /// **偏移语义**：单位四元数 = 不改那根骨头。
        /// 控制器模式下来自代理骨骼相对"控制器默认姿势"的偏移；否则来自参数里的保留名（只有 `Head` 不是 identity）。
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
        /// 一行状态：够定性就够了 —— 收到几个参数、凑出几个形状、有脸没有、控制器在不在。
        /// 想看得更细：把 `BlendShapes` / `Bone Rotations` 接到「Ho调试日志」（那边会把字典与数组摊开）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Ensure();

            string text = "参数 " + (Parameters != null ? Parameters.Count : 0) + " 个键"
                + "  ·  形状 " + (controllerActive ? controller.BlendShapes.Count : solver.BlendShapes.Count) + " 个"
                + "  ·  有脸=" + (Tracked ? "是" : "否")
                + "  ·  头姿 " + EulerText();

            if (!string.IsNullOrEmpty(ControllerPath))
                text += "\n控制器：" + controller.Status
                    + (controllerActive ? "  ·  对上参数 " + controller.MatchedParameters + " 个" : "");

            // 写进去的参数名 → 值。**这是"输入到位没有"的唯一证据**：
            // 形状恒 0 时靠它把"参数没写上（名字对不上）"与"控制器没把那格推到网格上"分开。
            if (controllerActive && !string.IsNullOrEmpty(controller.MatchedText))
                text += "\n写入 " + controller.MatchedText;

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

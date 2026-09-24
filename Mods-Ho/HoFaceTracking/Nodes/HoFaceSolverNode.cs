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

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly HoFaceSolver solver = new HoFaceSolver();
        private int evaluatedFrame = -1;

        /// <summary>
        /// 一帧只算一次，且**谁先读谁触发**（同参数处理节点：Warudo 没承诺节点之间的执行顺序，
        /// 所以不在 OnUpdate 里算，而是在输出端口里惰性求值）。
        /// </summary>
        private void Ensure()
        {
            if (evaluatedFrame == Time.frameCount) return;
            evaluatedFrame = Time.frameCount;
            solver.Solve(Parameters);
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
        /// 融合形状字典。键是**规范名**（Warudo 认的就是这批），值是参数处理算出来的结果。
        /// </summary>
        [DataOutput]
        [Label("BlendShapes")]
        public Dictionary<string, float> BlendShapes()
        {
            Ensure();
            // 给副本：端口的值会被下游一直拿着，接内部那个字典就成了活引用。
            var copy = new Dictionary<string, float>();
            foreach (var pair in solver.BlendShapes) copy[pair.Key] = pair.Value;
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
        /// **我们只有脸，所以只有 `Head` 这一格不是 identity**（其余等于"不改那根骨头"）。
        /// </summary>
        [DataOutput]
        [Label("Bone Rotations")]
        public Quaternion[] BoneRotations()
        {
            Ensure();
            var copy = new Quaternion[solver.BoneRotations.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = solver.BoneRotations[i];
            return copy;
        }

        // ── 输出：诊断（就这一条）────────────────────────────────────────────────

        /// <summary>
        /// 一行状态：够定性就够了 —— 收到几个参数、凑出几个形状、有脸没有。
        /// 想看得更细：把 `BlendShapes` / `Bone Rotations` 接到「Ho调试日志」（那边会把字典与数组摊开）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Ensure();
            return "参数 " + (Parameters != null ? Parameters.Count : 0) + " 个键"
                + "  ·  形状 " + solver.BlendShapes.Count + " 个"
                + "  ·  有脸=" + (Tracked ? "是" : "否")
                + "  ·  头姿 " + EulerText();
        }

        /// <summary>头姿那三个欧拉角（度），一行看得见。</summary>
        private string EulerText()
        {
            Vector3 euler = solver.HeadRotation.eulerAngles;
            return "(" + euler.x.ToString("F1") + ", " + euler.y.ToString("F1") + ", " + euler.z.ToString("F1") + ")°";
        }
    }
}

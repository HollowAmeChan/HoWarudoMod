// HoStringFloatMergeNode.cs  --  「HoStringFloatMerge」：两块"名字 → 浮点"的表，**下面的盖上面的**
//
// 【形状】（2026-09-27 用户定：就两个输入口，别做复杂）
//   `基础`(10)  ← 底表（典型接法：「HoFace参数处理」的 `参数`）
//   `覆盖`(20)  ← 盖在上面的表（典型接法：「HoStringFloat」手填的那份）
//   `字典`(输出) = 两张表并起来，**同名时下面的赢**
//   ⚠️ **没有"只补缺失"开关、也没有手填行**：手填是「HoStringFloat」的事（那才是通用件），
//      合并只干合并。少一个开关 = 少一种"看不出为什么这个值没生效"的可能。
//
// 【为什么需要有它】官方**没有**"两块 名字→浮点 表合并"的节点 —— 官方那一族列表节点只做单表操作
//   （空表 / 字面量 / 前缀 / 二值化 / 删除 / 平滑 / 开关），**合并只给了骨骼旋转与骨骼权重两族**
//   （`Merge Character Bone Rotation List` / `Merge Character Bone Weights`）。
//   而 `Dictionary<string,float>` 又是图里**唯一的字典类型**，官方 41 个字典口全是接过来的、
//   从不手填（2026-09-26 取证：官方场景 json 的 `typeKind` 统计 + 两套程序集全量反射，见
//   HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §3.5）。所以"合并"这件事只能自己做。
//
// 【典型用法：把控制器里恒 1 的门控关掉】
//   基础 ← 参数处理节点的「参数」
//   覆盖 ← 「HoStringFloat」里填一行：`Ho/Drive/Gate/Lip` = 0
//   字典 → 喂给「HoFace控制求解」的 `参数`（也可以再喂「HoFace写动态参数」）
//   控制器里 `Ho/Drive/Gate/Lip` 这种门控参数（float，`m_DefaultFloat: 1`）**中间层从来不写**，
//   它的值住在控制器参数上；把 0 并进字典之后，求解节点写参数时就会把 0 写进去 ⇒ 门控关掉，
//   而且**没有一个"第二个写者"**在旁边抢（覆盖是在同一次写入里改的值，不是另写一遍）。
//
// ⚠️ 三件必须知道的事：
//   ① **覆盖要每帧持续给值**。"这一帧字典里没有这个键" ≠ 回到默认值 —— Animator 参数会**保持上次写的值**
//      （控制器默认值只在 Animator 启动那一刻生效）。所以别用"缺席"表达 1，要 1 就写 1。
//   ② **名字必须在控制器参数表里**，否则会被**静默忽略**（写入那一步是按控制器声明的口逐个查字典的）。
//      「HoFace控制求解」的 `状态` 里会列出"对上"的前几个参数名，可以拿它核对。
//   ③ 两张输入表**都不会被改**，结果是第三份表；这份表**内部复用同一个实例**（跟官方
//      `OffsetBlendShapeNode.lastBlendShapes` 一个做法：`Clear()` + 重填 + 返回自己）——
//      所以别把它存下来跨帧看，要留一份就自己拷。
//
// ⚠️ **覆盖的是"参数值"，所以门控请只做一层**（2026-09-26 定的边界）：拿它关门控时，改的是控制器里那个
//   gate 参数，只有在**树里门控只有一层**时"这个参数值"才等于"树实际在用的有效门控"；门控在树里相乘 /
//   嵌套过的话，有效值只活在树里，Hub 与下游都拿不到真值（完整论述见 HoUnityTools
//   `docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md` §3.3）。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using UnityEngine;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "3f0c7d51-6a24-4f8b-9c02-8e1d5b7a64c3",
        Title = "HoStringFloatMerge",
        Category = "Ho Face Tracking")]
    public class HoStringFloatMergeNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>底表：先铺它（典型接法：参数处理节点的「参数」）。</summary>
        [DataInput(10)]
        [Label("基础")]
        public Dictionary<string, float> Base;

        /// <summary>盖在上面的表：**同名的键用它**（典型接法：「HoStringFloat」手填的那份）。</summary>
        [DataInput(20)]
        [Label("覆盖")]
        public Dictionary<string, float> Overlay;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> merged = new Dictionary<string, float>();
        private int addedLast;
        private int overwrittenLast;
        private int badKeysLast;
        private string loggedState;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 合并结果：`基础` 铺一遍，再用 `覆盖` 盖一遍（**同名时下面的赢**）。
        /// ⚠️ **返回的是内部复用的同一个实例**（每次原地重填，不每帧 new 一个字典 —— 官方
        /// `OffsetBlendShapeNode.lastBlendShapes` 也是这个做法）：别把它存下来跨帧看，要留一份就自己拷。
        /// </summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Merged()
        {
            merged.Clear();
            addedLast = 0;
            overwrittenLast = 0;
            badKeysLast = 0;

            CopyFrom(Base);
            CopyFrom(Overlay);

            LogOnce();
            return merged;
        }

        /// <summary>
        /// 状态：两张表各几个键、结果几个键、覆盖掉几个、空名字几个。
        /// 它是这个节点唯一的诊断出口（纯函数节点没有别的报错渠道），也顺手当"覆盖到底生效没有"的读数。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Merged();
            return "基础 " + (Base != null ? Base.Count : 0) + " 键"
                + "  ·  覆盖 " + (Overlay != null ? Overlay.Count : 0) + " 键"
                + "  ·  结果 " + merged.Count + " 键"
                + "（新增 " + addedLast + " / 覆盖 " + overwrittenLast + "）"
                + (badKeysLast > 0 ? "  ⚠️ " + badKeysLast + " 个键名字为空（已忽略）" : "");
        }

        // ── 实现 ────────────────────────────────────────────────────────────────

        private void CopyFrom(Dictionary<string, float> source)
        {
            if (source == null) return;
            foreach (var pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key)) { badKeysLast++; continue; }
                bool exists = merged.ContainsKey(pair.Key);
                merged[pair.Key] = pair.Value;
                if (exists) overwrittenLast++; else addedLast++;
            }
        }

        /// <summary>结构变了才写一行日志（键数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = (Base != null ? Base.Count : 0) + "/" + (Overlay != null ? Overlay.Count : 0)
                + "/" + merged.Count + "/" + addedLast + "/" + overwrittenLast + "/" + badKeysLast;
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho StringFloatMerge] 基础 " + (Base != null ? Base.Count : 0)
                + " · 覆盖 " + (Overlay != null ? Overlay.Count : 0)
                + " · 结果 " + merged.Count
                + "（新增 " + addedLast + " / 覆盖 " + overwrittenLast + "）");
        }
    }
}

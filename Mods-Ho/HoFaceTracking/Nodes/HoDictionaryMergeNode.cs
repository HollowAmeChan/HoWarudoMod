// HoDictionaryMergeNode.cs  --  「Ho合并字典」：把"名字 → 浮点"的表并起来，并支持面板手填的覆盖行
//
// 【为什么要有它】（2026-09-26 定）
// 面捕参数层的出口就是一份 `Dictionary<string,float>`；而"外部系统要改其中某几个值"这件事，
// Warudo 里**没有现成节点**：官方那一族列表节点只做单表操作（空表 / 字面量 / 前缀 / 二值化 /
// 删除 / 平滑 / 开关），**合并只给了骨骼旋转与骨骼权重两族**（`Merge Character Bone Rotation List`、
// `Merge Character Bone Weights`），没有"两块 名字→浮点 表合并"的。这个节点就是补那一个。
//
// 【⚠️ 语义必须说清：官方的同名类型 ≠ 同一件事】（2026-09-26 用户纠正）
//   官方那一族 `Dictionary<string,float>` 的口是 **BlendShape 语义**：键是**角色身上真实的形态键名**，
//   而且整族都在"角色形态键"这一条路上（`Override Character BlendShapes` / `Set Character Tracking
//   BlendShapes` / `Set/Get/Map/Offset/Scale/Remove/Trigger/Smooth/Binarize BlendShape…`）。
//   我们这个字典是 **任意动画参数名**（控制器参数口，float/bool/int 都由写入那一步按声明类型强转），
//   跟形态键**没有关系**。
//   **类型相同只说明"技术上插得上"，不代表能互相喂。** 别把我们的字典接到形态键节点上，
//   也别把形态键字典接到控制求解上 —— 图不拦你，人得拦住自己。
//
// 【手填这件事：字典口填不了，要填得用"元组行"】（2026-09-26 取证：官方场景 json + 全量反射）
//   官方场景 `DefaultScene.json` 里所有口的 `typeKind` 统计：
//     Value 222 · Enum 9 · Asset 11 · ValueArray 6 · StructuredData 14 · StructuredDataArray 9 · Reference 18
//   `Dictionary<string,float>` 落在 **Reference** 类，而且官方图里 **8 个字典口全是 null（一个都没填过）**
//   —— 官方从来不手填字典，字典一律是"接过来的"（`object` / `GameObject` 那些 Reference 口同样全空）。
//   官方能"手填一组一组东西"的只有两种形态：
//     · `ValueArray`：`Single[]` / `String[]`（例：`LiteralFloatListNode.Value`）
//     · `StructuredData` / `StructuredDataArray`：**一行一个对象、字段是 `[DataInput]`、面板上加/删行**
//       —— 这就是官方的"元组"机制，例：`ContactSource : StructuredData<OnContactNode>`（接触源行）、
//       `ParameterData : StructuredData<DefineFunctionNode>`、`BlendShapeEntryData`（名字 + 值 + 权重）。
//   全局 port 词汇表（两套程序集、约 120 种类型）里**没有** `Tuple` / `KeyValuePair`，也没有第二种
//   `Dictionary<,>` —— **元组就是 StructuredData 行，官方没有第二种。**
//   → 所以这里给了**两个覆盖入口**：`覆盖字典`（接过来的）与 `覆盖行`（**面板上直接填的元组行**）。
//
// 【典型用法：把控制器里恒 1 的门控关掉】
//   基础   ← 参数处理节点的「参数」
//   覆盖行 ← **不接线、面板上加一行**：`Ho/Drive/Gate/Lip` = 0
//   字典   → 喂给「HoFace控制求解」（接的是它的 `参数` 口），也可以再喂「写动态参数」
//   控制器里 `Ho/Drive/Gate/Lip` 这种门控参数（float，`m_DefaultFloat: 1`）**中间层从来不写**，
//   它的值住在控制器参数上；把 0 并进字典之后，求解节点写参数时就会把 0 写进去 ⇒ 门控关掉，
//   而且**没有一个"第二个写者"**在旁边抢（覆盖是在同一次写入里改的值，不是另写一遍）。
//   ⚠️ 覆盖的是**参数值**：只有**树里门控只有一层**时它才等于"树实际在用的有效门控" ——
//   门控在树里相乘 / 嵌套过的话，有效值只活在树里（见 HoUnityTools
//   docs/FACE_TRACKING_CONTROLLER_STRUCTURE.md §3.3）。
//
// 【合并顺序】基础 → 覆盖字典 → 覆盖行（后面的盖前面的；`覆盖行` 在最后 = 手填的意图最硬）。
//   `只补缺失` 开着时，三个来源里"已经存在的键"一律不覆盖（只补表里没有的键）。
//
// ⚠️ 三件必须知道的事：
//   ① **覆盖要每帧持续给值**。"这一帧字典里没有这个键" ≠ 回到默认值 —— Animator 参数会**保持上次写的值**
//      （控制器默认值只在 Animator 启动那一刻生效）。所以别用"缺席"表达 1，要 1 就写 1。
//   ② **名字必须在控制器参数表里**，否则会被**静默忽略**（写入那一步是按控制器声明的口逐个查字典的）。
//      「HoFace控制求解」的 `状态` 里会列出"对上"的前几个参数名，可以拿它核对。
//      顺带：**没有"删除键"这个动作**，也不需要 —— 想关就写 0。
//   ③ 输入表**不会被改**（`基础` / `覆盖字典` 原样不动），结果是第三份表。
//      这份表**内部复用同一个实例**（跟官方 `OffsetBlendShapeNode.lastBlendShapes` 一个做法：
//      `Clear()` + 重填 + 返回自己）—— 所以别把它存下来跨帧看，要留一份就自己拷。
//
// ⚠️ **跨仓验证项**：`覆盖行` 用的是 mod 自己定义的 `StructuredData<>` 行类型。类型注册是**懒加载**的
//   （`StructuredDataTypeRegistry.GetTypeMeta` 首次查询时 `RegisterType`，不需要全程序集扫描，见
//   `Warudo.Core.dll` 的 IL），所以理论上 mod 行类型跟官方行类型走同一条路；但"mod 节点面板能不能
//   正常渲染/增删这种行"**只能在 Warudo 里验证**（Unity 侧测不到）。验之前：`覆盖字典` 那条入口不受影响。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Graphs;
using UnityEngine;   // 只为 Debug.Log（状态变化时写一行）—— 这里没有渲染相关的用法

namespace HoFaceTracking.Nodes
{
    /// <summary>
    /// 一行覆盖：**名字 + 值**（这就是"元组"）。面板上加/删行，不用接线。
    /// 基类 <see cref="StructuredData{TParent}"/> 是官方表达"结构化的一行"的唯一机制（见文件头取证）。
    /// </summary>
    public class HoParameterOverride : StructuredData<HoDictionaryMergeNode>, ICollapsibleStructuredData
    {
        /// <summary>要覆盖的参数名（= 控制器参数名）。空名字的行会被忽略。</summary>
        [DataInput(10)]
        [Label("名字")]
        public string Key;

        /// <summary>覆盖成什么值（float/bool/int 参数都由写入那一步按控制器的声明类型强转）。</summary>
        [DataInput(20)]
        [Label("值")]
        public float Value;

        /// <summary>行标题（`ICollapsibleStructuredData`）：折叠时显示"名字 = 值"，免得只看到一串空行。</summary>
        public string GetHeader()
        {
            return string.IsNullOrEmpty(Key) ? "(空名字)" : Key + " = " + Value.ToString("0.###");
        }
    }

    [NodeType(
        Id = "3f0c7d51-6a24-4f8b-9c02-8e1d5b7a64c3",
        Title = "Ho合并字典",
        Category = "Ho General")]
    public class HoDictionaryMergeNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>底表：先铺它（典型接法：参数处理节点的「参数」）。</summary>
        [DataInput(10)]
        [Label("基础")]
        public Dictionary<string, float> Base;

        /// <summary>接过来的覆盖表（可选）。用于把另一份表（例如另一个合并节点的输出）并进来。</summary>
        [DataInput(20)]
        [Label("覆盖字典")]
        [Description("从别的节点接过来的覆盖表（可选）。**面板上填不了字典**（见文件头），要手填用「覆盖行」。")]
        public Dictionary<string, float> OverlayDict;

        /// <summary>面板上直接填的覆盖行（可选）。**这是这个节点真正的"手填"入口。**</summary>
        [DataInput(30)]
        [Label("覆盖行")]
        [Description("一行一个「名字 + 值」，在面板上加行即可，**不用接线**。"
            + "想关掉控制器里恒 1 的门控就填一行：名字 = 门控参数名，值 = 0。")]
        public HoParameterOverride[] OverlayRows;

        /// <summary>覆盖策略：开着 = 只补基础表里没有的键；关着 = 覆盖来源里的键一律盖掉已有的。</summary>
        [DataInput(40)]
        [Label("只补缺失")]
        [Description("开着：只把表里没有的键补进来（已有的一律不动）。"
            + "关着：覆盖里的键一律盖掉已有的 —— 想用一个值去关掉控制器里的门控，就用这个（默认）。")]
        public bool FillGapsOnly;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> merged = new Dictionary<string, float>();
        private int addedLast;
        private int overwrittenLast;
        private int skippedLast;
        private int rowsLast;
        private int badKeysLast;
        private string loggedState;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 合并结果。
        /// ⚠️ **返回的是内部复用的同一个实例**（每帧原地重填，不每帧 new 一个字典 —— 官方
        /// `OffsetBlendShapeNode.lastBlendShapes` 也是这个做法）——
        /// 所以别把它存下来跨帧看，也别拿它当长期容器。要留一份就自己拷。
        /// </summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Merged()
        {
            merged.Clear();
            addedLast = 0;
            overwrittenLast = 0;
            skippedLast = 0;
            rowsLast = 0;
            badKeysLast = 0;

            CopyFrom(Base);
            CopyFrom(OverlayDict);

            if (OverlayRows != null)
            {
                foreach (var row in OverlayRows)
                {
                    if (row == null) continue;
                    if (string.IsNullOrEmpty(row.Key)) { badKeysLast++; continue; }
                    Apply(row.Key, row.Value);
                    rowsLast++;
                }
            }

            LogOnce();
            return merged;
        }

        /// <summary>
        /// 状态：三个来源各几个键 / 这一帧新增、覆盖、跳过几个 / 空名字的行有几个。
        /// 它是这个节点唯一的诊断出口（纯函数节点没有别的报错渠道），也顺手当"覆盖到底生效没有"的读数。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Merged();
            var text = "基础 " + (Base != null ? Base.Count : 0) + " 键"
                + "  ·  覆盖字典 " + (OverlayDict != null ? OverlayDict.Count : 0) + " 键"
                + "  ·  覆盖行 " + rowsLast + " 行"
                + "  ·  结果 " + merged.Count + " 键"
                + "（新增 " + addedLast + " / 覆盖 " + overwrittenLast
                + (FillGapsOnly ? " / 跳过 " + skippedLast : "") + "）"
                + (badKeysLast > 0 ? "  ⚠️ " + badKeysLast + " 行名字为空（已忽略）" : "");
            return text + (FillGapsOnly ? "  ·  只补缺失" : "");
        }

        // ── 实现 ────────────────────────────────────────────────────────────────

        private void CopyFrom(Dictionary<string, float> source)
        {
            if (source == null) return;
            foreach (var pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key)) { badKeysLast++; continue; }
                Apply(pair.Key, pair.Value);
            }
        }

        private void Apply(string key, float value)
        {
            bool exists = merged.ContainsKey(key);
            if (exists && FillGapsOnly) { skippedLast++; return; }
            merged[key] = value;
            if (exists) overwrittenLast++; else addedLast++;
        }

        /// <summary>结构变了才写一行日志（键数/行数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = (Base != null ? Base.Count : 0) + "/" + (OverlayDict != null ? OverlayDict.Count : 0)
                + "/" + rowsLast + "/" + merged.Count + "/" + addedLast + "/" + overwrittenLast + "/" + skippedLast
                + "/" + (FillGapsOnly ? 1 : 0);
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho 合并字典] 基础 " + (Base != null ? Base.Count : 0)
                + " · 覆盖字典 " + (OverlayDict != null ? OverlayDict.Count : 0)
                + " · 覆盖行 " + rowsLast
                + " · 结果 " + merged.Count
                + "（新增 " + addedLast + " / 覆盖 " + overwrittenLast + " / 跳过 " + skippedLast + "）");
        }
    }
}

// HoStringFloatNode.cs  --  「HoStringFloat」：**一个 `(名字, 值)`** —— 也就是"元组"在图里的形态
//
// 【为什么它长这样】（2026-09-27 定）
// Warudo 的图里**没有元组端口类型**（全量 port 词汇表里没有 `Tuple`/`KeyValuePair`，
// 唯一的容器就是 `Dictionary<string,float>`；自定义 struct 当端口属未验证区，而且会丢掉
// "接线时就拦错"这条检查）。所以"一个元组"现实的表达就是**一份只含这一项的字典**：
//   `名字` + `值` → 字典（1 项）
// 它跟官方 `Empty BlendShape List` / `Literal Float` 那一类"常量节点"是一路的，只是内容是 `(名字, 值)`。
//
// 【家族】（面板里前缀一致，排在一起）
//   `HoStringFloat`        —— 一个 `(名字, 值)` → 1 项字典（**本文件**）
//   `HoStringFloatAppend`  —— 表 + 名字 + 值 → 追加后的表（一条一条拼）
//   `HoStringFloatMerge`   —— 两张表 → 一张（下面的盖上面的）
//   `HoStringFloatDict`    —— 面板手填多行 → 直接创建一张表
//   `HoBool2Float`         —— bool → float（官方缺这一个转换，见下）
//
// ⚠️ **坑**：名字留空 ⇒ 出来的是**空表**（不是"名字为空的那一项"）—— 字典的键不能是空串，
//   `状态` 里会明说。要拼表就用 `HoStringFloatAppend`，别把空名字接进来。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "39edc904-0493-4dd3-9d73-521aa66e0e50",
        Title = "HoStringFloat",
        Category = "Ho Face Tracking")]
    public class HoStringFloatNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>这一项的名字（= 目标字典的键）。**留空就是空表**。</summary>
        [DataInput(10)]
        [Label("名字")]
        public string Key;

        /// <summary>这个名字对应的值。</summary>
        [DataInput(20)]
        [Label("值")]
        public float Value;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> table = new Dictionary<string, float>();

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 只含这一项的字典（名字为空时是**空表**）。
        /// ⚠️ **返回的是内部复用的同一个实例**（官方 `OffsetBlendShapeNode.lastBlendShapes` 也是这个做法）：
        /// 别把它存下来跨帧看，要留一份就自己拷。
        /// </summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Table()
        {
            table.Clear();
            if (!string.IsNullOrEmpty(Key)) table[Key] = Value;
            return table;
        }

        /// <summary>状态：这一项是什么（或"名字为空 ⇒ 空表"）。</summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            return string.IsNullOrEmpty(Key)
                ? "⚠ 名字为空 ⇒ 输出的是空表（字典的键不能是空串）"
                : Key + " = " + Value.ToString("0.###");
        }
    }
}

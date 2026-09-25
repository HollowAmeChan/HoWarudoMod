// HoStringFloatProbeNode.cs  --  **探针**：Warudo 的图到底能不能搬 `KeyValuePair<string,float>`
//
// 【为什么要有它】（2026-09-27 用户指出我一直在规避这件事）
// 我之前的说法是"官方没有元组端口类型（全量 port 词汇表里没有 `Tuple`/`KeyValuePair`），
// 自定义类型当端口属未验证区"，于是设计了 `名字 + 值` 两个口 + `HoStringFloat*` 那一族。
// 但**那是从官方词汇表反推的，不是实测**：`KeyValuePair<string,float>` 这个**类型本身当然存在**，
// 而 Warudo 的端口类型是任意 `Type`（`DataInputPort.Type`），连接规则里"类型相等"就通过 ——
// 所以真正的问题只有一个：**Warudo 自己的序列化/面板能不能吃它**。这个只能测，不能推断。
//
// 【本节点怎么用（只为一件事：在 Warudo 里跑一遍，看它能不能注册/渲染/连线/存盘）】
//   输入 `元组`(KeyValuePair<string,float>) · `元组列表`(List<KeyValuePair<string,float>>)
//   输出 `元组`(KeyValuePair<string,float>) · `字典`(Dictionary<string,float>，由列表摊平而来) · `状态`
// 判据（在 Warudo 里边看边对）：
//   ① **节点在不在面板里** —— 注册期就要把字段的默认值序列化成字符串；序列化不了 ⇒ 整条 `NodeType`
//      注册失败 ⇒ 节点直接消失（跟 `[AutoComplete]` 签名不合规那条坑一模一样）；
//   ② **两个口画不画得出来**（面板上有没有可编辑的控件；`KeyValuePair` 是只读 struct，没有公开字段）；
//   ③ **能不能接别的节点**（同类口互接应当放行；接 `Dictionary<string,float>` 应当**被拦**）；
//   ④ **存盘/重载**（蓝图存下来再进来，值还在不在）。
// 结论无论哪边都值钱：能跑 ⇒ 家族改成真"`<string,float>` 元组口"（`HoStringFloatAppend` 接元组而不是两个口，
// Hub 也可能不用自定义 struct）；不能跑 ⇒ 这份实测就是"为什么只能两个口"的证据，写进文档。
//
// ⚠️ 这是**探针**：验完就该删（或者把结论转成正式形状）—— 别让它在正式蓝图里被依赖。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "621ce5f9-c30e-40f5-8bf4-160d410b5e2b",
        Title = "HoStringFloatProbe",
        Category = "Ho Face Tracking")]
    public class HoStringFloatProbeNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>一个 `(string, float)` 元组 —— 就是 `.NET` 的 `KeyValuePair<string,float>`。</summary>
        [DataInput(10)]
        [Label("元组")]
        public KeyValuePair<string, float> Pair;

        /// <summary>一串 `(string, float)` 元组（Hub 的存储形状就是它）。</summary>
        [DataInput(20)]
        [Label("元组列表")]
        public List<KeyValuePair<string, float>> Pairs;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> table = new Dictionary<string, float>();

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>把输入那个元组原样吐出去（用来测"能不能连到别的元组口"）。</summary>
        [DataOutput]
        [Label("元组")]
        public KeyValuePair<string, float> PairOut()
        {
            return Pair;
        }

        /// <summary>把 `元组列表` 摊成 `Dictionary<string,float>`（同名后写胜）—— 跟 Hub / 家族节点的接口对齐。</summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Dict()
        {
            table.Clear();
            if (Pairs != null)
                for (int i = 0; i < Pairs.Count; i++)
                {
                    string key = Pairs[i].Key;
                    if (!string.IsNullOrEmpty(key)) table[key] = Pairs[i].Value;
                }
            return table;
        }

        /// <summary>状态：两个口里现在是什么、摊平出几个键。</summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Dict();
            return "元组 = " + (string.IsNullOrEmpty(Pair.Key) ? "(空)" : Pair.Key + " / " + Pair.Value.ToString("0.###"))
                + "  ·  元组列表 " + (Pairs != null ? Pairs.Count : 0) + " 项"
                + "  ·  摊平 " + table.Count + " 键";
        }
    }
}

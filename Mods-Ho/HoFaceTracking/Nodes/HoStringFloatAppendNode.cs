// HoStringFloatAppendNode.cs  --  「HoStringFloatAppend」：把**一个键值对**并进一张表
//
// 【形状】`表`(Dictionary<string,float>) + `键值对`(`KeyValuePair<string,float>`) → `字典`（追加后的表）
//   —— 这就是用户说的"**拼成一个键值对，然后再转字典**"里的**后半步**：键值对进表，表就是字典。
//   （官方的同形节点是 `Float List Add Element` / `Boolean List Add Element`：`List` + 元素 → `Result`。
//     我们这里是"名字 → 浮点的表" + `(名字, 值)` 键值对 → 表。）
//
// 【典型链】布尔 → `HoBool2Float` → `HoStringFloat`（配名字）→ `HoStringFloatAppend`（并进表）
//   → 表喂给「HoStringFloatMerge」的 `覆盖` / 「HoFace控制求解」/「HoFace写动态参数」。
//
// ⚠️ 三条语义：
//   ① **输入表不会被改**：结果是另一份表（内部复用同一个实例，官方 `OffsetBlendShapeNode.lastBlendShapes`
//      也是这个做法）—— 别把它存下来跨帧看，要留一份就自己拷。
//   ② **同名时这一项赢**（append = 后写覆盖）。
//   ③ **键值对的名字为空**（`Key` 是空串/null）⇒ 这一项**不加**，原样返回输入表；`状态` 里会点名。
//
// 【端口类型】`KeyValuePair<string,float>` 能当图端口是 **2026-09-27 实测**的（探针节点在 Warudo 里
// 注册通过、端口画得出来、悬停提示写 `KeyValuePair<String, Float>`）—— 理由与取证见
// `HoStringFloatNode.cs` 的文件头。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;
using UnityEngine;   // 只为 Debug.Log（状态变了写一行）

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "2ba50601-566f-490e-934a-664165e1777f",
        Title = "HoStringFloatAppend",
        Category = "Ho Face Tracking")]
    public class HoStringFloatAppendNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>要并进哪张表（可选：不接就从空表起步）。</summary>
        [DataInput(10)]
        [Label("表")]
        public Dictionary<string, float> Table;

        /// <summary>要并进去的那个 `(名字, 值)` 键值对。名字为空 ⇒ 不加（原样返回输入表）。</summary>
        [DataInput(20)]
        [Label("键值对")]
        public KeyValuePair<string, float> Pair;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> result = new Dictionary<string, float>();
        private bool appendedLast;
        private bool overwroteLast;
        private string loggedState;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 并进去之后的表。⚠️ **返回的是内部复用的同一个实例**：别跨帧留着，要留一份就自己拷。
        /// </summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Appended()
        {
            result.Clear();
            appendedLast = false;
            overwroteLast = false;

            if (Table != null)
                foreach (var pair in Table)
                    if (!string.IsNullOrEmpty(pair.Key)) result[pair.Key] = pair.Value;

            if (!string.IsNullOrEmpty(Pair.Key))
            {
                bool exists = result.ContainsKey(Pair.Key);
                result[Pair.Key] = Pair.Value;
                appendedLast = true;
                overwroteLast = exists;
            }

            LogOnce();
            return result;
        }

        /// <summary>状态：表里几个键、这一项并进去了没有、是新增还是覆盖。</summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Appended();
            if (!appendedLast)
                return "表 " + result.Count + " 键  ·  ⚠ 键值对的名字为空 ⇒ 这一项没加（原样返回输入表）";
            return "表 " + result.Count + " 键  ·  " + Pair.Key + " = " + Pair.Value.ToString("0.###")
                + (overwroteLast ? "（覆盖了表里原有的值）" : "（新增）");
        }

        /// <summary>结构变了才写一行日志（键数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = (Table != null ? Table.Count : 0) + "/" + result.Count + "/"
                + (appendedLast ? 1 : 0) + "/" + (overwroteLast ? 1 : 0) + "/" + (Pair.Key ?? "");
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho StringFloatAppend] 表 " + (Table != null ? Table.Count : 0)
                + " → " + result.Count + " 键"
                + (appendedLast ? "  ·  " + Pair.Key + " = " + Pair.Value.ToString("0.###")
                    + (overwroteLast ? "（覆盖）" : "（新增）") : "  ·  键值对名字为空，没加"));
        }
    }
}

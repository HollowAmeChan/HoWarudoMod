// HoStringFloatAppendNode.cs  --  「HoStringFloatAppend」：给一张表**追加一项**
//
// 【形状】`表` + `名字` + `值` → `字典`（= 表 + 这一项；**同名时这一项赢**）
// 这是"一条一条把值拼起来"的那一步 —— 官方同形的节点是 `Float List Add Element` /
// `Boolean List Add Element` / `String List Add Element`（`List` + 元素 → `Result`），
// 我们只是把"List"换成"名字 → 浮点的表"，元素换成 `(名字, 值)` 这一对。
//
// 【为什么不用"元组口"】图里没有元组端口类型；"拼一个元组"现实的形态就是**两个口**（`名字` + `值`），
// 加在一张表上就是本节点（完整理由见 `HoStringFloatNode.cs` 的文件头与
// HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §3.5.1）。
//
// 【典型用法】`HoStringFloatMerge.字典 → 表`，逐个 `HoStringFloatAppend` 把几项加进去，
// 最后喂给控制求解 / 写动态参数；或者反过来：先 `HoStringFloatMerge` 叠覆盖表，再整体覆盖到中间层那份表上。
//
// ⚠️ 输入表**不会被改**（结果是另一份表，内部复用同一个实例）；名字留空 ⇒ **原样返回输入表**（`状态` 里说明）。
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

        /// <summary>要追加到哪张表（可选：不接就是空表起步）。</summary>
        [DataInput(10)]
        [Label("表")]
        public Dictionary<string, float> Table;

        /// <summary>这一项的名字（= 键）。**留空 = 什么都不加**（原样返回输入表）。</summary>
        [DataInput(20)]
        [Label("名字")]
        public string Key;

        /// <summary>这一项的值（同名时用它盖掉表里原来的值）。</summary>
        [DataInput(30)]
        [Label("值")]
        public float Value;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> result = new Dictionary<string, float>();
        private bool appendedLast;
        private bool overwroteLast;
        private string loggedState;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 追加后的表（名字为空时就是输入表的内容）。
        /// ⚠️ **返回的是内部复用的同一个实例**（官方 `OffsetBlendShapeNode.lastBlendShapes` 也是这个做法）：
        /// 别把它存下来跨帧看，要留一份就自己拷。
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

            if (!string.IsNullOrEmpty(Key))
            {
                bool exists = result.ContainsKey(Key);
                result[Key] = Value;
                appendedLast = true;
                overwroteLast = exists;
            }

            LogOnce();
            return result;
        }

        /// <summary>状态：表里几个键、这一项加进去了没有、是新增还是覆盖。</summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Appended();
            if (!appendedLast)
                return "表 " + result.Count + " 键  ·  ⚠ 名字为空 ⇒ 这一项没加（原样返回输入表）";
            return "表 " + result.Count + " 键  ·  " + Key + " = " + Value.ToString("0.###")
                + (overwroteLast ? "（覆盖了表里原有的值）" : "（新增）");
        }

        /// <summary>结构变了才写一行日志（键数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = (Table != null ? Table.Count : 0) + "/" + result.Count + "/"
                + (appendedLast ? 1 : 0) + "/" + (overwroteLast ? 1 : 0) + "/" + (Key ?? "");
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho StringFloatAppend] 表 " + (Table != null ? Table.Count : 0)
                + " → " + result.Count + " 键"
                + (appendedLast ? "  ·  " + Key + " = " + Value.ToString("0.###")
                    + (overwroteLast ? "（覆盖）" : "（新增）") : "  ·  名字为空，没加"));
        }
    }
}

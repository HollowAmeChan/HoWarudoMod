// HoFaceHubWriteNode.cs  --  「写动态参数」：把控制器算出来的语义槽写进**角色身上**的 Hub
//
// 【这条链的两端各在哪】
//   控制器（bundle 里跑）→ 它的曲线写进**影子**上的 `HoFaceSemanticHub`（只有值、没有名字）
//   → 「HoFace控制求解」的 `动态参数` 出口（我们按下标采出来，键是 `#0`/`#1`…）
//   → **本节点** → 写进**角色身上**那份 Hub（`Character/…/SemanticHub`）
// 影子那份是**代理**（只有值）；角色那份旁边还挂着 `HoFaceSemanticConnector`（名字表 + 指向 Hub）。
//
// 【为什么要有这个节点，而不是让求解节点直接写角色】
//   求解节点是纯函数式的"从参数反求输出"，它不认识角色（角色在官方那三个 apply 节点上选）。
//   写角色是**副作用**，必须由一个明确的节点承担 —— 这也符合"谁写、什么时候写"要看得见那条规矩。
//
// 【键怎么对上下标】
//   出口那份字典的键有两种形态：
//     · 控制器按**下标**采（影子 Hub 没有名字）⇒ 键是下标字符串（`#0` / `#1`）；
//     · 影子 Hub 旁边若也有 Connector ⇒ 键是语义名（`MouthOpen`）。
//   本节点两种都吃：`#<下标>` 直接用；其它按名字去**角色的 Connector 槽表**里查下标。
//   ⇒ 所以这条链**不靠名字**也能走通（下标是硬约定），名字只是给人看的与方便手接的。
//
// 【槽位数是"预留"的】
//   Hub 的 `values` 一开始就预留固定槽位（`HoFaceSemanticHub.DefaultSlotCount`），**不跟槽表耦合**。
//   ⚠️ 后果：下标越界时 `SetFloat` 什么都不做（而且控制器曲线越界写也不报错）——
//   所以本节点把"跳过几个"与"表里没声明的槽里有非零值"都**摆在状态口上**，不静默。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；[Trigger] = 纯按钮。
// ⚠️ 数据输入别叫 Name（撞 Node 基类成员，CS0108）。字段名 `Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using HoFaceTracking.Core;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "c47b1e05-8a92-4f6d-b3c1-7e5a9d20f68b",
        Title = "HoFace写动态参数",
        Category = "Ho Face Tracking")]
    public class HoFaceHubWriteNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>要写的角色。Connector（以及它指向的 Hub）挂在这个角色的子层级里（约定：`SemanticHub` 空物体）。</summary>
        [DataInput(10)]
        [Label("角色")]
        public GameObject Character;

        /// <summary>
        /// 语义槽：键 → 值。接「HoFace控制求解」的 `动态参数` 出口。
        /// 键可以是 `#<下标>`（硬约定，不靠名字）或语义名（按角色的槽表查下标）。
        /// </summary>
        [DataInput(20)]
        [Label("动态参数")]
        public Dictionary<string, float> Values = new Dictionary<string, float>();

        // ── 缓存 ────────────────────────────────────────────────────────────────

        /// <summary>找到的那个 Connector（**角色身上的**，名字表在它上面）。</summary>
        private HoFaceSemanticConnector connector;

        /// <summary>Connector 指向的那个 Hub（值在它上面）。Connector 没填 Hub 时是 null。</summary>
        private HoFaceSemanticHub hub;

        /// <summary>Connector 所在的对象。用它判断"角色换了没有"，也是状态行里报给用户的名字。</summary>
        private GameObject connectorOwner;

        private int writtenLastFrame;
        private int skippedLastFrame;

        /// <summary>本帧写进去的、**槽表没声明**的槽里有几个是非零的（见 <see cref="Status"/>）。</summary>
        private int outsideLastFrame;

        /// <summary>那几个下标的前几个（只报几个，够定位就行）。</summary>
        private readonly List<int> outsideSamples = new List<int>();

        private string loggedState;

        // ── 按钮 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 找一次角色上的 Connector（找不到时就报原因）。改完角色结构之后按一下，不用重启。
        /// </summary>
        [Trigger(200)]
        [Label("重找 Connector")]
        [Description("丢掉缓存，下一帧重新在角色层级里找一个 HoFaceSemanticConnector（以及它指向的 Hub）。")]
        public void Rebind()
        {
            connector = null;
            hub = null;
            connectorOwner = null;
            loggedState = null;
        }

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>本帧真写进去了几个槽。</summary>
        [DataOutput]
        [Label("写入数")]
        public int WrittenCount()
        {
            EnsureHub();
            return Apply();
        }

        /// <summary>
        /// 状态：找到 Connector 没有、Hub 有几个槽、槽表几项、本帧写了几个、跳过了几个。
        /// **它是唯一的报错出口**（这个节点没有 flow 口，没法抛异常）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            EnsureHub();

            if (Character == null)
                return "⚠ 没选角色 —— 动态参数写不进去（Connector 挂在角色的子层级里）。";

            if (connector == null)
                return "⚠ 这个角色上没有 HoFaceSemanticConnector"
                    + " —— 在角色 mod 里加一个空物体挂上它（约定叫 `SemanticHub`），"
                    + "并在它上面填好 Hub 与槽表。**本节点不会替你建**。";

            if (hub == null)
                return "⚠ Connector「" + connector.name + "」没填 Hub ⇒ 没有槽可以写"
                    + "（在 Connector 上把同一个物体上的 HoFaceSemanticHub 拖进去）。";

            int written = Apply();
            string text = "Connector：" + (connectorOwner != null ? connectorOwner.name : "?")
                + "  ·  Hub：" + hub.name + "（" + hub.SlotCount + " 个槽）"
                + "  ·  槽表 " + connector.Count + " 项"
                + "\n本帧：写入 " + written + " 个"
                + (skippedLastFrame > 0 ? "  ·  跳过 " + skippedLastFrame + " 个（键既不是 `#下标`、"
                    + "也没在槽表里找到；或者下标越界）" : "");

            if (connector.Count == 0)
            {
                // 空表 = 纯位置模式（只认 `#下标`）。这是合法用法，但要说出来 ——
                // 否则"名字对不上"会被误当成"表填错了"。
                text += "\nℹ 槽表是空的 ⇒ 只认 `#<下标>`（纯位置模式），没有名字可用。";
            }
            else if (outsideLastFrame > 0)
            {
                text += "\n⚠ 有 " + outsideLastFrame + " 个**非零**值落在槽表没声明的槽里（下标 "
                    + Join(outsideSamples) + "…）⇒ 控制器和槽表没对齐，那几个语义现在没人认领。";
            }

            if (hub.SlotCount == 0)
                text += "\n⚠ Hub 的 values 是空的（0 个槽）⇒ 什么都写不进去。";

            return text;
        }

        /// <summary>
        /// 节点没了就把缓存放掉（Hub / Connector 是别人的组件，不需要我们销毁）。
        /// </summary>
        protected override void OnDestroy()
        {
            connector = null;
            hub = null;
            connectorOwner = null;
        }

        // ── 干活 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 找角色上的 Connector 与它指向的 Hub。**只在缓存失效时找一次**（每帧 `GetComponentInChildren` 是白费）。
        /// 角色被换掉（引用变了）或 Connector 被删了，都会重新找。
        ///
        /// ⚠️ **找不到就是找不到，本节点绝不替你建一个。** 往用户的角色上自动加组件是"改他的东西"，
        /// 而且建出来的那个没有槽表、只有下标，只会让后面更难查。
        /// （Unity 侧调试面板同一条口径：只找不建。）
        /// </summary>
        private void EnsureHub()
        {
            if (Character == null) { connector = null; hub = null; connectorOwner = null; return; }

            // 缓存还有效吗：角色没换、Connector 还活着
            if (connector != null && connectorOwner != null && connectorOwner == Character)
            {
                hub = connector.hub;      // Hub 引用可能在编辑器里被改过，每帧顺手同步一次（不做搜索）
                return;
            }

            connector = Character.GetComponentInChildren<HoFaceSemanticConnector>(true);
            connectorOwner = connector != null ? connector.gameObject : null;
            hub = connector != null ? connector.hub : null;
        }

        /// <summary>
        /// 把 <see cref="Values"/> 写进 Hub。返回真写进去几个。
        /// **按下标写**（`SetFloat(int, float)`），不做任何反射。
        /// </summary>
        private int Apply()
        {
            skippedLastFrame = 0;
            outsideLastFrame = 0;
            outsideSamples.Clear();

            if (hub == null || Values == null || Values.Count == 0) { writtenLastFrame = 0; return 0; }

            int written = 0;
            foreach (var pair in Values)
            {
                int index = Resolve(pair.Key);
                if (index < 0) { skippedLastFrame++; continue; }

                hub.SetFloat(index, pair.Value);
                written++;

                // "槽表没声明、但控制器写了非零值" —— 唯一会静默出错的错配形态，必须报。
                // 只数**非零**：预留的槽里绝大多数恒为 0，全算进来会变成每帧刷屏。
                if (connector.Count > 0 && index >= connector.Count && pair.Value != 0f)
                {
                    outsideLastFrame++;
                    if (outsideSamples.Count < 4) outsideSamples.Add(index);
                }
            }

            writtenLastFrame = written;
            LogOnce();
            return written;
        }

        /// <summary>
        /// 键 → 下标。先认 `#<下标>`（硬约定，不靠名字），再拿角色的**槽表**按名字查。
        /// 两头都不中就返回 −1（**跳过**，不是写 0 —— "没声明这个槽"和"显式写 0"是两件事）。
        /// 越界的下标同样返回 −1（`SetFloat` 越界是静默的，所以要在这一层拦住并计数）。
        /// </summary>
        private int Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            if (key[0] == '#')
            {
                int index;
                if (int.TryParse(key.Substring(1), out index) && index >= 0 && index < hub.SlotCount) return index;
                return -1;
            }

            int byName = connector.IndexOf(key);
            return byName >= 0 && byName < hub.SlotCount ? byName : -1;
        }

        /// <summary>把几个下标拼成 `1、3、7`（空表时返回 `—`）。</summary>
        private string Join(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return "—";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < indices.Count; i++)
            {
                if (i > 0) text.Append('、');
                text.Append(indices[i]);
            }
            return text.ToString();
        }

        /// <summary>结构变了才写一行日志（键数/槽数/写入数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = "槽 " + (hub != null ? hub.SlotCount : 0) + " · 表 " + (connector != null ? connector.Count : 0)
                + " · 收到 " + (Values != null ? Values.Count : 0)
                + " · 写入 " + writtenLastFrame + " · 跳过 " + skippedLastFrame
                + " · 表外非零 " + outsideLastFrame;
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho 面捕] 写动态参数 " + state
                + (connector != null && connector.hub == null ? "  ·  ⚠ Connector 没填 Hub" : ""));
        }
    }
}

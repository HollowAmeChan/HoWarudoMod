// HoFaceHubWriteNode.cs  --  「写动态参数」：把**中间层算出来的参数**写进**角色身上**的 Hub
//
// 【这条链的两端各在哪】
//   「HoFace参数处理」的 `参数` 出口（或先过一遍「Ho合并字典」做覆盖）
//   → **本节点** → 写进**角色身上**那份 Hub（`Character/…/SemanticHub` 上那个 `HoFaceSemanticHub`）。
// 也就是说：**写的人就是中间层**（Unity 侧是调试会话写同一份东西：`HoFaceAnimationSession.PublishSemantics`），
// 控制器只负责"参数 → 形状 / 骨骼"，它不认识、也不再产出任何动态参数。
//
// ⚠️ **以前不是这样**（2026-09-26 清理）：那时候这条链是
//   控制器里的「语义写手」（状态机行为）→ 影子 rig 上的 Hub → 「HoFace控制求解」的 `动态参数` 口 → 本节点。
//   （那个"语义写手"是 2026-09-26 加的，当天就删了 —— 见下一条。）
//   删掉的理由：那些值**中间层本来就算得出来**（它就是写参数的那个人），让控制器再算一遍 = 两份真相 +
//   一个只在 bundle 里跑、编辑器里看不见的写者。于是求解节点那个 `动态参数` 出口也一起删了。
//
// 【为什么要有这个节点，而不是让求解节点直接写角色】
//   求解节点是纯函数式的"从参数反求输出"，它不认识角色（角色在官方那三个 apply 节点上选）。
//   写角色是**副作用**，必须由一个明确的节点承担 —— 这也符合"谁写、什么时候写"要看得见那条规矩。
//
// 【键怎么对上格 —— **没有表**】
//   名字**由写的人声明**：本节点按**名字**写（`HoFaceSemanticHub.SetFloat(名字, 值)` ——
//   已有那一格就复用、没有就当场开一格）。
//   名字就是中间层输出行的 `parameter`（`jawOpen` / `Ho/Drive/Lid/Left` …）。
//   ⇒ **没有"名字对不上"的校验点了**（表 2026-09-26 删掉）：名字敲错一个字符，本节点会安静地
//   新开一格。发现它的办法是看**新声明**那一行（下面 `状态` 里会点名），或者跑一遍看 Hub 里的名字。
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

        /// <summary>要写的角色。Hub 挂在这个角色的子层级里（约定：一个叫 `SemanticHub` 的空物体）。</summary>
        [DataInput(10)]
        [Label("角色")]
        public GameObject Character;

        /// <summary>
        /// 动态参数：键 → 值。接「HoFace参数处理」的 `参数`（或「Ho合并字典」的 `字典`）。
        /// 键就是**中间层输出行的 `parameter`**（`jawOpen` / `Ho/Drive/Lid/Left` …），按名字写。
        /// </summary>
        [DataInput(20)]
        [Label("动态参数")]
        public Dictionary<string, float> Values = new Dictionary<string, float>();

        // ── 缓存 ────────────────────────────────────────────────────────────────

        /// <summary>找到的那个 Hub（值在它上面；它也是**唯一的组件** —— Connector 2026-09-27 删了）。</summary>
        private HoFaceSemanticHub hub;

        /// <summary>Hub 所在的对象。用它判断"角色换了没有"，也是状态行里报给用户的名字。</summary>
        private GameObject hubOwner;

        private int writtenLastFrame;
        private int skippedLastFrame;

        /// <summary>本帧**新声明**了几个名字（= 角色那片 Hub 上刚开出来的格）。</summary>
        private int claimedLastFrame;

        /// <summary>那几个名字的前几个（这是"到底声明了什么名字"的唯一记录 —— 表删了之后就靠它）。</summary>
        private readonly List<string> claimedSamples = new List<string>();

        /// <summary>被跳过的键的前几个（空名字，写不进去）。</summary>
        private readonly List<string> skippedSamples = new List<string>();

        private string loggedState;

        // ── 按钮 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 找一次角色上的 Hub（找不到时就报原因）。改完角色结构之后按一下，不用重启。
        /// </summary>
        [Trigger(200)]
        [Label("重找 Hub")]
        [Description("丢掉缓存，下一帧重新在角色层级里找一个 HoFaceSemanticHub。")]
        public void Rebind()
        {
            hub = null;
            hubOwner = null;
            loggedState = null;
        }

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>本帧真写进去了几格。</summary>
        [DataOutput]
        [Label("写入数")]
        public int WrittenCount()
        {
            EnsureHub();
            return Apply();
        }

        /// <summary>
        /// 状态：找到 Hub 没有、有几格、本帧写了几个、跳过了几个、**新声明了哪几个名字**。
        /// **它是唯一的报错出口**（这个节点没有 flow 口，没法抛异常）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            EnsureHub();

            if (Character == null)
                return "⚠ 没选角色 —— 动态参数写不进去（Hub 挂在角色的子层级里）。";

            if (hub == null)
                return "⚠ 这个角色上没有 HoFaceSemanticHub"
                    + " —— 在角色 mod 里加一个空物体挂上它（约定叫 `SemanticHub`）。"
                    + "**本节点不会替你建**。";

            int written = Apply();
            string text = "Hub：" + (hubOwner != null ? hubOwner.name : "?") + " / " + hub.name
                + "（" + hub.Count + " 格）"
                + "\n本帧：写入 " + written + " 个"
                + (skippedLastFrame > 0 ? "  ·  跳过 " + skippedLastFrame + " 个（键是空的）" : "");

            if (claimedLastFrame > 0)
            {
                // 这是"控制器声明了什么名字"的唯一记录（表删掉之后没有别的对照物）。
                // 名字敲错一个字符时，这里会安静地多出一个新名字 —— 所以要点名。
                text += "\n⚠ 新声明 " + claimedLastFrame + " 个名字：" + Join(claimedSamples)
                    + (claimedLastFrame > claimedSamples.Count ? "…" : "")
                    + "（中间层输出行的名字，角色 Hub 上刚开出来的格）";
            }

            if (skippedLastFrame > 0 && skippedSamples.Count > 0)
                text += "\n⚠ 跳过的键里有：" + Join(skippedSamples)
                    + (skippedLastFrame > skippedSamples.Count ? "…" : "");

            if (hub.Count == 0)
                text += "\n⚠ 本帧一个名字都没收到 ⇒ 一格都没开出来。";

            return text;
        }

        /// <summary>
        /// 节点没了就把缓存放掉（Hub 是别人的组件，不需要我们销毁）。
        /// </summary>
        protected override void OnDestroy()
        {
            hub = null;
            hubOwner = null;
        }

        // ── 干活 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 找角色上的 Hub。**只在缓存失效时找一次**（每帧 `GetComponentInChildren` 是白费）。
        /// 角色被换掉（引用变了）或 Hub 被删了，都会重新找。
        ///
        /// ⚠️ **找不到就是找不到，本节点绝不替你建一个。** 往用户的角色上自动加组件是"改他的东西"，
        /// 而且建出来的那个也没有名字，只会让后面更难查。
        /// （Unity 侧调试面板同一条口径：只找不建。）
        /// </summary>
        private void EnsureHub()
        {
            if (Character == null) { hub = null; hubOwner = null; return; }

            // 缓存还有效吗：角色没换、Hub 还活着
            if (hub != null && hubOwner != null && hubOwner == Character) return;

            hub = Character.GetComponentInChildren<HoFaceSemanticHub>(true);
            hubOwner = hub != null ? hub.gameObject : null;
        }

        /// <summary>
        /// 把 <see cref="Values"/> 写进 Hub。返回真写进去几个。
        /// **按名字写**（`SetFloat(string, float)`：没有那一格就当场开一格），不做任何反射。
        /// </summary>
        private int Apply()
        {
            skippedLastFrame = 0;
            claimedLastFrame = 0;
            claimedSamples.Clear();
            skippedSamples.Clear();

            if (hub == null || Values == null || Values.Count == 0) { writtenLastFrame = 0; return 0; }

            int written = 0;
            foreach (var pair in Values)
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    // **跳过**，不是写 0 —— "没这个参数"和"显式写 0"是两件事。
                    skippedLastFrame++;
                    if (skippedSamples.Count < 4) skippedSamples.Add(pair.Key ?? "");
                    continue;
                }

                bool isNew = hub.IndexOf(pair.Key) < 0;      // 谁写谁开：名字由写的人声明
                if (!hub.SetFloat(pair.Key, pair.Value)) { skippedLastFrame++; continue; }
                if (isNew)
                {
                    claimedLastFrame++;
                    if (claimedSamples.Count < 6) claimedSamples.Add(pair.Key);
                }
                written++;
            }

            writtenLastFrame = written;
            LogOnce();
            return written;
        }

        /// <summary>把几个名字拼成 `MouthX、MouthY`。</summary>
        private string Join(List<string> names)
        {
            if (names == null || names.Count == 0) return "—";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) text.Append('、');
                text.Append(names[i]);
            }
            return text.ToString();
        }

        /// <summary>结构变了才写一行日志（键数/格数/写入数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = "格 " + (hub != null ? hub.Count : 0)
                + " · 收到 " + (Values != null ? Values.Count : 0)
                + " · 写入 " + writtenLastFrame + " · 跳过 " + skippedLastFrame
                + " · 新声明 " + claimedLastFrame;
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho 面捕] 写动态参数 " + state
                + (claimedLastFrame > 0 ? "  ·  新名字 " + Join(claimedSamples) : "")
                + (hub == null ? "  ·  ⚠ 角色上没有 HoFaceSemanticHub" : ""));
        }
    }
}

// HoFaceHubWriteNode.cs  --  「写动态参数」：把控制器算出来的语义槽写进**角色身上**的 Hub
//
// 【这条链的两端各在哪】
//   控制器（bundle 里跑）→ 它的曲线写进**影子**上的 `HoFaceSemanticHub`
//   → 「HoFace控制求解」的 `动态参数` 出口（我们按下标采出来）
//   → **本节点** → 写进**角色身上**那份 Hub（`Character/SemanticHub`）
// 影子那份是**代理**（只有值、没有资产）；角色那份是**正式**的（挂了动态参数资产、有名字）。
//
// 【为什么要有这个节点，而不是让求解节点直接写角色】
//   求解节点是纯函数式的"从参数反求输出"，它不认识角色（角色在官方那三个 apply 节点上选）。
//   写角色是**副作用**，必须由一个明确的节点承担 —— 这也符合"谁写、什么时候写"要看得见那条规矩。
//
// 【键怎么对上下标】
//   出口那份字典的键有两种形态：
//     · 影子 Hub **填了资产** ⇒ 键是语义名（`MouthX`）；
//     · 影子 Hub **没填资产**（通常如此，它只是代理）⇒ 键是下标字符串（`#0` / `#1`）。
//   本节点两种都吃：`#<下标>` 直接用；其它按名字去**目标 Hub 的资产**里查下标。
//   ⇒ 所以这条链**不靠名字**也能走通（下标是硬约定），名字只是给人看的与方便手接的。
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

        /// <summary>要写的角色。Hub 挂在这个角色的子层级里（约定：`SemanticHub` 空物体）。</summary>
        [DataInput(10)]
        [Label("角色")]
        public GameObject Character;

        /// <summary>
        /// 语义槽：键 → 值。接「HoFace控制求解」的 `动态参数` 出口。
        /// 键可以是 `#<下标>`（硬约定，不靠名字）或语义名（按 Hub 的资产查下标）。
        /// </summary>
        [DataInput(20)]
        [Label("动态参数")]
        public Dictionary<string, float> Values = new Dictionary<string, float>();

        /// <summary>
        /// 找不到 Hub 时要不要**自动加一个**。
        /// 默认关：往角色上自动加组件是"改用户的东西"，跟他没要求就动他的角色一样糟。
        /// 真需要时在节点上手动打开，并明确知道自己在改什么。
        /// </summary>
        [DataInput(30)]
        [Label("找不到就自动加 Hub")]
        public bool AutoAdd = false;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private HoFaceSemanticHub hub;
        private GameObject hubOwner;
        private int writtenLastFrame;
        private int skippedLastFrame;
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

        /// <summary>本帧真写进去了几个槽。</summary>
        [DataOutput]
        [Label("写入数")]
        public int WrittenCount()
        {
            EnsureHub();
            return Apply();
        }

        /// <summary>
        /// 状态：找到 Hub 没有、有几个槽、本帧写了几个、跳过了几个。
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
                    + (AutoAdd ? "（已打开自动加，下一帧会加一个 —— 但**没挂资产**，等于只有下标、没有名字）"
                               : " —— 在角色 mod 里加一个空物体挂上它（约定叫 `SemanticHub`），"
                                 + "或者打开上面的「找不到就自动加 Hub」");

            int written = Apply();
            string text = "Hub：" + (hubOwner != null ? hubOwner.name : "?")
                + "  ·  槽 " + hub.Count + " 个"
                + "  ·  资产 " + (hub.asset != null ? hub.asset.Summary() : "**没填**（只有下标，没有名字）")
                + "\n本帧：写入 " + written + " 个"
                + (skippedLastFrame > 0 ? "  ·  跳过 " + skippedLastFrame + " 个（键既不是 `#下标`、"
                    + "也没在资产里找到；或者下标越界）" : "");

            if (hub.asset == null)
                text += "\n⚠ Hub 没挂资产 ⇒ 只认 `#<下标>` 这种键；"
                    + "控制器的语义名对不上，会整批被跳过。给 Hub 填上动态参数资产才对得上名字。";

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
        /// </summary>
        private void EnsureHub()
        {
            if (Character == null) { hub = null; hubOwner = null; return; }

            // 缓存还有效吗：角色没换、Hub 还活着
            if (hub != null && hubOwner != null && hubOwner == Character) return;

            hub = Character.GetComponentInChildren<HoFaceSemanticHub>(true);
            hubOwner = hub != null ? hub.gameObject : null;

            if (hub == null && AutoAdd)
            {
                var owner = new GameObject("SemanticHub");
                owner.transform.SetParent(Character.transform, false);
                hub = owner.AddComponent<HoFaceSemanticHub>();
                hubOwner = owner;
                Debug.LogWarning("[Ho 面捕] 写动态参数：角色上没有 Hub，已自动加在 " + Character.name
                    + "/SemanticHub。⚠️ 它**没有挂动态参数资产**，所以只有下标、没有名字。");
            }

            if (hub != null) hub.AlignToAsset();   // 长度跟资产对齐（没资产就是空操作）
        }

        /// <summary>
        /// 把 <see cref="Values"/> 写进 Hub。返回真写进去几个。
        /// **按下标写**（`SetFloat(int, float)`），不做任何反射。
        /// </summary>
        private int Apply()
        {
            skippedLastFrame = 0;
            if (hub == null || Values == null || Values.Count == 0) { writtenLastFrame = 0; return 0; }

            int written = 0;
            foreach (var pair in Values)
            {
                int index = Resolve(pair.Key);
                if (index < 0) { skippedLastFrame++; continue; }
                hub.SetFloat(index, pair.Value);
                written++;
            }

            writtenLastFrame = written;
            LogOnce();
            return written;
        }

        /// <summary>
        /// 键 → 下标。先认 `#<下标>`（硬约定，不靠名字），再拿目标 Hub 的资产按名字查。
        /// 两头都不中就返回 −1（**跳过**，不是写 0 —— "没声明这个槽"和"显式写 0"是两件事）。
        /// </summary>
        private int Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            if (key[0] == '#')
            {
                int index;
                if (int.TryParse(key.Substring(1), out index) && index >= 0 && index < hub.Count) return index;
                return -1;
            }

            if (hub.asset == null) return -1;          // 没资产就没有名字可用
            int byName = hub.asset.IndexOf(key);
            return byName >= 0 && byName < hub.Count ? byName : -1;
        }

        /// <summary>结构变了才写一行日志（键数/槽数/写入数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = "槽 " + hub.Count + " · 收到 " + (Values != null ? Values.Count : 0)
                + " · 写入 " + writtenLastFrame + " · 跳过 " + skippedLastFrame;
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho 面捕] 写动态参数 " + state
                + (hub.asset != null ? "  ·  资产 " + hub.asset.Summary() : "  ·  ⚠ 没挂资产（只认 #下标）"));
        }
    }
}

// HoFaceParameterNode.cs  --  参数处理（本 Mod 的"第二步"，2026-09-25 从「Ho Face 处理链」拆出来）
//
// 【它干的事】把接收器交出来的**原始线名 + 原值**，按配置文件（`*.hoface.json`）算成**规范参数**：
//   · 输入行：`规范名 = 曲线(表达式(裸线名…))`
//   · 输出行：`参数名 = 曲线(表达式(规范名…))`（配置里写 `ARKit/xxx`，出口**去掉前缀**）
// 然后只交出一份字典（`参数`）和一个判断（`有脸`）。**它不负责装配** —— 那是「HoFace控制求解」的事。
//
// 【为什么拆】让别的来源**跳过这一层**：VB（走接收器的 VTS 服务端模式）、以后别的面捕源，
// 只要自己给得出"参数 + 有脸"，就能直接喂控制求解。两个节点之间只有一份字典这一条约定。
//
// 【`有脸` 为什么在这一层算】判据是"**新鲜** 且 手机报了 `FaceFound ≠ 0`" ——
// `FaceFound` 是**协议里的裸线名**，求解器只拿到规范名、根本看不到。所以协议知识留在这一层；
// 求解器那一层不碰它（见 `HoVtsPacket.FaceFoundKey` 的用法）。
//
// 【配置文件放哪】插件沙箱目录。面板上的 `状态` 端口直接给出路径；沙箱里一份都没有时，
// 插件会写一份内置默认（ho-2d-test1.hoface.json）当样板。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；[Trigger] = 纯按钮。
// ⚠️ 数据输入别叫 Name（撞 Node 基类成员，CS0108）。字段名 `Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using HoFaceTracking.Core;
using HoFaceTracking.PluginMod;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "a41d0c86-6f52-4b19-8d3a-5e2c71b904af",
        Title = "HoFace参数处理",
        Category = "Ho Face Tracking")]
    public class HoFaceParameterNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────
        //
        // 顺序用**显式 order**定死（不靠声明顺序的默契）：
        //   10 `原始值` → 20 `输入新鲜` → 30 `配置文件`
        // 2026-09-25 把前两个对调成这个顺序：与**接收器输出的顺序**（`原始值`(10) / `新鲜`(20)）对齐，
        // 连线时左右两列能对着看。

        /// <summary>接收器节点的「原始值」接这儿（线名 → 原值，没改名没换算）。</summary>
        [DataInput(10)]
        [Label("原始值")]
        public Dictionary<string, float> Raw = new Dictionary<string, float>();

        /// <summary>接收器节点的「新鲜」接这儿。断流时 <c>有脸</c> 就降下去。</summary>
        [DataInput(20)]
        [Label("输入新鲜")]
        public bool RawFresh;

        /// <summary>
        /// 沙箱里的配置文件名（例如 <c>ho-2d-test1.hoface.json</c>，子目录写相对路径）——
        /// **下拉列表里选**（`[AutoComplete]`，列出沙箱里的 `*.hoface.json`）。
        /// **留空 = 这一层不做事**（2026-09-25 起没有内置默认；见 `HoFaceProfileStore.Refresh` 的注释）。
        /// </summary>
        [DataInput(30)]
        [Label("配置文件")]
        [AutoComplete(nameof(AutoCompleteProfile), true, "")]
        public string ProfileFile = "";

        // ── 状态 ────────────────────────────────────────────────────────────────

        private HoFaceChain chain;
        private string chainKey;
        private int evaluatedFrame = -1;
        private string profileNote;

        /// <summary>上一次已经打进日志的那行状态 —— 只在**变了**的时候才写日志，免得每帧刷屏。</summary>
        private string loggedState;

        /// <summary>
        /// 一帧只算一次，且**谁先读谁触发**。
        ///
        /// 为什么不在 OnUpdate 里算：节点之间谁先跑、端口什么时候被灌进来，Warudo 没承诺顺序。
        /// 放在输出端口里惰性求值，就保证"读到的一定是这一帧输入算出来的"，不需要赌顺序。
        ///
        /// ⚠️ 所以这个节点**没有 flow 输入** —— 没有"触发"这回事。
        /// 想手动催一下就用节点上的「重读配置」按钮，或者接 `Enter` flow。
        /// </summary>
        private void Ensure()
        {
            if (evaluatedFrame == Time.frameCount) return;
            evaluatedFrame = Time.frameCount;
            Resolve();
        }

        private void Resolve()
        {
            // 每帧接一次句柄：同一个就直接返回，插件被重建或节点先跑一帧都能自愈。
            var owner = this.Plugin as HoFaceTrackingPlugin;
            HoFaceProfileStore.Attach(owner != null ? owner.Files : null);

            HoFaceMiddleware middleware = null;
            string key = null;
            profileNote = null;

            if (string.IsNullOrEmpty(ProfileFile))
            {
                // **没有内置默认**（2026-09-25 改）。以前留空会用内置默认表（还是过时的 iFacialMocap 那套），
                // "悄悄拿一份你没指定的配置去算"本身就是坑；现在留空 = 这一层不做事。
                profileNote = "没填配置文件 —— 这一层不做事（填一份，或者用别的来源直接喂「HoFace控制求解」）。";
            }
            else
            {
                long stamp;
                string error;
                if (HoFaceProfileStore.TryGet(ProfileFile, out middleware, out stamp, out error))
                {
                    key = ProfileFile + "#" + stamp;
                }
                else
                {
                    middleware = null;
                    profileNote = error;
                }
            }

            if (middleware != null)
            {
                // 配置换了（文件名或时间戳变了）才重新编译：表达式只解析一次。
                if (key != chainKey)
                {
                    chain = new HoFaceChain(middleware);
                    chainKey = key;
                }
            }
            else
            {
                // ⚠️ **读不到 / 没填就把链清掉**。以前这里只改 `profileNote`、保留上一次编译好的那份，
                // 于是"把配置文件删了、点重读，参数还在照旧输出"—— 用户实测到的就是这个（极难查）。
                chain = null;
                chainKey = null;
            }

            if (chain != null)
                chain.Evaluate(Raw, Mathf.Max(0f, Time.deltaTime), Time.realtimeSinceStartup);

            // 状态一变就写一行日志。这条是给"面板上连不出线/看不到值"准备的：
            // 结果直接进 Warudo 的 Player.log，不用接任何调试节点。
            string state = StateText();
            if (state != loggedState)
            {
                loggedState = state;
                Debug.Log("[Ho 面捕] 参数处理 " + state
                    + (chain != null && chain.Error != null ? "\n  ⚠ 配置问题：" + chain.Error : ""));
            }

        }

        /// <summary>拼状态行（**不触发求值** —— 供 <see cref="Resolve"/> 内部与日志用）。</summary>
        private string StateText()
        {
            if (chain == null) return "没有可用的配置：" + (profileNote ?? "（未知原因）");
            return chainKey + "  ·  输入行 " + chain.InputRowCount + " / 输出行 " + chain.OutputRowCount
                + "  ·  原始键 " + (Raw != null ? Raw.Count : 0)
                + (RawFresh ? "（新鲜）" : "（不新鲜）")
                + (profileNote != null ? "  ·  " + profileNote : "");
        }

        // ── 手动催一下（这个节点没有 flow 触发，所以给个按钮）──────────────────────

        /// <summary>
        /// `配置文件` 那个下拉列表的数据源：沙箱里的 `*.hoface.json`。
        /// 与「HoFace控制求解」的 `控制器` 下拉同一个套路（`AutoCompleteList.Single`，
        /// `value` = 要填进字段的**相对路径**，不是绝对路径）。
        /// </summary>
        public AutoCompleteList AutoCompleteProfile()
        {
            var entries = new List<AutoCompleteEntry>();

            // 列目录的前提是句柄已接上；没接上就先接一次（这里不依赖 Ensure 跑过）
            var owner = this.Plugin as HoFaceTrackingPlugin;
            HoFaceProfileStore.Attach(owner != null ? owner.Files : null);

            var found = HoFaceProfileStore.Entries;
            if (found != null)
                foreach (var entry in found)
                    if (entry != null && !string.IsNullOrEmpty(entry.relativePath))
                        entries.Add(new AutoCompleteEntry { label = entry.fileName, value = entry.relativePath });

            if (entries.Count == 0) entries.Add(new AutoCompleteEntry { label = "（沙箱里没有 *.hoface.json）", value = "" });
            return AutoCompleteList.Single(entries);
        }

        /// <summary>
        /// 强制重新列沙箱目录、重新读配置、重新编译链。用于"刚往沙箱里丢了新文件"或
        /// "改完文件想立刻看到"。
        /// </summary>
        [FlowInput]
        [Label("重读配置")]
        public Continuation Enter()
        {
            HoFaceProfileStore.Refresh();
            chainKey = null;      // 清掉 key 强制重编译
            evaluatedFrame = -1;
            Ensure();
            Debug.Log("[Ho 面捕] 参数处理 重读：" + StateText() + "\n" + PreviewText());
            return Exit;
        }

        [FlowOutput]
        public Continuation Exit;

        /// <summary>与 <see cref="Enter"/> 同一个动作，但在节点上是一个**按钮**。</summary>
        [Trigger(200)]
        [Label("重读配置")]
        [Description("重新列沙箱目录 + 重读配置文件 + 重编译链，并把状态写进日志。")]
        public void ReloadNow()
        {
            Enter();
        }

        // ── 输出：两份，正好是"参数 + 有脸"这一条约定 ─────────────────────────────

        /// <summary>
        /// **算出来的参数**（列表语义，单独一个口）：键已去掉 `ARKit/` 前缀，保留名（`Head/RotX`…）原样。
        /// 这就是交给「HoFace控制求解」的东西 —— 别的来源（VB 等）只要能给出同样形状的字典，就能跳过本节点。
        /// </summary>
        [DataOutput]
        [Label("参数")]
        public Dictionary<string, float> Parameters()
        {
            Ensure();
            // 给副本：端口的值会被下游一直拿着，接内部那个字典就成了活引用。
            var copy = new Dictionary<string, float>();
            if (chain != null)
                foreach (var pair in chain.Parameters) copy[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>
        /// **丢追判定** —— "断流回中性"整条机制的开关，别按"收到包就算追到"写。
        ///
        /// 实测过的一次踩坑：手机丢追时**仍然照发那 15 个标量**（Rotation/Position/Eye*/FaceFound/Hotkey/Timestamp），
        /// 只是不再发 `BlendShapes`（本帧键 65 → 15）。所以 `Raw.Count > 0` 这种写法在丢追时**依然是 true** ——
        /// 而官方那张图正是靠 `IsTracked` 走 `SWITCH_*` + `1 - IsTracked` 权重淡到中性的。
        /// 所以判据是 **`FaceFound` 那条线名**（来源明确告诉你找到脸没有）；协议里没有这个键时才退回"有键就算追到"。
        ///
        /// ⚠️ 这一层是**协议知识**（`FaceFound` 是裸线名），所以留在这里；求解器只认规范名，看不到它。
        /// </summary>
        [DataOutput]
        [Label("有脸")]
        public bool Tracked()
        {
            Ensure();
            if (Raw == null || Raw.Count == 0) return false;
            if (!RawFresh) return false;

            float faceFound;
            if (Raw.TryGetValue(HoVtsPacket.FaceFoundKey, out faceFound))
                return faceFound != 0f;      // 协议报了"找到脸没有"，以它为准

            return true;                     // 协议没有这个键：退回"有键就算追到"
        }

        // ── 输出：诊断（合并成一条）──────────────────────────────────────────────

        /// <summary>
        /// 四行状态：配置 + 输入输出行数 + 原始键；问题；沙箱目录；沙箱里现成的配置。
        /// 每一行都对应一件"必须知道才能往下走"的事（放文件放哪儿 / 为什么没生效 / 有哪几份可选）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Ensure();

            var text = new System.Text.StringBuilder();
            text.Append(StateText());
            text.Append("\n问题：").Append(ProblemsText());
            text.Append("\n沙箱：").Append(SandboxText());
            text.Append("\n可用配置：").Append(ProfileListText());
            return text.ToString();
        }

        /// <summary>配置文件该放哪儿 —— 直接把这个路径当答案，不要去猜 Warudo 的目录结构。</summary>
        private static string SandboxText()
        {
            if (!HoFaceProfileStore.Ready) return "（插件的沙箱还没就绪）";
            return HoFaceProfileStore.Root + (HoFaceProfileStore.Error != null ? "  ⚠ " + HoFaceProfileStore.Error : "");
        }

        /// <summary>沙箱里现成有哪几份（放进去新文件后按「重读配置」）。一行逗号分隔，读起来不占地方。</summary>
        private static string ProfileListText()
        {
            var entries = HoFaceProfileStore.Entries;
            if (entries.Count == 0)
                return HoFaceProfileStore.Error != null ? "（列不出来：" + HoFaceProfileStore.Error + "）" : "（沙箱里一份都没有）";

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) text.Append("、");
                text.Append(entries[i].relativePath);
            }
            return text.ToString();
        }

        /// <summary>配置本身的问题（表达式写错、修饰符 kind 不认得）。</summary>
        private string ProblemsText()
        {
            if (chain == null) return profileNote ?? "没有可用的配置。";
            return chain.Error ?? "没有。";
        }

        /// <summary>
        /// **用文本摊开数值** —— 不依赖 Warudo 面板怎么渲染 `Quaternion` / `Vector3`。
        /// 它不再是一个常驻端口（口太多），而是"按「重读配置」按钮时写进 `Player.log`"。
        /// </summary>
        private string PreviewText()
        {
            if (chain == null) return "（没有链）";

            var text = new System.Text.StringBuilder();

            // 丢追这件事必须摆在最前面：手机丢追时照样发那 15 个标量，只有 BlendShapes 没了
            // （本帧键 65 → 15），所以光看"有没有数据"是看不出丢追的。
            float faceFound = 0f;
            bool reported = Raw != null && Raw.TryGetValue(HoVtsPacket.FaceFoundKey, out faceFound);
            text.Append("有脸 = ").Append(Tracked());
            text.Append("   FaceFound = ").Append(reported ? faceFound.ToString("F0") : "（协议没报）");
            text.Append("   原始键 = ").Append(Raw != null ? Raw.Count : 0).Append('\n');

            text.Append("参数（输出行那份字典，按名字排序）：\n");
            var keys = new List<string>(chain.Parameters.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            if (keys.Count == 0) text.Append("  （一个都没有 —— 原始输入没接、或者配置里一行输出都没有）\n");
            for (int i = 0; i < keys.Count; i++)
            {
                text.Append("  ").Append(keys[i]).Append(" = ")
                    .Append(chain.Parameters[keys[i]].ToString("F4")).Append('\n');
                if (i >= 63) { text.Append("  …（还有 ").Append(keys.Count - i - 1).Append(" 个）\n"); break; }
            }

            return text.ToString();
        }
    }
}

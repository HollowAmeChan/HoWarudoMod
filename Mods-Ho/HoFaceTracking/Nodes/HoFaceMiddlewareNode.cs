// HoFaceMiddlewareNode.cs  --  处理链节点（本 Mod 的"第二步"）
//
// 【它干的事】
// 把接收器交出来的**原始线名 + 原值**，按配置文件（`*.hoface.json`）转成 Warudo 认的追踪数据形状。
// 输出的 5 个端口**与官方/社区接收器 Mod 的取数节点同形**：
//     IsTracked / BlendShapes / HeadPosition / RootPosition / BoneRotations
// 于是下游不需要知道我们是谁 —— 直接接官方的
//     `Set Character Tracking BlendShapes` / `Override Character Bone Rotation Offsets` /
//     `Override Character Root Position`，**角色在那个节点上选**。
//
// 【为什么没有"影子 Animator"】
// 原来打算内部跑一个隐藏 Animator 吃混合树 .controller。走不通：
// 插件 Mod 既不能读盘（无 System.IO）也不能带已编译资源，而 Unity 播放器**无法从文件
// 加载 AnimatorController**（只有 AssetBundle 能）。所以混合树改成**数据**，由
// Core/HoFaceChain.cs 求值 —— 而它跑的是**从 HoUnityTools 逐字搬来**的那份中间层代码，
// 所以面板里看到什么，Warudo 里就是什么（见 Core/PORTED.md）。
//
// 【配置文件放哪】
// 插件沙箱目录。面板上的「沙箱目录」端口直接给出路径；沙箱里一份都没有时，插件会写一份
// 内置默认（ho-2d-test1.hoface.json）当样板。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法。
// ⚠️ 数据输入别叫 Name（撞 Node 基类成员，CS0108）。字段名 `Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using HoFaceTracking.Core;
using HoFaceTracking.PluginMod;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "7c3a91d6-4f2b-48e7-9a15-63d8f0b2c47e",
        Title = "Ho Face 处理链",
        Category = "Ho Face Tracking")]
    public class HoFaceMiddlewareNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────
        //
        // 顺序用**显式 order**定死（不靠声明顺序的默契）：
        //   10 `输入新鲜` → 20 `原始值` → 30 `配置文件`
        // 2026-09-25 把前两个对调过：`输入新鲜` 是"这一帧还算不算数"的总闸，摆前面；
        // `原始值` 是数据本体，紧跟其后。

        /// <summary>接收器节点的「新鲜」接这儿。断流时 <c>IsTracked</c> 就降下去。</summary>
        [DataInput(10)]
        [Label("输入新鲜")]
        public bool RawFresh;

        /// <summary>接收器节点的「原始值」接这儿（线名 → 原值，没改名没换算）。</summary>
        [DataInput(20)]
        [Label("原始值")]
        public Dictionary<string, float> Raw = new Dictionary<string, float>();

        /// <summary>
        /// 沙箱里的配置文件名（例如 <c>ho-2d-test1.hoface.json</c>，子目录写相对路径）。
        /// **留空 = 用内置默认**（两份内置协议各一套输入行 + 52 个 ARKit 直通）。
        /// </summary>
        [DataInput(30)]
        [Label("配置文件")]
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

            HoFaceMiddleware middleware;
            string key;
            profileNote = null;

            if (string.IsNullOrEmpty(ProfileFile))
            {
                middleware = HoFaceMiddlewareDefaults.Create();
                key = "<内置默认>";
                profileNote = "没填配置文件，用的是内置默认（两种内置协议的输入行 + 52 个 ARKit 直通）。";
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
                    key = "<读不到>";
                    profileNote = error;
                }
            }

            // 配置换了（文件名或时间戳变了）才重新编译：表达式只解析一次。
            if (middleware != null && key != chainKey)
            {
                chain = new HoFaceChain(middleware);
                chainKey = key;
            }

            if (chain != null)
                chain.Evaluate(Raw, Mathf.Max(0f, Time.deltaTime), Time.realtimeSinceStartup);

            // 状态一变就写一行日志。这条是给"面板上连不出线/看不到值"准备的：
            // 结果直接进 Warudo 的 Player.log，不用接任何调试节点。
            string state = StateText();
            if (state != loggedState)
            {
                loggedState = state;
                Debug.Log("[Ho 面捕] 处理链 " + state
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
            Debug.Log("[Ho 面捕] 处理链 重读：" + StateText() + "\n" + PreviewText());
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

        // ── 输出：与官方接收器取数节点同形的 5 个 ─────────────────────────────────

        /// <summary>
        /// **丢追判定** —— 这个口是"断流回中性"整条机制的开关，别按"收到包就算追到"写。
        ///
        /// 实测过的一次踩坑：手机丢追时**仍然照发那 15 个标量**（Rotation/Position/Eye*/FaceFound/Hotkey/Timestamp），
        /// 只是不再发 `BlendShapes`（本帧键 65 → 15）。所以
        /// `Raw.Count &gt; 0` 这种写法在丢追时**依然是 true** —— 而官方那张图正是靠 `IsTracked`
        /// 走 `SWITCH_*` + `1 - IsTracked` 权重淡到中性的。它一直是 true，就等于"脸没了，
        /// 表情冻在最后一帧，权重还以为一切正常"。
        ///
        /// 所以判据是 **`FaceFound` 那条线名**（手机明确告诉你找到脸没有），
        /// 协议里没有这个键时才退回"有键就算追到"。
        /// ⚠️ 中间层那 52 个输入行在缺键时**保持上一帧**（VBridger 语义，这是刻意的），
        /// 所以"回中性"本来就不该由它们做，而是由下游图按这个 `IsTracked` 做 —— 别把两件事混起来。
        /// </summary>
        [DataOutput]
        [Label("Is Tracked")]
        public bool IsTracked()
        {
            Ensure();
            if (Raw == null || Raw.Count == 0) return false;
            if (!RawFresh) return false;

            float faceFound;
            if (Raw.TryGetValue(HoVtsPacket.FaceFoundKey, out faceFound))
                return faceFound != 0f;      // 协议报了"找到脸没有"，以它为准

            return true;                     // 协议没有这个键：退回"有键就算追到"
        }

        /// <summary>
        /// 融合形状字典。键是 ARKit 那 52 个的**小驼峰规范名**（Warudo 认的就是这批），
        /// 值是配置文件里 <c>ARKit/&lt;键&gt;</c> 那些行算出来的结果。
        /// </summary>
        [DataOutput]
        [Label("BlendShapes")]
        public Dictionary<string, float> BlendShapes()
        {
            Ensure();
            // 给副本：端口的值会被下游一直拿着，接内部那个字典就成了活引用。
            var copy = new Dictionary<string, float>();
            if (chain != null)
                foreach (var pair in chain.BlendShapes) copy[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>头部位置（米）。来自保留名 <c>Head/PosX|PosY|PosZ</c>。</summary>
        [DataOutput]
        [Label("Head Position")]
        public Vector3 HeadPosition()
        {
            Ensure();
            return chain != null ? chain.HeadPosition : Vector3.zero;
        }

        /// <summary>根位置（米）。来自保留名 <c>Root/PosX|PosY|PosZ</c>。</summary>
        [DataOutput]
        [Label("Root Position")]
        public Vector3 RootPosition()
        {
            Ensure();
            return chain != null ? chain.RootPosition : Vector3.zero;
        }

        /// <summary>
        /// 骨骼旋转，按 <see cref="HumanBodyBones"/> 索引。
        /// **我们只有脸，所以只有 <c>Head</c> 这一格不是 identity**（其余等于"不改那根骨头"）。
        /// </summary>
        [DataOutput]
        [Label("Bone Rotations")]
        public Quaternion[] BoneRotations()
        {
            Ensure();
            if (chain == null) return new Quaternion[0];
            var copy = new Quaternion[chain.BoneRotations.Length];
            for (int i = 0; i < copy.Length; i++) copy[i] = chain.BoneRotations[i];
            return copy;
        }

        // ── 输出：我们自己的诊断（2026-09-25 从六个口收成一个）──────────────────────
        //
        // 只留 `状态` 这一个文本口。原来那五个（`沙箱目录` / `可用配置` / `配置问题` / `发出内容` /
        // `数值预览`）全是"给人看一眼"的东西，不驱动任何节点 —— 合并进这一条就够了，
        // 面板上的口越少，接线越不容易接错。
        //   · 要看**原始数值**：用上面那五个与官方同形的口（`BlendShapes` 字典、`BoneRotations` 数组…）；
        //   · 要看**逐帧的值**：把 `状态` 接到「Ho调试日志」；
        //   · 要看**那份长数值预览**：按节点上的「重读配置」按钮 —— 它会连预览一起写进 Player.log。

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

        /// <summary>配置本身的问题（表达式写错、修饰符 kind 不认得、发不出去的参数名）。</summary>
        private string ProblemsText()
        {
            if (chain == null) return profileNote ?? "没有可用的配置。";
            return chain.Error ?? "没有。";
        }

        private string PreviewText()
        {
            if (chain == null) return "（没有链）";

            var text = new System.Text.StringBuilder();

            // 丢追这件事必须摆在最前面：手机丢追时照样发那 15 个标量，只有 BlendShapes 没了
            // （本帧键 65 → 15），所以光看"有没有数据"是看不出丢追的。
            float faceFound = 0f;
            bool reported = Raw != null && Raw.TryGetValue(HoVtsPacket.FaceFoundKey, out faceFound);
            text.Append("IsTracked = ").Append(IsTracked());
            text.Append("   FaceFound = ").Append(reported ? faceFound.ToString("F0") : "（协议没报）");
            text.Append("   原始键 = ").Append(Raw != null ? Raw.Count : 0);
            text.Append("   形态键到位 = ").Append(chain.BlendShapes.Count > 0 ? "是" : "否").Append('\n');

            text.Append("HeadRotation 欧拉角 = ").Append(V3(chain.HeadRotation.eulerAngles)).Append('\n');
            text.Append("HeadRotation 四元数 = ").Append(Q(chain.HeadRotation)).Append('\n');
            text.Append("单位四元数应是 (0, 0, 0, 1)").Append('\n');
            text.Append("HeadPosition = ").Append(V3(chain.HeadPosition)).Append('\n');
            text.Append("RootPosition = ").Append(V3(chain.RootPosition)).Append('\n');
            text.Append("BoneRotations 长度 = ").Append(chain.BoneRotations.Length).Append('\n');

            AppendBone(text, "Hips", HumanBodyBones.Hips);
            AppendBone(text, "Spine", HumanBodyBones.Spine);
            AppendBone(text, "Head", HumanBodyBones.Head);
            AppendBone(text, "LeftHand", HumanBodyBones.LeftHand);
            AppendBone(text, "LastBone（应是 identity）", HumanBodyBones.LastBone - 1);

            text.Append("BlendShapes 键数 = ").Append(chain.BlendShapes.Count).Append('\n');
            text.Append("非零的键：");
            int shown = 0;
            foreach (var pair in chain.BlendShapes)
            {
                if (pair.Value == 0f) continue;
                if (shown > 0) text.Append(", ");
                text.Append(pair.Key).Append('=').Append(pair.Value.ToString("F4"));
                if (++shown >= 12) { text.Append(" …"); break; }
            }
            if (shown == 0) text.Append("（都是 0 —— 原始输入没接或没收到数据）");

            return text.ToString();
        }

        private void AppendBone(System.Text.StringBuilder text, string label, HumanBodyBones bone)
        {
            AppendBone(text, label, (int)bone);
        }

        private void AppendBone(System.Text.StringBuilder text, string label, int index)
        {
            if (chain == null || index < 0 || index >= chain.BoneRotations.Length)
            {
                text.Append(label).Append(" = （越界）").Append('\n');
                return;
            }
            text.Append(label).Append('[').Append(index).Append("] = ")
                .Append(Q(chain.BoneRotations[index])).Append('\n');
        }

        private static string Q(Quaternion q)
        {
            return "(" + q.x.ToString("F4") + ", " + q.y.ToString("F4") + ", "
                + q.z.ToString("F4") + ", " + q.w.ToString("F4") + ")";
        }

        private static string V3(Vector3 v)
        {
            return "(" + v.x.ToString("F4") + ", " + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ")";
        }
    }
}

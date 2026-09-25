// HoDebugLogNode.cs  --  通用调试日志：接进来什么，就显示出来；点一下按钮，整段进剪贴板
//
// 【它跟面捕无关，谁都能用】放哪个 mod 里都行；这里只是跟着面捕 mod 一起发布。
//
// 【怎么用】把任意输出接到「写入」→ 上面那块**只读**文本跟着变 → 想拿走就点「复制」。
//   没有说明文字、没有下拉框、没有别的按钮。
//
// 【为什么显示用 `[Markdown]`（照抄官方「查看值」）而不是能选中的框】
//   实测（2026-09-25，用户报的）：值在动的时候**框每帧重画，选区就被冲掉** ——
//   用户 Ctrl+A 之后还没来得及复制，选中的就没了。所以"能选中的框"这条路是死的：
//   显示改成**只读渲染**（选不中，也就没有"选了一半被冲掉"这回事），复制交给按钮。
//   官方 `InspectValueNode` 的显示字段就是 `[Markdown(13, False, False)] public String Text`
//   （`warudo-knobs --attrs` 读的），我们**原样照抄这一行** —— 控件由特性决定，不由类决定，
//   所以这就是"复用内置节点的玩意儿"。
//
// 【为什么按钮是 `[Trigger]`，剪贴板是 `UnityEngine.GUIUtility`】
//   · Warudo 的**纯按钮**就是 `[Trigger(order)]`（官方节点一大堆：`CommentNode.Edit/Done`、
//     `SetAssetPositionNode.AlignTargetWithAsset`…）。它不占口；`[FlowInput]` 也能点，但会多一个
//     flow 出口 socket，对"日志"这种节点是多余的（第一版就是那么写的，收口时改成 `[Trigger]`）。
//     ⚠️ 查官方用法要用 `warudo-knobs --find-attr TriggerAttribute`（**带 Attribute 后缀**）——
//        写成 `Trigger` 会静默返回空，我据此写出过一条"Core 里没有 `[Trigger]`"的错结论。
//   · Warudo 自己**没有剪贴板 API**（两个 DLL 的 `--list Clipboard` 都是空）。
//     整个 Managed 目录里只有 `UnityEngine.IMGUIModule.dll` 带 `systemCopyBuffer`
//     （Tools.dll / TextMeshPro / UI / Vuplex 里那几个是同名别的东西）。
//     → `UnityEngine.GUIUtility.systemCopyBuffer = 文本;`
//     ⚠️ 本地 `tools/compile-check.ps1` 的引用表为此必须有 `UnityEngine.IMGUIModule.dll`。
//
// 【必须照抄的两条，否则界面不重画】（都实测过，别再改回去）
//   · 写这个字段要**字段赋值 + `BroadcastDataInput`**：官方 IL 就是 `stfld Text` 紧接着
//     `BroadcastDataInput("Text")`；只调 `SetDataInput` 时端口里有新值，界面上那块纹丝不动。
//   · 输入口用 `object`：用 `string` 的话，非字符串上游（整张表、数组）根本接不进来。
//
// 【为什么"直接读上游"而不是等着被推】（2026-09-25 定案，实测逼出来的）
//   症状：线**确实接在「写入」上**（`Player.log`：`输入连线：「A」←Ho Face 接收器（VTS 手机）.RawValues`
//   —— 那是**当时的节点标题**，现在叫 `HoFaceVTS接收器`），
//   同一根上游喂官方「查看值」有数据，可这个节点的 `A` 一直是空。
//   所以改成顺着连线自己去上游那个口要值：
//     `Graph.GetInputDataConnections(this)` → `DataConnection` → `OutputNode` + `OutputPort`；
//     口上就挂着 **`public Func<object> ComputedValue`**（`DataOutputPort`），调一下就是这一帧的值。
//   端口/字段那条老路留着当兜底：真被推过来时照样认。
//
// 【直读的代价（这个副作用是承认的，并且压过）】
//   直读 = **替流程图求值一次上游那个口**，所以：
//     · 上游每被读一次就要算一次。接收器的 `RawValues()` 是 `HoFaceInputState.Snapshot()`，
//       每调一次**新建一个字典**；它本来被处理链要一次，我们这是额外的第二次。
//     · 我们还要把整张表摊成文本（排 65 个键 + 拼 ~1.3 KB 字符串）才能跟上一帧比"变没变"。
//     · 每读一次还有一次 `Graph.GetInputDataConnections(this)`。
//   → 所以**不是每帧读**，而是每 `ReadInterval`（默认 0.1 s = 10 Hz）读一次：观感没差别，垃圾少 6 倍。
//     真要看每一帧的值，用官方「查看值」节点（它读字段，不替谁求值）。
//   还有一条语义上的：**求值时机由我们决定**（我们的 `OnUpdate`），不是流程图决定。
//   我们自己的口都是"现场算的纯读"，且处理链的 `Ensure()` 用 `Time.frameCount` 保证一帧只算一次，
//   所以"时机"没有可观察后果；**但如果你把有副作用的口接进来，就等于让我们替你触发了它**。
//
// 【⚠️ 这里绝不能出现 `System.Reflection`】（两次真机构建烧出来的）
//   UMod 的安全校验把整个命名空间判非法，构建直接失败：
//     `Illegal reference to disallowed namespace: System.Reflection` → `BUILD FAILED!`
//   而且连**间接引用**都算：`value.GetType().Name` 编译成 `MemberInfo::get_Name`，照样毙。
//   `DataOutputPort.ComputedValue` 与 `is` 模式判断就是为了绕开它 —— 纯调用，不碰反射。
//   本地 `tools/compile-check.ps1` 的 `UMod sandbox lint` 阶段会在本地拦这一类引用。
//
// 【行为上要知道的两点】
//   · 上游**直接接在「日志」那一行**上也行（老版本那种用法）—— 探到这种接法就**不碰它**。
//   · 断流时**不清空**：保持最后一次拿到的内容，方便你回头复制；重新有值就跟着变。
//
// 【诊断都在 Player.log 里，界面上看不见】
//   · 连线变了就写一行：`[Ho 调试日志] 输入连线：「A」←「…」.RawValues`
//     —— 孤儿线会带一句 `（这个输入口已经不存在了 → 删掉这条线重接）`（判据：`InputPort == null`）。
//   · 还没拿到值的时候，每秒写一行当前状态（直读拿到了什么、端口里有没有）——"接上了却没值"靠它定性。
//
// 【想抓"运行中每一帧"的现场：把「写日志」开关打开】
//   打开后每 0.5 s 把当前这份值**原样**写进 `Player.log`（前缀 `[Ho 调试日志] `），值停住也写
//   —— 停顿本身是情报（"卡在 0 不动"、"丢脸后还是老值"）。界面上那块只留最后一帧、没法两份对照，
//   所以"原始值 vs 输出值逐条对"要在日志里做。查完关掉。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "e2a47f83-5d19-4c6b-a07e-91b3c58d4f26",   // 保持原 Id：蓝图里已经放好的节点不会丢
        Title = "Ho调试日志",
        Category = "Ho Face Tracking")]
    public class HoDebugLogNode : Node
    {
        /// <summary>入口：**什么类型都行**。不接就一直空着。</summary>
        [DataInput(10)]
        [Label("写入")]
        public object A;

        /// <summary>
        /// 显示这段文本 —— **只读渲染、选不中**（照抄官方「查看值」的形态：
        /// `warudo-knobs --attrs` 读出来是 `[Markdown(13, False, False)] public String Text`）。
        /// 选不中是有意的：能选中的框在值频繁变化时**每帧重画会把选区冲掉**，根本复制不成。
        /// 想拿走内容用上面的「复制」按钮（它复制的是**没被显示改写过**的那份原文）。
        /// `[Transient]`：一次性的显示内容，不跟着蓝图存进场景文件。
        /// </summary>
        [Markdown(20, false, false)]
        [Transient]
        [Label("日志")]
        public string Text = "";

        /// <summary>
        /// 把当前这段文字整份复制到剪贴板。`[Trigger(order)]` = **纯按钮**（官方节点的按钮全是这个：
        /// `CommentNode.Edit/Done`、`SetAssetPositionNode.AlignTargetWithAsset`…，
        /// `warudo-knobs --find-attr TriggerAttribute` 一抓一大把）。
        /// 它不占任何口 —— 不用像 `[FlowInput]` 那样再交回一个 flow 出口。
        /// ⚠️ 复制的是 <see cref="_raw"/> 而不是 <see cref="Text"/>：显示版为了 Markdown 换行做过改写。
        /// </summary>
        [Trigger(30)]
        [Label("复制")]
        [Description("把上面这段文字整份复制到系统剪贴板。")]
        public void Copy()
        {
            GUIUtility.systemCopyBuffer = _raw ?? "";
        }

        private string _raw = "";           // 当前这段文字（原文；复制按钮复制的就是它）
        private string _wiring;             // 上一次报过的连线情况
        private string _noValueLine;        // 上一次报过的"还没有值"现场
        private float _lastWiringCheck;
        private float _lastDiag;
        private float _lastRead;
        private float _lastLogged;
        private bool _displayWired;         // 上游接在显示那一行上
        private bool _gotValue;

        /// <summary>
        /// **持续写日志**的开关。打开后，这个节点每 <see cref="LogInterval"/> 秒把当前这份值
        /// 原样写进 `Player.log`（带上游给它的原文，含换行），于是"运行中的每一帧"都能事后在日志里读。
        ///
        /// 为什么不用界面那块看：它只保留**最后一帧**，而且值一变就重画 —— 查"原始值 vs 输出值
        /// 是不是逐条对上"要的是**同一帧的两份值**，界面上没法对照。
        ///
        /// ⚠️ **默认关**（`false`）。只有你手动打开才写日志 —— 排查用完记得关回去。
        /// </summary>
        [DataInput(11)]
        [Label("写日志")]
        [Description("打开：每 0.5 s 把当前这份值原样写进 Player.log（排查用，查完关掉）。")]
        public bool LogToFile = false;

        /// <summary>
        /// 多久往日志里写一行（秒）。0.5 s 足够看清"值在动/卡住"，又不至于把日志刷爆。
        /// </summary>
        private const float LogInterval = 0.5f;

        /// <summary>
        /// 这行日志的**出处标签**。放两个「Ho调试日志」（一个接原始值、一个接出口参数）时，
        /// 日志里就会有 `[Ho 调试日志] 原始 ← …` / `[Ho 调试日志] 出口 ← …`，不用回头猜哪行是谁打的。
        /// 留空就写 `-`。**不是必填**：只有写日志那一行用到它。
        /// </summary>
        [DataInput(12)]
        [Label("标签")]
        [Description("写进日志时给这一行加个出处（例如「原始」「出口」）。留空写 `-`。")]
        public string Tag = "";

        /// <summary>
        /// 多久去上游要一次值（秒）。**不是每帧** —— 理由见下面的"直读的代价"。
        /// 想更跟手就调小（0.033 = 30 Hz），想更省就调大；屏幕上这是调试日志，10 Hz 已经看不出差别。
        /// </summary>
        private const float ReadInterval = 0.1f;

        public override void OnUpdate()
        {
            float now = Time.realtimeSinceStartup;
            RefreshWiring(now);

            // 上游直接接到显示那一行上：别覆盖它，让它自己显示。
            if (_displayWired) return;

            // 直读的代价（为什么不是每帧读）：
            //   · 每读一次 = 替上游求值一次。接收器的 `RawValues()` 是 `HoFaceInputState.Snapshot()`，
            //     它**每调一次就新建一个字典**（注释原话"52~200 个键拷一份"）；处理链本来就会要一次，
            //     我们这是**额外的第二次**。
            //   · 每读一次还要把整张表摊成文本（排 65 个键 + StringBuilder + ~1.3 KB 字符串），
            //     而绝大多数帧结果跟上一帧一模一样 —— 全部白做。
            //   · 每帧还会 `Graph.GetInputDataConnections(this)` 一次（那张字典也是新建的）。
            //   压到 10 Hz 之后：观感没差别，垃圾少 6 倍。真要看每一帧的值请用官方「查看值」节点。
            if (now - _lastRead < ReadInterval) return;
            _lastRead = now;

            // 第一优先：顺着连线直读上游那个口（不赌推送）。
            string next = "";
            string source = null;
            object upstream;
            string upstreamName;
            if (TryReadUpstream(out upstream, out upstreamName))
            {
                next = Describe(upstream);
                if (next.Length > 0) source = "直读 " + upstreamName;
            }

            // 兜底：上游真被推到这个口/字段里时走这条。
            if (next.Length == 0)
            {
                string pushed = Describe(ReadInput());
                if (pushed.Length > 0)
                {
                    next = pushed;
                    source = "端口/字段";
                }
            }

            // 还没拿到值：每秒写一行现场，别让它闷着。
            if (next.Length == 0)
            {
                ReportNoValue(now, upstreamName, upstream);
                return;
            }

            bool changed = next != _raw;
            if (changed)
            {
                _raw = next;
                Text = ToMarkdown(next);
                BroadcastDataInput(nameof(Text));   // ← 少了这一句界面不会重画（实测）
            }

            if (!_gotValue)
            {
                _gotValue = true;
                Debug.Log("[Ho 调试日志] 第一次拿到值：" + next.Length + " 个字符（" + source + "）");
            }

            // 持续写日志（开关打开时）。**故意不等值变化**：值不变本身也是情报
            //（比如"卡在 0 不动"、"丢脸之后还是老值"），而那正是要抓的现场。
            if (LogToFile) WriteToLog(now, next);
        }

        /// <summary>
        /// 持续把当前这份值写进 `Player.log`（不是写在界面上 —— 界面那块只显示最新的一份）。
        ///
        /// 为什么要它：界面上那块只留**最后一帧**，而"原始值 vs 输出值逐条对"要的是
        /// **同一帧的两份**。自动 dump 那种"抓到一次就停"的做法还要赌时机（丢脸时手机照样发 15 个标量，
        /// 一眼看不出是不是有脸帧），所以改成"想看就一直打"。
        ///
        /// 节奏 = 直读的节奏（<see cref="ReadInterval"/>，10 Hz），再压一层 <see cref="LogInterval"/>：
        /// 值在动的时候每 0.5 s 一行，**值停下时也会写**（停顿同样要看得出）。
        /// 一行 ~1.3 KB，开着时的量按 2 行/秒算 ≈ 2.6 KB/s —— 查完就关掉。
        /// </summary>
        private void WriteToLog(float now, string text)
        {
            if (now - _lastLogged < LogInterval) return;
            _lastLogged = now;
            Debug.Log("[Ho 调试日志] " + (string.IsNullOrEmpty(Tag) ? "-" : Tag) + " ←\n" + text);
        }

        /// <summary>
        /// 给显示用的版本：Markdown 里单个换行是"软换行"，有些渲染器会并成一行 ——
        /// 行尾补两个空格是标准的硬换行写法，认 Markdown 的就断行、不认的也看不见这两个空格。
        /// **只改显示**，复制走的是原文（`_raw`）。
        /// </summary>
        private static string ToMarkdown(string raw)
        {
            return raw.Replace("\n", "  \n");
        }

        // ── 直读上游 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 顺着本节点的输入连线，找**上游节点 + 上游口**，直接求值。
        /// 不关心这条线挂在我们哪个口上（改过名的孤儿线照样能读出值）。
        /// 找到可求值的口就返回 true（上游没数据时 <paramref name="value"/> 可能是空 —— 那也是情报）。
        /// </summary>
        private bool TryReadUpstream(out object value, out string name)
        {
            value = null;
            name = null;

            var graph = Graph;
            if (graph == null) return false;

            IReadOnlyDictionary<string, List<DataConnection>> map;
            try
            {
                map = graph.GetInputDataConnections(this);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return false;
            }

            if (map == null) return false;

            foreach (var pair in map)
            {
                var list = pair.Value;
                if (list == null) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var connection = list[i];
                    if (connection == null || connection.OutputNode == null || connection.OutputPort == null) continue;

                    string key = connection.OutputPort.Key;
                    // 以上游节点自己的口为准（连接上那个口是它的副本），拿不到再退回连接上的。
                    DataOutputPort port = connection.OutputNode.GetDataOutputPort(key) ?? connection.OutputPort;
                    if (port == null || port.ComputedValue == null) continue;

                    try
                    {
                        value = port.ComputedValue();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        continue;
                    }

                    name = "「" + connection.OutputNode.Name + "」." + key;
                    return true;
                }
            }

            return false;
        }

        /// <summary>读上游推过来的值：先读端口，再退回字段。</summary>
        private object ReadInput()
        {
            try
            {
                object fromPort = GetDataInput<object>(nameof(A));
                if (fromPort != null) return fromPort;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            return A;
        }

        // ── 诊断 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 每 0.5 秒问一次图："我这个节点的输入口都接了什么"。
        /// 只在**还没有拿到值**的时候攒诊断文本（拿到值之后这条就没用了），且只在连线变化时写日志。
        /// </summary>
        private void RefreshWiring(float now)
        {
            if (now - _lastWiringCheck < 0.5f) return;
            _lastWiringCheck = now;

            var graph = Graph;
            if (graph == null) return;

            var builder = new StringBuilder();
            bool displayWired = false;
            try
            {
                var map = graph.GetInputDataConnections(this);
                if (map != null)
                    foreach (var pair in map)
                    {
                        if (pair.Key == nameof(Text)) displayWired = true;
                        if (_gotValue) continue;
                        if (builder.Length > 0) builder.Append("；");
                        builder.Append('「').Append(pair.Key).Append('」');

                        var list = pair.Value;
                        if (list == null || list.Count == 0) { builder.Append(" 没有连线"); continue; }
                        for (int i = 0; i < list.Count; i++)
                        {
                            var connection = list[i];
                            string from = connection == null || connection.OutputNode == null
                                ? "?" : connection.OutputNode.Name;
                            string port = connection == null || connection.OutputPort == null
                                ? "?" : connection.OutputPort.Key;
                            builder.Append('←').Append(from).Append('.').Append(port);
                            if (connection != null && connection.InputPort == null)
                                builder.Append("（这个输入口已经不存在了 → 删掉这条线重接）");
                        }
                    }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return;
            }

            _displayWired = displayWired;
            if (_gotValue) return;

            string wiring = builder.Length == 0 ? "（没有任何输入连线）" : builder.ToString();
            if (wiring == _wiring) return;
            _wiring = wiring;
            Debug.Log("[Ho 调试日志] 输入连线：" + wiring);
        }

        /// <summary>
        /// "接上了却没值"的现场：直读上游拿到了什么、端口里有没有。
        /// 每秒一行，且只在内容变了的时候写（免得刷屏）。
        /// </summary>
        private void ReportNoValue(float now, string upstreamName, object upstream)
        {
            if (now - _lastDiag < 1f) return;
            _lastDiag = now;

            string line = "[Ho 调试日志] 还没有值：直读 "
                + (upstreamName ?? "（没有可读的上游口）")
                + " = " + Summarize(upstream)
                + " · 端口 = " + Summarize(ReadInput());
            if (line == _noValueLine) return;
            _noValueLine = line;
            Debug.Log(line);
        }

        /// <summary>
        /// 给诊断用的一句话摘要（不是显示的那份文本）。
        ///
        /// ⚠️ **这里不许出现 `value.GetType().Name`**：它编译出来是 `System.Reflection.MemberInfo::get_Name`，
        /// 安全校验连这种"间接引用"都算（第二次构建就是这么炸的：`Illegal Type References = '1'`）。
        /// 所以类型判断一律用 `is` 模式，想知道是什么就多列几种自己认得的。
        /// </summary>
        private static string Summarize(object value)
        {
            if (value == null) return "空";
            if (value is string text) return text.Length + " 个字符";
            if (value is IDictionary<string, float> table) return table.Count + " 个键";
            if (value is System.Collections.IDictionary rows) return rows.Count + " 行";
            if (value is System.Collections.ICollection items) return items.Count + " 项";
            return "有值（不是字符串/表）";
        }

        // ── 显示 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 把上游那个对象摊成能读的文本。
        ///
        /// `object` 口什么都能接，所以不能只靠 `ToString()` —— 它至少在两处会打出没用的东西：
        ///   · `Dictionary` → `System.Collections.Generic.Dictionary...`（类型名）；
        ///   · **数组** → `UnityEngine.Quaternion[]`（实测：处理链的 `Bone Rotations` 接过来就是这个，
        ///     而官方「检查值」能把值打出来 —— 差别就在这儿）。
        /// 所以这里认四类：字符串原样、名字→值的表（排序后摊平）、**数组/列表逐项摊开**、
        /// `Vector3`/`Quaternion` 用 F3 打；其余才交给 `ToString()`。
        /// </summary>
        private static string Describe(object value)
        {
            if (value == null) return "";

            if (value is string text) return text;

            if (value is IDictionary<string, float> table)
            {
                if (table.Count == 0) return "";

                var names = new List<string>(table.Keys);
                names.Sort(System.StringComparer.Ordinal);
                var builder = new StringBuilder();
                foreach (string name in names)
                {
                    if (builder.Length > 0) builder.Append('\n');
                    builder.Append(name).Append(" = ").Append(table[name].ToString("F3"));
                }

                return builder.ToString();
            }

            // 键不是 `string → float` 的字典（官方有些口给的是这类）：按原顺序摊平。
            if (value is System.Collections.IDictionary rows)
            {
                if (rows.Count == 0) return "";

                var builder = new StringBuilder();
                foreach (System.Collections.DictionaryEntry entry in rows)
                {
                    if (builder.Length > 0) builder.Append('\n');
                    builder.Append(entry.Key).Append(" = ").Append(Describe(entry.Value));
                }

                return builder.ToString();
            }

            // 数组与列表（`T[]` 和 `List<T>` 都实现了 IList）：一条一项，带上标号。
            // 骨头那根数组是 55 项，全打出来（官方「查看值」也是把整个数组打出来）——
            // 嫌长就别接这个口，处理链那边点「重读配置」会把经过挑选的数值预览写进 Player.log。
            if (value is System.Collections.IList list)
            {
                if (list.Count == 0) return "";

                var builder = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    if (builder.Length > 0) builder.Append('\n');
                    builder.Append('[').Append(i).Append("] = ").Append(Describe(list[i]));
                }

                return builder.ToString();
            }

            if (value is Vector3 vector)
                return "(" + vector.x.ToString("F3") + ", " + vector.y.ToString("F3") + ", " + vector.z.ToString("F3") + ")";

            if (value is Quaternion rotation)
                return "(" + rotation.x.ToString("F3") + ", " + rotation.y.ToString("F3") + ", "
                    + rotation.z.ToString("F3") + ", " + rotation.w.ToString("F3") + ")";

            return value.ToString();
        }
    }
}

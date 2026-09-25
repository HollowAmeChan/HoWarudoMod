// HoStringFloatDictNode.cs  --  「HoStringFloatDict」：面板上手填若干 `(名字, 值)`，**直接创建一份字典**
//
// 【名字与家族】（2026-09-27 用户定）面板标题 **`HoStringFloatDict`** —— 它**不是**"把键值对转成字典"，
// 而是**直接创建一份字典**（手填的行就是那张表）。同一个家族里的另外几个：
//   `HoStringFloat`（名字 + 值 → `KeyValuePair<string,float>` 键值对）· `HoStringFloatAppend`（表 + 键值对 → 表）
//   · `HoStringFloatMerge`（两张表 → 一张）· `HoBool2Float`（bool → float）。
//
// 【为什么要有它】（2026-09-27 用户定：新节点就两个 —— 这个 +「Ho合并字典」）
// Warudo 的图里**没有"手填一份字典"这回事**：`Dictionary<string,float>` 落在 `Reference` 那一类口上，
// 官方图里那 41 个字典口**全是接过来的、一个都没手填过**（2026-09-26 取证：官方场景 json 的 `typeKind`
// 统计 + 两套程序集全量反射，见 HoUnityTools `docs/FACE_TRACKING_WARUDO_ROUTE.md` §3.5）。
// 官方能"手填一组一组东西"的只有 `ValueArray` 与 `StructuredData`/`StructuredDataArray` ——
// 后者就是官方面板上"一组一组填"的机制（加/删行，字段是 `[DataInput]`）—— ⚠️ 它是**结构化行**，
// 所以"手填一张 (名字, 值) 表"这件事**只能做成节点**，而这个节点就是那个通用件。
//
// 【典型用法】
//   行里填 `Ho/Drive/Gate/Lip = 0` ⇒ `字典` 喂给「HoStringFloatMerge」的 `覆盖`（或直接喂「HoFace控制求解」）
//   行里填若干常量 ⇒ 想给谁就给谁：合并字典 / 控制求解 / 写动态参数，都是同一个 `Dictionary<string,float>`。
//
// 【为什么输出是字典，而不是键值对列表】（2026-09-27 定）
//   图里唯一的字典类型就是 `Dictionary<string,float>`，合并字典/控制求解/写动态参数的口全是它 ——
//   这个节点输出字典 ⇒ **类型完全相同、直接能插**，而且"别的类型不许接"由 Warudo 在**接线那一刻**
//   抛 `ArgumentException("… is not compatible with …")` 执行（比运行期自己判类型严格得多）。
//   多态口（`object`）只有在"要接线的键值对真的存在"时才有意义 —— 而它确实存在（`KeyValuePair<string,float>`
//   已实测能当端口），所以"一个 `(名字, 值)`"直接用真键值对表示，不必拿"1 项字典"凑。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Graphs;
using UnityEngine;

namespace HoFaceTracking.Nodes
{
    /// <summary>
    /// 一行 `(名字, 值)`（`string` + `float`）。面板上加/删行，不用接线。
    /// 基类 <see cref="StructuredData{TParent}"/> 是官方表达"结构化的一行"的唯一机制（见文件头）。
    /// </summary>
    public class HoStringFloatEntry : StructuredData<HoStringFloatDictNode>, ICollapsibleStructuredData
    {
        /// <summary>要写的名字（= 目标字典的键）。空名字的行会被忽略。</summary>
        [DataInput(10)]
        [Label("名字")]
        public string Key;

        /// <summary>这个名字对应的值。</summary>
        [DataInput(20)]
        [Label("值")]
        public float Value;

        /// <summary>行标题（`ICollapsibleStructuredData`）：折叠时显示"名字 = 值"，免得只看到一串空行。</summary>
        public string GetHeader()
        {
            return string.IsNullOrEmpty(Key) ? "(空名字)" : Key + " = " + Value.ToString("0.###");
        }
    }

    [NodeType(
        Id = "3e9abc40-8c60-4238-880a-c4a4febee63a",
        Title = "HoStringFloatDict",
        Category = "Ho Face Tracking")]
    public class HoStringFloatDictNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>要输出的那些 `(名字, 值)`。**空名字的行会被忽略**；同名多行时**后面的赢**。</summary>
        [DataInput(10)]
        [Label("行")]
        [Description("一行一个「名字 + 值」，在面板上加行即可。同名多行 ⇒ 后面的赢；名字空的行忽略。")]
        public HoStringFloatEntry[] Rows;

        // ── 状态 ────────────────────────────────────────────────────────────────

        private readonly Dictionary<string, float> table = new Dictionary<string, float>();
        private int usedLast;
        private int badKeysLast;
        private int duplicatesLast;
        private string loggedState;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 手填出来的那份表。
        /// ⚠️ **返回的是内部复用的同一个实例**（每次原地重填，不每帧 new 一个字典 —— 官方
        /// `OffsetBlendShapeNode.lastBlendShapes` 也是这个做法）：所以别把它存下来跨帧看，要留一份就自己拷。
        /// </summary>
        [DataOutput]
        [Label("字典")]
        public Dictionary<string, float> Table()
        {
            table.Clear();
            usedLast = 0;
            badKeysLast = 0;
            duplicatesLast = 0;

            if (Rows != null)
            {
                for (int i = 0; i < Rows.Length; i++)
                {
                    var row = Rows[i];
                    if (row == null) continue;
                    if (string.IsNullOrEmpty(row.Key)) { badKeysLast++; continue; }
                    // 同名多行：后面的赢（跟配置里"同名多行最后一行生效"是同一条规矩）。
                    if (table.ContainsKey(row.Key)) duplicatesLast++;
                    table[row.Key] = row.Value;
                    usedLast++;
                }
            }

            LogOnce();
            return table;
        }

        /// <summary>
        /// 状态：填了几行、有效的几行、空名字几行、同名重复几个、结果几个键。
        /// 它是这个节点唯一的诊断出口（纯函数节点没有别的报错渠道）。
        /// </summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            Table();
            return "行 " + (Rows != null ? Rows.Length : 0)
                + "  ·  有效 " + usedLast
                + "  ·  结果 " + table.Count + " 键"
                + (duplicatesLast > 0 ? "  ·  同名重复 " + duplicatesLast + " 个（后写的赢）" : "")
                + (badKeysLast > 0 ? "  ⚠️ " + badKeysLast + " 行名字为空（已忽略）" : "");
        }

        /// <summary>结构变了才写一行日志（行数 / 键数变化时），免得每帧刷屏。</summary>
        private void LogOnce()
        {
            string state = (Rows != null ? Rows.Length : 0) + "/" + usedLast + "/" + table.Count
                + "/" + duplicatesLast + "/" + badKeysLast;
            if (state == loggedState) return;
            loggedState = state;
            Debug.Log("[Ho StringFloatDict] 行 " + (Rows != null ? Rows.Length : 0)
                + " · 有效 " + usedLast
                + " · 结果 " + table.Count + " 键"
                + (duplicatesLast > 0 ? " · 同名重复 " + duplicatesLast : "")
                + (badKeysLast > 0 ? " · 空名字 " + badKeysLast : ""));
        }
    }
}

// HoStringFloatNode.cs  --  「HoStringFloat」：**一个 `(名字, 值)`** —— 输出 `.NET` 的 `KeyValuePair<string,float>`
//
// 【形状】`名字`(string) + `值`(float) → `元组`(`KeyValuePair<string,float>`)
// 这就是"把值拼成一个元组"那一步：布尔先用 `HoBool2Float` 换成 1/0，再由这里配上名字。
//
// 【✅ 2026-09-27 实测：`KeyValuePair<string,float>` **能当 Warudo 的图端口**】
// 之前我按"官方 port 词汇表里没有 `Tuple`/`KeyValuePair`"推断"不能当端口"，于是绕成 `名字`+`值` 两个口 ——
// 那是**推断，不是实测**（用户指出："为什么你就是不能写 `<string,float>` 类型呢"）。
// 拿探针节点（`HoStringFloatProbe`）在 Warudo 里一跑就清楚了：
//   · 节点**注册通过、出现在面板里**（注册期要把字段默认值序列化成字符串，这一关过了）；
//   · `KeyValuePair<string,float>` 的端口**画得出来**，悬停提示直接写 `KeyValuePair<String, Float>`。
// ⇒ 所以家族改成**真元组口**：本节点吐元组、`HoStringFloatAppend` 收元组。
//
// 【家族】
//   `HoStringFloat`        —— 名字 + 值 → **元组**（本文件）
//   `HoStringFloatAppend`  —— 表 + 元组 → 追加后的表（**"再转字典"就是它**）
//   `HoStringFloatMerge`   —— 两张表 → 一张（下面的盖上面的）
//   `HoStringFloatDict`    —— 面板手填多行 → 直接创建一张表
//   `HoBool2Float`         —— bool → 1/0（官方没有这个转换，而面捕里布尔值很多）
//
// ⚠️ 名字留空时 `Key` 是空串：下游 `HoStringFloatAppend` 会把它当"没有这一项"（`状态` 里会说明）。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using System.Collections.Generic;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "39edc904-0493-4dd3-9d73-521aa66e0e50",
        Title = "HoStringFloat",
        Category = "Ho Face Tracking")]
    public class HoStringFloatNode : Node
    {
        // ── 输入 ────────────────────────────────────────────────────────────────

        /// <summary>这一项的名字。**留空 = 这个元组没有名字**（下游会跳过它）。</summary>
        [DataInput(10)]
        [Label("名字")]
        public string Key;

        /// <summary>这个名字对应的值。</summary>
        [DataInput(20)]
        [Label("值")]
        public float Value;

        // ── 输出 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 拼出来的 `(名字, 值)`。`KeyValuePair` 是**值类型**，所以这里没有"复用实例"那类坑
        /// （对比几个字典口：它们复用的是内部 `Dictionary`）。
        /// </summary>
        [DataOutput]
        [Label("元组")]
        public KeyValuePair<string, float> Tuple()
        {
            return new KeyValuePair<string, float>(Key, Value);
        }

        /// <summary>状态：这个元组是什么（或"名字为空"）。</summary>
        [DataOutput]
        [Label("状态")]
        public string Status()
        {
            return string.IsNullOrEmpty(Key)
                ? "⚠ 名字为空 ⇒ 这个元组没有名字（下游 `HoStringFloatAppend` 会跳过它）"
                : Key + " = " + Value.ToString("0.###");
        }
    }
}

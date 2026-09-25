// HoBool2FloatNode.cs  --  「HoBool2Float」：`true/false` → `1/0`
//
// 【为什么需要它】（2026-09-27 用户定）
// 面捕里很多值是**布尔**（"这块算不算数""丢没丢脸""跟不跟"…），而"名字 → 浮点"这张表只能装浮点。
// 官方**没有** bool → float 的路：
//   · 转换节点里只有 `Integer Convert To Float` / `Float Convert To Integer` / `Convert To String` /
//     `Convert Boolean To Toggle Action` —— **没有 bool → float**；
//   · 内置转换器表（`DataConverters.Initialize` 的 IL）也只有 `IntToFloat` / `FloatToInt` /
//     `IntToString` / `FloatToString` / **`BoolToString`** —— 同样没有。
// 所以这个转换得我们自己给一个节点（比"注册一个 bool→float 转换器"更直白：图上看得见这一步）。
//
// 【形状】照官方 `Integer Convert To Float`：一个输入、一个输出，没有别的东西。
//   `值`(bool) → `值`(float)：`true` = `1`，`false` = `0`。
// 接到 `HoStringFloatAppend` / `HoStringFloat` 的 `值` 口，就能把布尔写进那张表。
//
// 【端口规则】[DataInput] = public 字段；[DataOutput] = public 方法；
// ⚠️ 字段名别叫 Name（撞 Node 基类）；`Plugin` 也别用（撞 Node.Plugin 属性）。

using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "25c639c7-6b33-4e90-bb6a-4aa811613aba",
        Title = "HoBool2Float",
        Category = "Ho Face Tracking")]
    public class HoBool2FloatNode : Node
    {
        /// <summary>要转换的布尔值。</summary>
        [DataInput(10)]
        [Label("值")]
        public bool Value;

        /// <summary>`true` = `1`，`false` = `0`。</summary>
        [DataOutput]
        [Label("值")]
        public float Result()
        {
            return Value ? 1f : 0f;
        }
    }
}

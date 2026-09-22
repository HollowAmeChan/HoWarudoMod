// HoTestGreetNode.cs  -- 插件 Mod 节点 1（Nodes/ 目录）
//
// 流程节点：有 DataInput / DataOutput / FlowInput / FlowOutput 四类端口，
// 是最小但完整的节点形态。
//
// 端口规则（Warudo 强制）：
//   [DataInput]  -> public 字段
//   [DataOutput] -> public 方法
//   [FlowInput]  -> public 方法，返回值必须是 Continuation
//   [FlowOutput] -> public 字段，类型必须是 Continuation
//   Continuation 三态：return Exit; / return null;（流程终止）/ InvokeFlow(nameof(Exit));

using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoWarudoModTests.Plugin.Nodes
{
    [NodeType(
        Id = "9f1d0a24-6b7f-4d3e-9c2a-1b0e5f7a8c11",
        Title = "Ho Test Greet",
        Category = "HoWarudoModTests")]
    public class HoTestGreetNode : Node
    {
        [DataInput]
        public string Name = "World";

        [FlowInput]
        public Continuation Enter()
        {
            _greeting = "Hello, " + Name + "!";
            return Exit;
        }

        [DataOutput]
        public string Greeting()
        {
            return _greeting ?? "";
        }

        [FlowOutput]
        public Continuation Exit;

        private string _greeting;
    }
}

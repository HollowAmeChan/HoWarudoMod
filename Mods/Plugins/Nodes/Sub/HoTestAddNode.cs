// HoTestAddNode.cs  -- 插件 Mod 节点 2（位于嵌套子目录 Nodes/Sub/）
//
// 存在的意义有两个：
//   1) 证明节点源码放在多深的子目录里都能被打包；
//   2) 演示"纯数据节点"——没有 FlowInput / FlowOutput，只做数值计算。

using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoWarudoModTests.Plugins.Nodes.Sub
{
    [NodeType(
        Id = "3c7b2e51-8a4d-4f6b-b1e9-2d5c8a0f4e22",
        Title = "Ho Test Add",
        Category = "HoWarudoModTests")]
    public class HoTestAddNode : Node
    {
        [DataInput]
        public float A = 1f;

        [DataInput]
        public float B = 2f;

        [DataOutput]
        public float Sum()
        {
            return A + B;
        }
    }
}

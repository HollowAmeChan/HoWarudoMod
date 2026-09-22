// HoTestPlugin.cs  -- 插件类 Mod 入口（蓝图节点类 Mod 的下限）
//
// "插件 Mod" 的官方定义：一个普通 Warudo Mod，里面包含一个继承
// Warudo.Core.Plugins.Plugin 的 C# 类，并用 [PluginType] 注册。
//
// NodeTypes 必须列全本 Mod 贡献给蓝图面板的所有节点类型；
// 漏掉的节点即使编译进了程序集，也不会出现在节点面板里。
//
// ⚠️ 命名空间不要叫 ...Plugin：那样 using Warudo.Core.Plugins 里的 Plugin 基类
//    会被自己所在的命名空间遮蔽，报 CS0118「"Plugin" 是命名空间，但此处被当做类型来使用」。
//    这里用 PluginMod 就是为了避开这个坑。

using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

namespace HoWarudoModTests.Plugins
{
    [PluginType(
        Id = "howarudomodtests.plugins",
        Name = "Ho Warudo Mod Tests",
        Description = "Minimal multi-file plugin mod used to verify Warudo packaging.",
        Version = "1.0.0",
        Author = "Hollow",
        NodeTypes = new[]
        {
            typeof(Nodes.HoTestGreetNode),
            typeof(Nodes.Sub.HoTestAddNode)
        })]
    public class HoTestPlugin : Plugin
    {
    }
}

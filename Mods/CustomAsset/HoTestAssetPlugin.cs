// HoTestAssetPlugin.cs  -- 资源类型 Mod 的插件入口
//
// 【核心结论】Warudo 没有"资源类型"这个独立打包类型。
// 一个能往 Warudo 里添加新资源（Add Asset 菜单里的新条目）的 Mod，
// 打包上就是一个普普通通的**插件类 Mod**（落点 StreamingAssets/Plugins），
// 只不过它的程序集里除了 [PluginType] 还多了一个 [AssetType] 类。
//
// 证据（本机实测，见 README）：
//   工坊 3780922560 = Plugins\VRM1SpringBoneWind.warudo，扫描其程序集得到
//     1 个 [PluginType] + 1 个 [AssetType]（Vrm1SpringBoneWindAsset : Warudo.Core.Scenes.Asset）
//     0 个 [NodeType]
//
// 【AssetTypes 必须列全】与 NodeTypes 同理：漏列的 [AssetType] 即使编译进了
// 程序集也不会出现在「添加资源」菜单里。
//
// ⚠️ 命名空间不要叫 ...Plugin（会遮蔽 Warudo.Core.Plugins.Plugin 基类，报 CS0118）。

using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

namespace HoWarudoModTests.CustomAsset
{
    [PluginType(
        Id = "howarudomodtests.customasset",
        Name = "Ho Warudo Mod Tests - Custom Asset",
        Description = "Minimal custom asset type mod used to verify a new resource type shows up in Warudo.",
        Version = "1.0.0",
        Author = "Hollow",
        AssetTypes = new[]
        {
            typeof(HoTestCubeAsset),
            typeof(HoTestCounterAsset),
            typeof(HoTestFaceTrackerAsset)
        })]
    public class HoTestAssetPlugin : Plugin
    {
    }
}

// HoTestLightAsset.cs  -- LightAsset 基类（光照资源）
//
// 内置的 DirectionalLightAsset / PointLightAsset 都继承它。它是 abstract，
// 所以想加一种自己的灯就得派生。
//
// ✅ 探针实测，LightAsset 要求子类实现**两样**：
//       protected override GameObject CreateGameObject();
//       protected override bool       IsRangeSupported();     // 这盏灯有没有 Range 参数
//   其余（颜色 / 强度 / Range 等）基类都已经做成 [DataInput] 了。
//
// 注意：GameObject 上**必须挂 UnityEngine.Light 组件**，基类靠它把数据输入应用到灯上。

using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Plugins.Core.Assets.Environment;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "e4a70b15-6dc8-4f93-b14a-8c5d2f6a0e74",
        Title = "Ho Test Light",
        Category = "CATEGORY_DEBUG")]
    public class HoTestLightAsset : LightAsset
    {
        // 造一个点光源。选青色是为了跟场景里别的灯一眼区分开。
        protected override GameObject CreateGameObject()
        {
            var go = new GameObject("Ho Test Light");

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.25f, 0.85f, 1f);
            light.intensity = 3f;
            light.range = 5f;

            return go;
        }

        // 点光源有 Range，返回 true 基类才会显示 Range 数据输入。
        protected override bool IsRangeSupported()
        {
            return true;
        }
    }
}

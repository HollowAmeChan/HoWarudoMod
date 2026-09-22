// HoTestCubeAsset.cs  -- 自定义资源类型本体
//
// 【这是本 Mod 的全部意义】往 Warudo 的「添加资源」菜单里加一个新条目。
//
// [AssetType] 的四个参数（📖 官方文档 docs.warudo.app/zh/docs/scripting/api/assets）：
//   Id        必填。这个**类型**的唯一 GUID，不是实例的 UUID（实例是 asset.Id）。
//             每个新资源类型都要自己生成一个，不能和别人的撞。
//   Title     必填。显示在「添加资源」菜单里的名字。可以填本地化 key。
//   Category  选填。菜单里的分组，内置可用值见下面。
//   Singleton 选填。true = 这个资源在场景里只能有一个，默认 false。
//
// 常用内置 Category（📖 官方文档给的清单）：
//   CATEGORY_INPUT  CATEGORY_CHARACTERS  CATEGORY_PROP  CATEGORY_ACCESSORY
//   CATEGORY_ENVIRONMENT  CATEGORY_CINEMATOGRAPHY  CATEGORY_EXTERNAL_INTERACTION
//   CATEGORY_MOTION_CAPTURE
//   （本机字符串堆实测确认 Warudo.Plugins.Core.dll 里存在 CATEGORY_DEBUG 与
//     CATEGORY_MOTION_CAPTURE；CATEGORY_DEBUG 就是内置资源 FPSCounterAsset 用的那个）
//
// 基类选谁：
//   Warudo.Core.Scenes.Asset             最裸的资源，要自己管 GameObject 生死
//   Warudo.Plugins.Core.Assets.GameObjectAsset   ← 本文件用这个
//        它替你创建/销毁 GameObject，并且自带一个 Transform 数据输入，
//        用户可以像拖道具一样在场景里拖它、改位置旋转缩放。
//        只需 override CreateGameObject()。
//        📖 官方文档原话：「如果你的资源是『用户可以在场景里挪动的东西』，
//        那多半应该继承 GameObjectAsset」
//   Warudo.Plugins.Core.Assets.MotionCapture.GenericTrackerAsset
//        追踪器基类（自定义面捕追踪器的起点），见 README 的「未来」一节。
//
// 资源能有什么组件（📖 官方文档）：
//   数据输入 [DataInput] + 触发器 [Trigger]。
//   **资源没有 DataOutput，也没有 FlowInput / FlowOutput** —— 这点和节点不同。

using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Plugins.Core.Assets;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "a7c31f60-2d84-4b19-8f5e-6c9d0b7a4e33",
        Title = "Ho Test Cube",
        Category = "CATEGORY_DEBUG")]
    public class HoTestCubeAsset : GameObjectAsset
    {
        // 状态文字。给用户/我们一个「这个资源确实活着」的可读标志。
        [Markdown]
        public string Status = "Ho Test Cube is running.";

        // 每秒自转角度。设成 0 就静止。
        [DataInput]
        public float SpinSpeed = 90f;

        // 触发器：资源面板上的一个按钮。点一下把角度归零。
        [Trigger]
        public void ResetRotation()
        {
            if (GameObject != null)
            {
                GameObject.transform.rotation = Quaternion.identity;
            }
            Status = "Rotation reset.";
            BroadcastDataInput(nameof(Status));
        }

        // GameObjectAsset 唯一要我们实现的东西：返回一个 GameObject。
        // 这里用 Unity 内置立方体，这样不需要任何美术资产就能肉眼验收。
        protected override GameObject CreateGameObject()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Ho Test Cube";
            cube.transform.localScale = Vector3.one * 0.5f;
            return cube;
        }

        // 📖 资源的 OnUpdate() 每帧调用一次，相当于 Unity 的 Update()。
        public override void OnUpdate()
        {
            base.OnUpdate();
            if (GameObject == null)
            {
                return;
            }
            GameObject.transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.Self);
        }
    }
}

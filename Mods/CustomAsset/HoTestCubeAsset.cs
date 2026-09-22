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
        // 诊断用状态文字。同时暴露 active 状态和帧计数，一眼定性：
        //   frames 一直涨 -> OnUpdate() 在跑（已实测确认）
        //   rotY 一直变   -> 旋转真的落到画面上了
        [Markdown]
        public string Status = "starting...";

        // 每秒自转角度。设成 0 就静止。
        [DataInput]
        public float SpinSpeed = 90f;

        private int _frames;

        // ⚠️ 必须是 protected override，不能写 public override。
        //    官方文档示例写的是 `public override void OnCreate()`，但那只对直接继承
        //    `Asset` 成立；`GameObjectAsset` 把它收窄成了 protected，
        //    照抄文档会报 CS0507「重写 protected 继承成员时无法更改访问修饰符」。
        //    （这条是本地 Roslyn 编译实测出来的，文档没说。）
        //
        // 📖 官方文档原话：资源创建后**默认不是 active 的**（"By default, assets are
        // NOT active when they are created"），官方给的写法就是在 OnCreate 里显式置位。
        protected override void OnCreate()
        {
            base.OnCreate();
            SetActive(true);
            Status = "active=" + Active;
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

        // 触发器：资源面板上的一个按钮。点一下把角度归零。
        [Trigger]
        public void ResetRotation()
        {
            var zeroed = Transform.Rotation;
            zeroed.y = 0f;
            Transform.Rotation = zeroed;
            Status = "rotation reset.";
            BroadcastDataInput(nameof(Status));
        }

        // 📖 资源的 OnUpdate() 每帧调用一次，相当于 Unity 的 Update()。
        //
        // ★★★ 本文件最关键的一行知识 ★★★
        //
        // 旋转**必须写进资源自己的 `Transform` 数据输入**，不能直接转 GameObject。
        //
        // GameObjectAsset 的公开字段（本机元数据转储实测）：
        //     [DataInput] bool          Enabled
        //     [DataInput] TransformData Transform
        // 而 TransformData 是：
        //     [DataInput] Vector3 Position
        //     [DataInput] Vector3 Rotation   ← 欧拉角，单位是度
        //     [DataInput] Vector3 Scale
        //
        // GameObjectAsset 每帧都拿这个数据输入回写 GameObject 的 transform。
        // 所以 `GameObject.transform.Rotate(...)` 会在同一帧被覆盖掉，
        // 表现就是「平时不转，只有手动拖动（交互期间）才转」——
        // 这正是本目录第一版的实测症状，也是它唯一的 bug。
        public override void OnUpdate()
        {
            base.OnUpdate();

            if (Transform == null)
            {
                return;
            }

            var euler = Transform.Rotation;
            euler.y = Mathf.Repeat(euler.y + SpinSpeed * Time.deltaTime, 360f);
            Transform.Rotation = euler;

            _frames++;
            if (_frames % 30 == 0)
            {
                Status = "active=" + Active + "  frames=" + _frames
                         + "  rotY=" + euler.y.ToString("F0");
                BroadcastDataInput(nameof(Status));
            }
        }
    }
}

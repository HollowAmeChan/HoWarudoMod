// HoTestCounterAsset.cs  -- 最裸的资源基类 Warudo.Core.Scenes.Asset
//
// 【为什么还要一个「什么都没继承」的资源】
// 前面那个立方体继承的是 GameObjectAsset，它替我们管了 GameObject 和 Transform。
// 但 Warudo 里绝大多数**逻辑型资源**（状态机、计时器、连接器、数据源）根本不碰
// GameObject —— 它们只想要「一个能存数据、能被蓝图读到、能响应触发器、能每帧跑」
// 的东西。那种资源就直接继承最裸的 `Asset`。
//
// 所以这个文件是**没有 GameObject 的资源**的最小样本，也是以后写自定义逻辑资源的模板。
//
// ─────────────────────────────────────────────────────────────
// 三种基类的分工（本目录三个文件正好各覆盖一个）
//
//   Warudo.Core.Scenes.Asset                        纯逻辑，自己管 GameObject（本文件）
//   Warudo.Plugins.Core.Assets.GameObjectAsset      有实体、用户能拖（HoTestCubeAsset）
//   ...Assets.MotionCapture.GenericTrackerAsset     追踪器（HoTestFaceTrackerAsset）
//
// ─────────────────────────────────────────────────────────────
// 数据输入 / 触发器 / 说明文字
//
//   资源**只有** [DataInput] 和 [Trigger]。
//   资源**没有** [DataOutput]，也**没有** [FlowInput] / [FlowOutput] —— 这点和蓝图节点不同。
//   （想在蓝图里读取资源的值，用蓝图节点 `Get Asset Property`，或者自己写节点。）
//
// Warudo 的 UI 提示类属性（✅ 本机在 Warudo.Core.dll 的字符串堆里确认存在）：
//   [Markdown]             一段只读文本
//   [FloatSlider] [IntegerSlider]      滑条
//   [Section] [SectionHiddenIf]        分组
//   [HiddenIf] [DisabledIf]            条件显隐
//   [MultilineInput] [AutoComplete] [CardSelect] [Label] [Description] ...
//   本文件先用已验证过的最小子集：[DataInput] [Trigger] [Markdown] + order 参数。

using Warudo.Core.Attributes;
using Warudo.Core.Scenes;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "b1d47e82-3a95-4c60-8e17-5f2a9c3d7b41",
        Title = "Ho Test Counter",
        Category = "CATEGORY_DEBUG")]
    public class HoTestCounterAsset : Asset
    {
        // 只读说明文字。
        [Markdown]
        public string Status = "not started";

        // 目标计数值。
        [DataInput]
        public int Target = 10;

        // 计数间隔（秒）。
        [DataInput]
        public float Interval = 1f;

        // 当前计数。也能手改。
        [DataInput]
        public int Count;

        [Trigger]
        public void Reset()
        {
            Count = 0;
            _elapsed = 0f;
            Status = "reset";
            BroadcastDataInput(nameof(Status));
        }

        [Trigger]
        public void Step()
        {
            Count++;
            BroadcastAll();
        }

        private float _elapsed;

        // ⚠️ 必须是 **protected** override。
        //    官方文档 Assets 页的示例写的是 `public override void OnCreate()`，
        //    那是旧版本的写法；本机实测 0.15.0 里它定义在 `Entity.OnCreate()` 上且是
        //    protected，照抄文档会报 CS0507。
        //    —— 和 GameObjectsAsset 那条坑是同一个坑，见 HoTestCubeAsset.cs。
        protected override void OnCreate()
        {
            base.OnCreate();

            // 📖 官方文档：资源创建后默认不是 active 的。纯逻辑资源尤其要自己置位，
            //    否则它不会被认为是「就绪」的。
            SetActive(true);
            Status = "running";
        }

        // 每帧跑。没有 GameObject 也照样会被调用。
        public override void OnUpdate()
        {
            base.OnUpdate();

            if (Interval <= 0f)
            {
                return;
            }

            _elapsed += UnityEngine.Time.deltaTime;
            if (_elapsed < Interval)
            {
                return;
            }
            _elapsed = 0f;

            Count++;
            if (Count >= Target)
            {
                Count = Target;
                Status = "reached target " + Target;
                BroadcastDataInput(nameof(Status));
                return;
            }

            BroadcastAll();
        }

        // 把数据刷到 Warudo 编辑器 UI 上。
        // 纯逻辑资源没有 GameObject，改值**必须**自己 Broadcast，UI 才会跟着动。
        private void BroadcastAll()
        {
            BroadcastDataInput(nameof(Count));
            BroadcastDataInput(nameof(Status));
        }
    }
}

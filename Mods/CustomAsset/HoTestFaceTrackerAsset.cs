// HoTestFaceTrackerAsset.cs  -- 我们自己的面捕追踪器资源
//
// 【这是干什么的】
// 一个自定义**面部追踪器**。它出现在「添加资源」的 CATEGORY_MOTION_CAPTURE 分组里，
// 加进场景后指定 Character，就能往角色脸上灌混合形状数据。
//
// 现在它输出的是**合成正弦数据**（不接任何硬件），用来把整条链路验通：
//   添加资源 -> 指定角色 -> 脸动
// 接真硬件时，只要把 UpdateRawData() 里的赋值换成你的数据源即可，其余都不用动。
//
// ─────────────────────────────────────────────────────────────
// 基类：Warudo.Plugins.Core.Assets.MotionCapture.GenericTrackerAsset
//       (它继承自 ...Assets.Character.CharacterDaemonAsset)
//
// ✅ 本机元数据转储实测，基类**已经自带**一大堆 [DataInput]，不用我们自己写：
//     MirroredTracking / BlendShapeSensitivity / HeadMovementIntensity
//     MaximalHeadTranslation / BodyMovementIntensity / BodyRotationType
//     BodyRotation* / HeadRotationIntensity / HeadRotationOffset
//     EyeMovementIntensity / EyeMovementHeadRotationCompensation
//     EyeBlinkSensitivity / LinkedEyeBlinking / BlendShapesMapping ...
//   还自带可读的 IsTracked / LatestBlendShapes / LatestHeadPosition 等。
//
// 我们只需要提供两样东西：
//   1) InputBlendShapes —— 本追踪器能提供哪些混合形状（名字必须是 Warudo 认的）
//   2) UpdateRawData()  —— 每帧把原始数据填进 Raw* 系列
//
// 可选的 protected override（VMC 那个官方例子里用了前三个）：
//     UseHeadIK / UseCharacterDaemon / CanCalibrate / UseCharacterDaemonBones
//     ProvideHeadTracking / UseEyeInputs / IsInputHeadTransformMirrored
//     EyeMovementIntensityBaseMultiplier
//
// ─────────────────────────────────────────────────────────────
// 混合形状名字从哪来（这条是熬出来的，别改）
//
// Warudo 认的名字是 ARKit 那 52 个的**小驼峰**写法：
//     jawOpen  eyeBlinkLeft  eyeBlinkRight  mouthSmileLeft  browInnerUp  noseSneerLeft ...
//
// ✅ 证据：Warudo.Plugins.Core.dll 的 **UTF-16 字符串字面量堆**里就是这批名字。
//    （注意：元数据里的类型/字段名是 UTF-8，字符串字面量是 UTF-16，
//      所以用 ASCII 扫 DLL 是看不到它们的 —— 这个坑我们踩过。）
//
// 不要手抄这份清单。基类提供了现成的：
//     Warudo.Plugins.Core.Utils.BlendShapes.ARKitBlendShapeNames   (public static string[])

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Plugins.Core.Assets.MotionCapture;
using Warudo.Plugins.Core.Utils;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "d3f69a04-5cb7-4e82-a039-7b4c1e5f9d63",
        Title = "Ho Test Face Tracker",
        Category = "CATEGORY_MOTION_CAPTURE")]
    public class HoTestFaceTrackerAsset : GenericTrackerAsset
    {
        // 资源面板上的说明文字
        [Markdown]
        public string TestDataStatus = "输出合成正弦数据：张嘴 + 眨眼。接真硬件时改 UpdateRawData()。";

        // 合成数据的快慢。0 = 冻住。
        [DataInput]
        public float TestSpeed = 1f;

        private float _time;

        // ── 基类要求实现/可覆盖的东西 ───────────────────────────────

        // 本追踪器不接管角色骨骼，只给混合形状。
        //
        // ⚠️ 这三个必须写 **public** override。
        //    官方 VMC 示例（WarudoPluginExamples/VMC/Assets/VMCReceiverAsset.cs）写的是
        //    `protected override`，那是 0.14.x 的写法；0.15.0 里
        //    GenericTrackerAsset.UseHeadIK / UseCharacterDaemon / CanCalibrate 都是 **public**，
        //    照抄官方示例会报 CS0507「重写 public 继承成员时无法更改访问修饰符」。
        //    （本地 Roslyn 编译实测，文档与示例都还没跟上。）
        public override bool UseHeadIK => false;

        // 不走 CharacterDaemon 那条骨骼通道。
        public override bool UseCharacterDaemon => false;

        // 没有校准流程。
        public override bool CanCalibrate => false;

        // 本追踪器能提供哪些混合形状。
        // 用基类现成的 ARKit 清单，不手抄 —— 抄错一个名字那一路就静默失效。
        public override List<string> InputBlendShapes => BlendShapes.ARKitBlendShapeNames.ToList();

        // ── 每帧填数据 ──────────────────────────────────────────────

        // Raw* 系列是本追踪器的「原始输入」，基类会拿去做强度/灵敏度/校准处理，
        // 结果放在可读的 Latest* 系列里。
        //
        // 返回 false 表示「这一帧没有数据」。
        protected override bool UpdateRawData()
        {
            _time += Time.deltaTime * TestSpeed;

            // 张嘴：2 秒一个来回，映射到 0..1
            RawBlendShapes["jawOpen"] = (Mathf.Sin(_time * Mathf.PI) + 1f) * 0.5f;

            // 眨眼：每 3 秒眨 0.12 秒
            var blinking = Mathf.Repeat(_time, 3f) < 0.12f ? 1f : 0f;
            RawBlendShapes["eyeBlinkLeft"] = blinking;
            RawBlendShapes["eyeBlinkRight"] = blinking;

            // 笑：8 秒一个来回，幅度小一点
            var smile = (Mathf.Sin(_time * Mathf.PI / 4f) + 1f) * 0.25f;
            RawBlendShapes["mouthSmileLeft"] = smile;
            RawBlendShapes["mouthSmileRight"] = smile;

            return true;
        }
    }
}

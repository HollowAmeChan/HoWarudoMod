// HoTestFullBodyTrackerAsset.cs  -- GenericFullBodyTrackerAsset 基类（全身/姿态追踪器）
//
// 和面捕追踪器（HoTestFaceTrackerAsset）是亲兄弟，只差基类：
//   面捕 -> GenericTrackerAsset
//   全身 -> GenericFullBodyTrackerAsset : GenericTrackerAsset
//
// ✅ 探针实测：GenericFullBodyTrackerAsset 只要求子类实现 **UpdateRawData()** 一个东西。
//    （UseHeadIK / UseCharacterDaemon / UseCharacterDaemonBones / CanCalibrate /
//      UseEyeInputs / ProvideHeadTracking 它都已经实现了，不用管。）
//
// 和面捕的区别：这个还驱动**骨骼**，数据填进 RawBoneRotations / RawBonePositions，
// 下标就是 UnityEngine.HumanBodyBones 的枚举值。
//
// 这里同样输出合成正弦数据：两只大臂前后摆，肉眼能直接看出来。

using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Plugins.Core.Assets.MotionCapture;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "06c92d37-8fea-41b5-d36c-ae7f4b8c2096",
        Title = "Ho Test Pose Tracker",
        Category = "CATEGORY_MOTION_CAPTURE")]
    public class HoTestFullBodyTrackerAsset : GenericFullBodyTrackerAsset
    {
        [Markdown]
        public string TestDataStatus = "输出合成正弦骨骼数据：两只大臂前后摆。接真硬件时改 UpdateRawData()。";

        [DataInput]
        public float TestSpeed = 1f;

        private float _time;

        protected override bool UpdateRawData()
        {
            _time += Time.deltaTime * TestSpeed;

            // 2 秒一个来回，摆幅 ±60 度
            var swing = Mathf.Sin(_time * Mathf.PI) * 60f;

            SetBoneRotation(HumanBodyBones.LeftUpperArm, Quaternion.Euler(swing, 0f, 0f));
            SetBoneRotation(HumanBodyBones.RightUpperArm, Quaternion.Euler(-swing, 0f, 0f));

            return true;
        }

        // RawBoneRotations 是按 HumanBodyBones 下标索引的数组，基类负责分配。
        // 越界不写，免得把别的骨骼搞坏。
        private void SetBoneRotation(HumanBodyBones bone, Quaternion rotation)
        {
            var rotations = RawBoneRotations;
            if (rotations == null)
            {
                return;
            }

            var index = (int)bone;
            if (index < 0 || index >= rotations.Length)
            {
                return;
            }

            rotations[index] = rotation;
        }
    }
}

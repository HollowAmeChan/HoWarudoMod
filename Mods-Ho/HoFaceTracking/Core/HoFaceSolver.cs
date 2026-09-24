// HoFaceSolver.cs  --  控制求解：**从参数反求动画输出**（零配置）
//
// 【它是什么】「HoFace控制求解」节点的引擎：把一份"参数名 → 值"的字典，反求成 Warudo 官方那 5 个口要的形状
//   · 融合形状字典（`BlendShapes`：键 = 规范名）
//   · 头姿（`Head/RotX|Y|Z` 三个保留名 → `Quaternion.Euler`，单位是**度**）
//   · 头位（`Head/PosX|Y|Z`，米）、根位（`Root/PosX|Y|Z`，米）
//   · 骨骼旋转偏移数组（按 `HumanBodyBones` 索引，**我们只有脸 → 只写 `Head`，其余 identity**）
//
// 【为什么单独一个文件/一个节点（2026-09-25 拆的）】
//   原来的「Ho Face 处理链」把两件事混在一起：**改名/量纲**（读 `*.hoface.json`，是配置驱动的）与
//   **反求动画输出**（纯装配）。拆开之后：
//     · 参数处理那一半要配置、只吐一份字典；
//     · 这一半**零配置** —— 于是别的来源（VB 走 VTS 服务端模式、以后别的面捕源）可以**跳过参数处理**
//       直接喂这里，这正是拆分的全部意义。
//   ⚠️ **红线：别往这里塞配置**（平滑/曲线/映射表都不行）。一旦有配置，"跳过参数处理"就不成立了。
//
// 【接口（两层之间唯一的约定）】
//   键：**裸规范名**（`JawOpen` / `EyeBlinkLeft` …）—— 与官方 `BlendShapes` 字典同形；
//       外加 9 个保留名（见 `Targets`）。
//   缺键 = 中性：字典里没有的键按 0 / identity 处理（例如 VB 那条路不给 `Head/RotX`，
//   头姿就是 identity，头/根由 VB 自己那边的骨骼输出承担）。**这是承诺，不是巧合。**
//   保留名以外的键**原样进 `BlendShapes`**：官方那个应用节点只认角色身上真有的形态键，
//   所以"配置里多算出来的参数"（比如眼睑两根轴）传下去是安全的、而且将来混合树要用它们。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoFaceTracking.Core
{
    public sealed class HoFaceSolver
    {
        /// <summary>
        /// 输出的口径表 —— 与官方接收器同形的那 5 个口就靠这张表：
        /// `ARKit/` 前缀在**参数处理**那一层已经去掉，这里只留"保留名"这一套。
        /// </summary>
        public static class Targets
        {
            /// <summary>头部欧拉角，单位**度**，顺序 X→Y→Z（即 <c>Quaternion.Euler(x, y, z)</c>）。</summary>
            public const string HeadRotX = "Head/RotX";
            public const string HeadRotY = "Head/RotY";
            public const string HeadRotZ = "Head/RotZ";

            /// <summary>头部位置，单位**米**，相对角色根。</summary>
            public const string HeadPosX = "Head/PosX";
            public const string HeadPosY = "Head/PosY";
            public const string HeadPosZ = "Head/PosZ";

            /// <summary>根位置，单位**米**。</summary>
            public const string RootPosX = "Root/PosX";
            public const string RootPosY = "Root/PosY";
            public const string RootPosZ = "Root/PosZ";

            /// <summary>表里所有保留名（面板上要展示这张表）。</summary>
            public static readonly string[] Fixed = {
                HeadRotX, HeadRotY, HeadRotZ,
                HeadPosX, HeadPosY, HeadPosZ,
                RootPosX, RootPosY, RootPosZ
            };
        }

        /// <summary>拼装出来的融合形状字典（键 = 规范名）。直接给节点当端口值。</summary>
        public readonly Dictionary<string, float> BlendShapes = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>头部欧拉角（度）。三个保留名都没出现时这个四元数是 identity。</summary>
        public Quaternion HeadRotation = Quaternion.identity;

        /// <summary>头部位置（米）。</summary>
        public Vector3 HeadPosition = Vector3.zero;

        /// <summary>根位置（米）。</summary>
        public Vector3 RootPosition = Vector3.zero;

        /// <summary>按 <see cref="HumanBodyBones"/> 索引的骨骼旋转偏移，默认全是 identity。</summary>
        public Quaternion[] BoneRotations = NewBoneRotations();

        /// <summary>`HumanBodyBones` 里最后一个枚举值 —— 骨骼数组按它开。</summary>
        private static readonly int BoneCount = (int)HumanBodyBones.LastBone;

        /// <summary>这一帧的参数（求解时用；保留引用只是为了少传参数）。</summary>
        private Dictionary<string, float> _parameters;

        /// <summary>
        /// 走一帧：把参数字典反求成上面那几样。**原地更新**，不分配新数组。
        /// <paramref name="parameters"/> 为 null 时等于空字典（全部按中性）。
        /// </summary>
        public void Solve(Dictionary<string, float> parameters)
        {
            _parameters = parameters;

            BlendShapes.Clear();
            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    if (pair.Key == null) continue;
                    if (IsReserved(pair.Key)) continue;          // 保留名不进融合形状字典
                    BlendShapes[pair.Key] = pair.Value;
                }
            }

            HeadRotation = Quaternion.Euler(
                Reserved(Targets.HeadRotX), Reserved(Targets.HeadRotY), Reserved(Targets.HeadRotZ));
            HeadPosition = new Vector3(
                Reserved(Targets.HeadPosX), Reserved(Targets.HeadPosY), Reserved(Targets.HeadPosZ));
            RootPosition = new Vector3(
                Reserved(Targets.RootPosX), Reserved(Targets.RootPosY), Reserved(Targets.RootPosZ));

            // 骨骼数组只写头：我们只有脸。其余保持 identity（= 不改那根骨头）。
            for (int i = 0; i < BoneRotations.Length; i++) BoneRotations[i] = Quaternion.identity;
            BoneRotations[(int)HumanBodyBones.Head] = HeadRotation;
        }

        /// <summary>保留名这一帧的值；没有这个键时 0（等于"不改"）。</summary>
        private float Reserved(string name)
        {
            float value;
            return _parameters != null && _parameters.TryGetValue(name, out value) ? value : 0f;
        }

        public static bool IsReserved(string name)
        {
            for (int i = 0; i < Targets.Fixed.Length; i++)
                if (Targets.Fixed[i] == name) return true;
            return false;
        }

        private static Quaternion[] NewBoneRotations()
        {
            var array = new Quaternion[BoneCount];
            for (int i = 0; i < array.Length; i++) array[i] = Quaternion.identity;
            return array;
        }
    }
}

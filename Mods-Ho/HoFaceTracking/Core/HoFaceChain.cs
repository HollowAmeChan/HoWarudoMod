// HoFaceChain.cs  --  中间层的**运行期求值器**（Warudo 侧）
//
// 【为什么要它】
// 中间层原来打算靠"影子 Animator + 混合树 .controller"求值。走不通，两条路都堵死：
//   · 插件 Mod 不能用 System.IO，也不能带已编译资源；
//   · Unity 播放器**不能从文件加载 AnimatorController**（只有 AssetBundle 能），
//     所以"把 controller 放进沙箱目录再读"也不成立。
// 于是混合树改成**数据**（行 = 表达式 + 曲线 + 有序修饰符），由这里求值。
//
// 【它和编辑器那边是同一个人】
// 输入行/输出行的语义、缺键保持、修饰符顺序、曲线端点夹取，全部由 Core/ 下那几份
// **从 HoUnityTools 逐字搬来**的文件实现（见 Core/PORTED.md）。这里只补它们没有的
// 那部分：**没有 Animator、没有代理渲染器、没有通道模式** —— 也就是纯求值。
// 求值顺序与 HoFaceAnimationSession.Tick 一致：输入行 → 输出行（表达式 → 曲线 → 修饰符）。
//
// 【和编辑器版的唯一差别：变量只认两层】
// 编辑器那边是三层（形态键通道 → 输入行 → 合并后的原始线名），因为通道上还挂着
// "模式 / 输入曲线 / 断流回中性"那套校准。Warudo 侧**没有通道**（那套是设备校准，
// 留在 Unity 里），所以只剩：
//   ① **输入行的结果**（`jawOpen`、`headRotX`…；这一帧没来就**保持上一帧**）
//   ② **原始线名**（`JawOpen`、`head_0`…）—— 想绕过输入行直接用原值也允许
// 未知名字按 0（求值器不抛异常）。
//
// 【输出的去向：保留名】
// 行全是标量，而官方节点要吃 `Dictionary<string,float>`、`Quaternion[]`、`Vector3`。
// 中间加一层"保留参数名"来拼装（**只有这一张固定表**，没有别的隐式规则）：
//   ARKit/<键>              → BlendShapes 字典（键一律换成 52 个规范名之一）
//   Head/RotX|RotY|RotZ     → 头部欧拉角（**度**）→ BoneRotations[Head]
//   Head/PosX|PosY|PosZ     → HeadPosition（**米**）
//   Root/PosX|PosY|PosZ     → RootPosition（**米**）
// 其它参数名照样求值，只是**发不出去** —— 不静默吞掉，节点会把它们列出来。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoFaceTracking.Core
{
    /// <summary>处理链的运行期实例。一个配置文件一个实例，节点每帧调 <see cref="Evaluate"/>。</summary>
    public sealed class HoFaceChain
    {
        /// <summary>
        /// 保留参数名 —— **唯一的输出拼装表**。左值是行的 `parameter`，右值是它落到哪个端口。
        /// 不在这张表里、又不是 `ARKit/` 开头的参数名，就是"求值了但发不出去"。
        /// </summary>
        public static class Targets
        {
            public const string Arkit = "ARKit/";

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

            /// <summary>三轴一组的保留名，按 X→Y→Z。</summary>
            public static readonly string[] HeadRotation = { HeadRotX, HeadRotY, HeadRotZ };
            public static readonly string[] HeadPosition = { HeadPosX, HeadPosY, HeadPosZ };
            public static readonly string[] RootPosition = { RootPosX, RootPosY, RootPosZ };

            /// <summary>表里所有非 <c>ARKit/</c> 的名字（面板上要展示这张表）。</summary>
            public static readonly string[] Fixed = {
                HeadRotX, HeadRotY, HeadRotZ,
                HeadPosX, HeadPosY, HeadPosZ,
                RootPosX, RootPosY, RootPosZ
            };
        }

        /// <summary>拼装出来的融合形状字典（键 = 52 个规范名之一）。直接给节点当端口值。</summary>
        public readonly Dictionary<string, float> BlendShapes = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>头部欧拉角（度）。三个保留名都没出现时这个四元数是 identity。</summary>
        public Quaternion HeadRotation = Quaternion.identity;

        /// <summary>头部位置（米）。</summary>
        public Vector3 HeadPosition = Vector3.zero;

        /// <summary>根位置（米）。</summary>
        public Vector3 RootPosition = Vector3.zero;

        /// <summary>按 <see cref="HumanBodyBones"/> 索引的骨骼旋转，默认全是 identity。</summary>
        public Quaternion[] BoneRotations = new Quaternion[BoneCount];

        /// <summary>有几行输出（面板显示用）。</summary>
        public int OutputRowCount { get { return outputs.Length; } }

        /// <summary>有几行输入（面板显示用）。</summary>
        public int InputRowCount { get { return inputRows.Length; } }

        /// <summary>
        /// 编译期的问题（表达式解析失败、修饰符 kind 不认得、发不出去的参数名）。
        /// 逐帧求值**不看它** —— 它只用来在面板上点名。
        /// </summary>
        public string Error { get; private set; }

        /// <summary>`HumanBodyBones` 里最后一个枚举值 —— 骨骼数组按它开。</summary>
        private static readonly int BoneCount = (int)HumanBodyBones.LastBone;

        // ── 编译好的行（表达式解析一次，状态数组按行开）────────────────────────
        private readonly HoFaceOutput[] inputRows;
        private readonly HoFaceExpression[] inputExpressions;
        private readonly float[] inputValues;
        private readonly bool[] inputFresh;
        private readonly float[] inputSmooth;
        private readonly int[] inputStepIndex;
        private readonly double[] inputStepUntil;
        private readonly Dictionary<string, int> inputIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly HoFaceOutput[] outputs;
        private readonly HoFaceExpression[] expressions;
        private readonly float[] outputValues;
        private readonly float[] outputSmooth;
        private readonly int[] stepIndex;
        private readonly double[] stepUntil;
        private readonly Dictionary<string, int> reservedRow = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly int[] arkitRow = new int[HoFaceTrackingChannels.Names.Length];

        /// <summary>表达式引用到的变量名（草稿：每行求值时攒一遍，见 <see cref="Evaluate"/>）。</summary>
        private readonly List<string> touched = new List<string>();

        private readonly List<string> unemitted = new List<string>();
        private bool primed;

        /// <summary>这一帧的原始线名（<see cref="Lookup"/> 的第二层回退要它）。</summary>
        private Dictionary<string, float> current;

        /// <summary>这份链编译自哪套中间层（面板显示用）。</summary>
        public readonly HoFaceMiddleware Middleware;

        public HoFaceChain(HoFaceMiddleware middleware)
        {
            Middleware = middleware ?? new HoFaceMiddleware();
            Error = null;

            var inputList = Middleware.inputs ?? new List<HoFaceOutput>();
            inputRows = new HoFaceOutput[inputList.Count];
            inputExpressions = new HoFaceExpression[inputList.Count];
            inputValues = new float[inputList.Count];
            inputFresh = new bool[inputList.Count];
            inputSmooth = new float[inputList.Count];
            inputStepIndex = new int[inputList.Count];
            inputStepUntil = new double[inputList.Count];
            for (int i = 0; i < inputStepIndex.Length; i++) inputStepIndex[i] = -1;
            for (int i = 0; i < inputList.Count; i++)
            {
                inputRows[i] = inputList[i];
                if (inputList[i] == null || string.IsNullOrEmpty(inputList[i].parameter)) continue;
                HoFaceExpression parsed;
                string parseError;
                if (HoFaceExpression.TryParse(inputList[i].expression, out parsed, out parseError))
                    inputExpressions[i] = parsed;
                else
                    Note("第 " + (i + 1) + " 条输入行的表达式用不了（" + inputList[i].parameter + "）：" + parseError);
                inputIndex[inputList[i].parameter] = i;   // 同名多行：最后一行生效（用户覆盖用）
            }

            var rows = Middleware.outputs ?? new List<HoFaceOutput>();
            outputs = new HoFaceOutput[rows.Count];
            expressions = new HoFaceExpression[rows.Count];
            outputValues = new float[rows.Count];
            outputSmooth = new float[rows.Count];
            stepIndex = new int[rows.Count];
            stepUntil = new double[rows.Count];
            for (int i = 0; i < stepIndex.Length; i++) stepIndex[i] = -1;   // −1 = 还没进任何档
            for (int i = 0; i < arkitRow.Length; i++) arkitRow[i] = -1;

            for (int i = 0; i < rows.Count; i++)
            {
                outputs[i] = rows[i];
                if (rows[i] == null || string.IsNullOrEmpty(rows[i].parameter)) continue;

                HoFaceExpression parsed;
                string parseError;
                if (HoFaceExpression.TryParse(rows[i].expression, out parsed, out parseError))
                    expressions[i] = parsed;
                else
                    Note("第 " + (i + 1) + " 行的表达式用不了（" + rows[i].parameter + "）：" + parseError);

                if (IsReserved(rows[i].parameter))
                {
                    if (!reservedRow.ContainsKey(rows[i].parameter)) reservedRow[rows[i].parameter] = i;
                }
                else if (rows[i].parameter.StartsWith(Targets.Arkit, StringComparison.Ordinal))
                {
                    string shape = rows[i].parameter.Substring(Targets.Arkit.Length);
                    int channel = HoFaceTrackingChannels.IndexOf(shape);   // 认 `_L/_R` 别名，键一律换成规范名
                    if (channel >= 0) arkitRow[channel] = i;
                    else Note("不认识的融合形状（第 " + (i + 1) + " 行）：" + rows[i].parameter);
                }
                else
                {
                    if (!unemitted.Contains(rows[i].parameter)) unemitted.Add(rows[i].parameter);
                }
            }

            if (unemitted.Count > 0)
                Note("以下行算出来了、但这一版**不发给角色**（既不是 ARKit/ 开头、也不在保留表里）："
                    + string.Join("、", unemitted.ToArray())
                    + " —— 这不是错：在用控制器（混合树）的版本里，这些正是喂给树的参数（比如眼睑那两根轴）；"
                    + "当前版本还没有树，所以它们只算不输出。");

            BoneRotations = NewBoneRotations();
        }

        /// <summary>
        /// 走一帧。**必须先调用它，再读任何结果** —— 结果都是原地更新的。
        /// </summary>
        /// <param name="rawValues">手机发来的"线名 → 原值"。null 当空字典（所有输入行都保持上一帧）。</param>
        /// <param name="deltaTime">平滑修饰符用的步长（秒）。</param>
        /// <param name="now">分档修饰符用的时刻（秒，单调递增即可）。</param>
        public void Evaluate(Dictionary<string, float> rawValues, float deltaTime, double now)
        {
            current = rawValues;
            EvaluateInputs(rawValues, deltaTime, now);
            EvaluateOutputs(deltaTime, now);
            Assemble(rawValues);
            primed = true;
        }

        /// <summary>
        /// 求值输入行：<c>规范名 = 曲线(表达式(线名…))</c>。
        ///
        /// **缺键语义**（与 VBridger 一致）：表达式引用到的线名只要有一个这一帧没来，这一行就**不写** ——
        /// 值保持上一帧。这条规矩让"两种协议的输入行同时存在"变成安全操作：哪个源在发，只有那一套行会动。
        /// </summary>
        private void EvaluateInputs(Dictionary<string, float> rawValues, float deltaTime, double now)
        {
            for (int row = 0; row < inputRows.Length; row++)
            {
                var expression = inputExpressions[row];
                if (expression == null) { inputFresh[row] = false; continue; }

                touched.Clear();
                float value = expression.Evaluate(name =>
                {
                    touched.Add(name);
                    return Raw(rawValues, name);
                });

                bool present = touched.Count > 0;
                for (int i = 0; i < touched.Count; i++)
                    if (!Has(rawValues, touched[i])) { present = false; break; }
                if (!present) { inputFresh[row] = false; continue; }

                value = inputRows[row].Transform(value);
                value = ApplyModifiers(row, inputRows[row], value, deltaTime, now,
                    inputSmooth, inputStepIndex, inputStepUntil);
                inputValues[row] = value;
                inputFresh[row] = true;
            }
        }

        /// <summary>求值输出行：表达式 → 曲线 → 有序修饰符。</summary>
        private void EvaluateOutputs(float deltaTime, double now)
        {
            for (int row = 0; row < outputs.Length; row++)
            {
                var output = outputs[row];
                if (output == null) { outputValues[row] = 0f; continue; }
                float value = expressions[row] != null ? expressions[row].Evaluate(Lookup) : 0f;
                value = output.Transform(value);
                outputValues[row] = ApplyModifiers(row, output, value, deltaTime, now);
            }
        }

        /// <summary>把标量行拼成三个端口要的形状。**原地更新**，不分配新数组。</summary>
        private void Assemble(Dictionary<string, float> rawValues)
        {
            BlendShapes.Clear();
            for (int channel = 0; channel < arkitRow.Length; channel++)
                if (arkitRow[channel] >= 0)
                    BlendShapes[HoFaceTrackingChannels.Names[channel]] = outputValues[arkitRow[channel]];

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

        /// <summary>保留名这一帧的值；没有这一行时 0（等于"不改"）。</summary>
        private float Reserved(string name)
        {
            int row;
            return reservedRow.TryGetValue(name, out row) ? outputValues[row] : 0f;
        }

        /// <summary>
        /// 表达式取变量，两层（越靠前越"规范"）：
        /// ① **输入行的结果**（`jawOpen`、`headRotX`…）—— 这一帧没来就是上一帧的值；
        /// ② **原始线名**（`JawOpen`、`head_0`…）。
        /// 未知名字按 0（表达式求值器不抛异常）。
        /// </summary>
        private float Lookup(string name)
        {
            if (name == null) return 0f;
            int row;
            if (inputIndex.TryGetValue(name, out row)) return inputValues[row];
            return Raw(current, name);
        }

        /// <summary>原始线名原值；没来就是 0（**只给输入行用**，输出行的变量走 <see cref="Lookup"/>）。</summary>
        private static float Raw(Dictionary<string, float> rawValues, string name)
        {
            float value;
            return rawValues != null && name != null && rawValues.TryGetValue(name, out value) ? value : 0f;
        }

        private static bool Has(Dictionary<string, float> rawValues, string name)
        {
            return rawValues != null && name != null && rawValues.ContainsKey(name);
        }

        /// <summary>
        /// 有序修饰符。按列出顺序生效（照 VBridger 的输出修饰符）：平滑 / 分档（延迟**还没实现**）。
        /// 输入行与输出行共用这一套实现，只是状态数组各带一份。
        /// </summary>
        private float ApplyModifiers(int row, HoFaceOutput output, float value, float deltaTime, double now) =>
            ApplyModifiers(row, output, value, deltaTime, now, outputSmooth, stepIndex, stepUntil);

        private float ApplyModifiers(int row, HoFaceOutput output, float value, float deltaTime, double now,
            float[] smooth, int[] stepRows, double[] stepUntilRows)
        {
            if (output.modifiers == null || output.modifiers.Count == 0) return value;
            for (int i = 0; i < output.modifiers.Count; i++)
            {
                var modifier = output.modifiers[i];
                if (modifier == null || !modifier.Active) continue;
                switch (modifier.kind)
                {
                    case HoFaceModifierKind.Smooth:
                        // 只有**第一帧**做一次性初始化，免得开场从 0 扫过来。
                        smooth[row] = !primed
                            ? value
                            : Mathf.Lerp(smooth[row], value, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / modifier.seconds));
                        value = smooth[row];
                        break;
                    case HoFaceModifierKind.Steps:
                        value = Step(row, modifier, value, now, stepRows, stepUntilRows);
                        break;
                    default:
                        break;   // 延迟：数据留位，未实现
                }
            }

            return value;
        }

        /// <summary>
        /// 分档：参数过 <c>trigger</c> 就跳到 <c>target</c>，往下掉超过 <c>threshold</c> 才退回去，
        /// 触发后至少保持 <c>hold</c> 秒。没触发任何档时输出 0（等于隐含的"最小档"）。
        /// </summary>
        private float Step(int row, HoFaceModifier modifier, float value, double now, int[] stepRows, double[] stepUntilRows)
        {
            var steps = modifier.steps;
            if (steps == null || steps.Count == 0) return value;

            int next = -1;
            for (int i = 0; i < steps.Count; i++)
                if (steps[i] != null && value >= steps[i].trigger) next = i;

            int current = stepRows[row];
            if (current >= 0 && current < steps.Count && steps[current] != null)
            {
                if (now < stepUntilRows[row]) next = current;                       // 最短保持
                else
                {
                    float release = steps[current].trigger - Mathf.Abs(steps[current].threshold);
                    if (value >= release && next < current) next = current;         // 迟滞：没掉够就不退
                }
            }

            if (next != current)
            {
                stepRows[row] = next;
                stepUntilRows[row] = next >= 0 && steps[next] != null ? now + Mathf.Max(0f, steps[next].hold) : 0.0;
            }

            return next >= 0 && steps[next] != null ? steps[next].target : 0f;
        }

        private static Quaternion[] NewBoneRotations()
        {
            var array = new Quaternion[BoneCount];
            for (int i = 0; i < array.Length; i++) array[i] = Quaternion.identity;
            return array;
        }

        private static bool IsReserved(string name)
        {
            for (int i = 0; i < Targets.Fixed.Length; i++)
                if (Targets.Fixed[i] == name) return true;
            return false;
        }

        private void Note(string message)
        {
            Error = Error == null ? message : Error + "；" + message;
        }
    }
}

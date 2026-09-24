// HoFaceChain.cs  --  中间层的**运行期求值器**（Warudo 侧）：裸线名 → 规范参数
//
// 【2026-09-25 拆过一次：这里只剩"参数层"】
// 原来这个文件把两件事混在一起：**求值**（profile 的输入行/输出行）与**装配**（把结果拼成
// `BlendShapes` / 头姿 / 头位 / 根位 / 骨骼数组那 5 个口）。现在装配搬去了 `HoFaceSolver`
// —— 于是"参数处理"与"控制求解"变成两个节点，中间只隔一份字典（见 `Nodes/HoFaceParameterNode.cs`）。
// 拆的缝本来就是现成的：求值三行里 `EvaluateInputs` / `EvaluateOutputs` 留在这儿，`Assemble` 出门。
//
// 【这份求值器做什么】
//   输入行：`规范名 = 曲线(表达式(裸线名…))`   —— 名字是**手机/B 端发来的原样**
//   输出行：`参数名 = 曲线(表达式(规范名…))`   —— 名字带 `ARKit/` 前缀或落在保留名表里
//   交出去：`Parameters`（**出口不带 `ARKit/` 前缀**，保留名原样）—— 这就是两层之间唯一的接口
//
// 【缺键语义】表达式引用到的线名只要有一个这一帧没来，这一行就**不写**（值保持上一帧）——
// 与 VBridger 一致。这条规矩让"两种协议的输入行同时存在"变成安全操作：哪个源在发，只有那一套行会动。
//
// 【为什么没有 Animator / 代理渲染器】纯求值：没有 Animator、没有代理渲染器、没有通道模式，
// 一份数据进、一份数据出。理由见 Unity 侧的 `FACE_TRACKING_CONTROLLER_STRUCTURE.md` 与本文档头。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoFaceTracking.Core
{
    public sealed class HoFaceChain
    {
        /// <summary>
        /// 这一帧算出来的**参数**（输出行的结果）：键已去掉 `ARKit/` 前缀，保留名（`Head/RotX`…）原样。
        /// 这就是交给「HoFace控制求解」的那份字典 —— **两层之间唯一的接口**。
        /// 除了 52 个融合形状与 9 个保留名，配置里写了别的名字（比如眼睑那两根轴）也会原样在这里，
        /// 由求解器决定认不认（它只挑保留名 + 其余进 `BlendShapes`）。
        /// </summary>
        public readonly Dictionary<string, float> Parameters = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>表达式里用的 `ARKit/` 前缀（配置层的写法；出口会去掉它）。</summary>
        public const string ArkitPrefix = "ARKit/";

        /// <summary>有几行输出（面板显示用）。</summary>
        public int OutputRowCount { get { return outputs.Length; } }

        /// <summary>有几行输入（面板显示用）。</summary>
        public int InputRowCount { get { return inputRows.Length; } }

        /// <summary>
        /// 编译期的问题（表达式解析失败、修饰符 kind 不认得）。
        /// 逐帧求值**不看它** —— 它只用来在面板上点名。
        /// </summary>
        public string Error { get; private set; }

        // ── 编译好的行（表达式解析一次，状态数组按行开）────────────────────────
        private readonly HoFaceOutput[] inputRows;
        private readonly HoFaceExpression[] inputExpressions;
        private readonly float[] inputValues;
        private readonly bool[] inputFresh;
        private readonly float[] inputSmooth;
        private readonly int[] inputStepIndex;
        private readonly double[] inputStepUntil;

        /// <summary>
        /// 规范名 → 输入行号。
        ///
        /// **同名多行 = 最后一行生效（后写的赢）** —— 这是**有意**的**覆盖 / 优先级**机制：
        /// 想在别人的表上盖一行，就把它写在后面。**而且缺数据时不回退**：覆盖行没数据 ⇒ 这一格就是 0
        /// （"吵"比"偷偷拿下面那行的值"好；静默回退才是最难查的那种）。
        ///
        /// ⚠️ 2026-09-25 那次现场是**配置写错**、不是这条规矩错：调试配置里同一格混写了两种方言
        /// （安卓命名在前、iPhone 命名在后），而设备只发安卓那套 ⇒ 后声明的那行没数据、把有数据的行顶掉 ⇒ 值恒 0，
        /// 症状是"只有 head 系与眨眼在动"。**修法是让配置只写它真会发的方言**（现在那份就是单方言），
        /// 不是改这里的语义 —— 我一度把它改成了"每帧取最后一个真有值的行"，**那是错的，已恢复**。
        /// </summary>
        private readonly Dictionary<string, int> inputIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly HoFaceOutput[] outputs;
        private readonly string[] outputKeys;        // 出口用的键（已去 `ARKit/` 前缀）
        private readonly HoFaceExpression[] expressions;
        private readonly float[] outputValues;
        private readonly float[] outputSmooth;
        private readonly int[] stepIndex;
        private readonly double[] stepUntil;

        /// <summary>表达式引用到的变量名（草稿：每行求值时攒一遍，见 <see cref="Evaluate"/>）。</summary>
        private readonly List<string> touched = new List<string>();

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

                // 同名多行：最后一行生效（用户覆盖 / 优先级 —— 后写的赢，缺数据也不回退）
                inputIndex[inputList[i].parameter] = i;
            }

            var rows = Middleware.outputs ?? new List<HoFaceOutput>();
            outputs = new HoFaceOutput[rows.Count];
            outputKeys = new string[rows.Count];
            expressions = new HoFaceExpression[rows.Count];
            outputValues = new float[rows.Count];
            outputSmooth = new float[rows.Count];
            stepIndex = new int[rows.Count];
            stepUntil = new double[rows.Count];
            for (int i = 0; i < stepIndex.Length; i++) stepIndex[i] = -1;   // −1 = 还没进任何档

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

                // 出口的键：
                //   `ARKit/EyeBlink_L` → 查通道表 → **规范名** `eyeBlinkLeft`（认 `_L/_R` 别名，这是老行为，别丢）
                //   认不出的融合形状名 → 报一句配置问题，键仍用它本身（求解器只认角色真有的键，传下去无害）
                //   保留名（`Head/RotX` 之类）与其它名字 → 原样
                outputKeys[i] = OutputKey(rows[i].parameter, i);
            }
        }

        /// <summary>输出行的键怎么落到出口字典上（见上面那段注释）。</summary>
        private string OutputKey(string parameter, int rowIndex)
        {
            if (string.IsNullOrEmpty(parameter)) return null;
            if (!parameter.StartsWith(ArkitPrefix, StringComparison.Ordinal)) return parameter;

            string shape = parameter.Substring(ArkitPrefix.Length);
            int channel = HoFaceTrackingChannels.IndexOf(shape);
            if (channel >= 0) return HoFaceTrackingChannels.Names[channel];

            Note("不认识的融合形状（第 " + (rowIndex + 1) + " 行）：" + parameter);
            return shape;
        }

        /// <summary>
        /// 走一帧。**必须先调用它，再读任何结果** —— 结果都是原地更新的。
        /// </summary>
        /// <param name="rawValues">来源发来的"线名 → 原值"。null 当空字典（所有输入行都保持上一帧）。</param>
        /// <param name="deltaTime">平滑修饰符用的步长（秒）。</param>
        /// <param name="now">分档修饰符用的时刻（秒，单调递增即可）。</param>
        public void Evaluate(Dictionary<string, float> rawValues, float deltaTime, double now)
        {
            current = rawValues;
            EvaluateInputs(rawValues, deltaTime, now);
            EvaluateOutputs(deltaTime, now);
            AssembleParameters();
            primed = true;
        }

        /// <summary>
        /// 把每一行的结果摊成出口那份字典：键已归一（`ARKit/` 去掉、`_L/_R` 别名换成规范名）。
        /// </summary>
        private void AssembleParameters()
        {
            Parameters.Clear();
            for (int i = 0; i < outputs.Length; i++)
            {
                string key = outputKeys[i];
                if (string.IsNullOrEmpty(key)) continue;
                Parameters[key] = outputValues[i];
            }
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

        /// <summary>
        /// 输出行的表达式取值：先看**同名输入行里最后声明的那个**（覆盖 / 优先级），再回退到原始线名。
        /// 覆盖行这一帧没数据时不回退 —— 那一格就是 0（见 <see cref="inputIndex"/> 的说明）。
        /// </summary>
        private float Lookup(string name)
        {
            int row;
            if (name != null && inputIndex.TryGetValue(name, out row)) return inputValues[row];
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

        private void Note(string message)
        {
            Error = Error == null ? message : Error + "；" + message;
        }
    }
}

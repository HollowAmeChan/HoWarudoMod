// HoFaceController.cs  --  「跑一个真的 AnimatorController」：从文件路径读 bundle，在隐藏影子上跑，采集结果
//
// 【为什么要这个】一直想把"控制器"（混合树那套）包进控制求解节点，让这个 Mod 变成通用的：
// 用户给一个控制器，我们负责喂参数、把结果采出来交给官方那三个应用节点。**完全解耦**。
//
// 【⚠️ 硬约束一：`.controller` 文件本身读不了】
// `.controller` 是**编辑器格式**（YAML，引用别的资源靠 GUID/fileID），播放器里既没有
// `UnityEditor.Animations`、也没有运行时反序列化器。运行时能拿到 `RuntimeAnimatorController` 的容器只有两种：
//   ① mod 自带资源（导出进 `sharedassets.bin`，用 `Plugin.ModHost.SharedAssets` 取）—— 不是解耦（用户得改 mod 工作区）；
//   ② **AssetBundle**（`AssetBundle.LoadFromFile` + `LoadAllAssets<RuntimeAnimatorController>()`）← 本文件走这条。
// 所以本节点的输入是**一个 bundle 文件的路径**，不是 `.controller`。
//
// 【⚠️ 硬约束二：bundle 里必须带"控制器绑定的那套 rig"】
// 控制器的 clip 是按**层级路径**（`Body/Head`）和**属性名**（`blendShape.JawOpen`）绑定的，而
// **运行时没有 API 能枚举一个 `AnimationClip` 的绑定**（`AnimationUtility` 是编辑器专属）。
// 所以我们**没法凭空造一个代理**去接住控制器的输出 —— 必须用控制器原配的那套层级。
// 于是 bundle 里要有：**一个 GameObject 预制体（rig）+ 一个/多个 `RuntimeAnimatorController`**。
// 我们把它实例化到隐藏影子上，加 `Animator`、挂上控制器，喂参数，然后**从代理身上读结果**。
//
// 【结果怎么读】（`Animator` 没有"读混合树输出"的 API —— `GetFloat` 读的是我们写进去的输入）
//   · 融合形状：`SkinnedMeshRenderer.GetBlendShapeWeight`（Unity 是 0..100，我们 /100 成 0..1）
//   · 骨骼：`Animator.GetBoneTransform(...)` 拿到代理骨骼，取其相对**第一帧姿势**的偏移
//     （`Inverse(rest) * current`）—— 因为官方那个口的语义是"**偏移**：单位四元数 = 不改那根骨头"。
//   · **头/根位置不由控制器负责**（仍由参数里的保留名装配）：控制器多半只管表情与骨骼，
//     让位置继续走保留名，lip sync 类的控制器才不会把头部追踪弄没。
//
// 【生命周期与热更新】
//   影子 GameObject 用 `HideFlags.HideAndDontSave`（不进场景、不被保存）。热更新会把静态引用丢掉而
//   对象还活着，所以在开始跑之前先 `Resources.FindObjectsOfTypeAll<Animator>()` 扫一遍、把**同名的**旧影子清掉
//   （`GameObject.Find` 找不到 HideAndDontSave 的东西，别用它）。
//
// 【❓ 还没验证的（第一次真机跑就看这几条）】
//   ① UMod 的安全校验放不放行 `UnityEngine.AssetBundle`（历史：`System.Reflection`/`System.Security.Cryptography`
//      都被禁；`System.Net.Sockets`、`UnityEngine.GUIUtility` 是放行的）；
//   ② 用户自己打的 bundle 能不能 LoadFromFile；
//   ③ 运行期给隐藏对象加 `Animator` 后 `parameters` / 求值 / 采样是否照常。
//   失败时状态文字会**逐条点名**失败在哪一步（打不开 bundle / 里面没有控制器 / 里面没有 rig）。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HoFaceTracking.Core
{
    public sealed class HoFaceController
    {
        /// <summary>影子对象的名字。热更新后清理旧影子靠它。</summary>
        private const string RigName = "HoFaceSolverRig";

        private AssetBundle _bundle;
        private string _bundlePath;
        private GameObject _rig;
        private Animator _animator;
        private readonly List<SkinnedMeshRenderer> _meshes = new List<SkinnedMeshRenderer>();
        private readonly List<string> _shapeNames = new List<string>();
        private readonly Dictionary<string, float> _shapes = new Dictionary<string, float>(StringComparer.Ordinal);

        private readonly int _boneCount = (int)HumanBodyBones.LastBone;
        private readonly Quaternion[] _restPose = new Quaternion[(int)HumanBodyBones.LastBone];
        private bool _restCaptured;

        /// <summary>把参数写进控制器的输入口时，最多写几个（够用就行；防呆）。</summary>
        private const int MaxParameters = 256;

        public bool Ready { get { return _animator != null; } }

        public string BundlePath { get { return _bundlePath; } }

        /// <summary>人看的短状态（节点 `状态` 口直接拼这句话）。</summary>
        public string Status { get; private set; }

        /// <summary>控制器自带的输入参数个数（`Animator.parameters` 是运行时可读的）。</summary>
        public int ParameterCount { get; private set; }

        /// <summary>上一帧真写进去的参数个数（对不上的说明名字不匹配 —— 那是唯一线索）。</summary>
        public int MatchedParameters { get; private set; }

        /// <summary>
        /// 上一帧写进去的 **参数名 → 值**，摊成一行（最多 <see cref="ReportLimit"/> 个）。
        ///
        /// ⚠️ 这条是**必备证据**，不是装饰：控制器模式下 `BlendShapes` 来自代理网格，
        /// 而网格上"有没有这个形状、权重采到多少"与"参数有没有写进去"是**两件独立的事**。
        /// 只报个数（`对上参数 2 个`）时，"形状恒 0"分不清是哪一件 —— 2026-09-25 那次
        /// `mouthSmileLeft` 恒 0 就是卡在这个盲区里（参数层明明 0.946）。
        /// 值取的是**我们写给 Animator 的那个数**（`Animator.GetFloat` 读回来的是同一个），
        /// 所以它说明的是"输入到位"，形状那一侧仍要看 `BlendShapes`。
        /// </summary>
        public string MatchedText { get; private set; }

        /// <summary>`MatchedText` 里最多列几个参数（节点 `状态` 是给人看的，别摊开 200 个）。</summary>
        private const int ReportLimit = 6;

        /// <summary>采样那次日志写过了没有（见 `Solve` 的第 ⑤ 步）。</summary>
        private bool _loggedSample;

        private readonly string[] _matchedNames = new string[ReportLimit];
        private readonly float[] _matchedValues = new float[ReportLimit];

        /// <summary>
        /// `Prepare` 时做的一次性自检报告（**只报事实，别拿它当结论**）：
        ///   · `.shapes[a,b]` —— 代理网格上**真实存在**的 blend shape 名（前几个）。
        ///     ⚠️ `SkinnedMeshRenderer.GetBlendShapeWeight` 是**按名字**取权重的：控制器里写了
        ///     `blendShape.foo` 而这个网格上根本没有 `foo`，Unity **不会报错**，那一格就永远是 0。
        ///     这是"形状恒 0"的第三条可能原因（前两条见 <see cref="MatchedText"/>）。
        ///   · `.roundtrip ok=` —— 拿第一个形状做"写 37 → 立刻读回来"的往返：这一步**绕开 Animator**，
        ///     验的是"这个代理网格到底会不会动"。`ok=True got=37 count=N` 说明网格本身没问题。
        ///   · `.state=hash` —— `Animator` 当前状态的全路径哈希（说明状态机在不在跑）。
        /// </summary>
        public string Report { get; private set; }

        /// <summary>采集到的融合形状字典（键 = 代理网格上的形状名）。</summary>
        public readonly Dictionary<string, float> BlendShapes = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>采集到的骨骼旋转偏移（按 `HumanBodyBones` 索引；没采到就是 identity）。</summary>
        public Quaternion[] BoneRotations;

        public HoFaceController()
        {
            BoneRotations = NewIdentity();
            Status = "未启用";
        }

        /// <summary>按路径准备（路径没变就什么都不做）。返回是否可用。</summary>
        public bool Prepare(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Dispose();
                Status = "未启用";
                return false;
            }

            if (_animator != null && path == _bundlePath) return true;   // 已经就绪

            Dispose();
            _bundlePath = path;

            // ① 打开 bundle
            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
            }
            catch (Exception e)
            {
                Status = "打不开 bundle：" + Short(e);
                Debug.LogException(e);
                _bundle = null;
                return false;
            }

            if (_bundle == null)
            {
                Status = "打不开 bundle（路径对不对？它要是 Unity 打的 AssetBundle）";
                return false;
            }

            // ② 找控制器
            RuntimeAnimatorController controller = null;
            var controllers = _bundle.LoadAllAssets<RuntimeAnimatorController>();
            if (controllers != null && controllers.Length > 0) controller = controllers[0];
            if (controller == null)
            {
                Dispose();
                Status = "bundle 里没有 RuntimeAnimatorController";
                return false;
            }

            // ③ 找 rig（控制器原配的那套层级；运行时枚举不了 clip 绑定，所以必须用户给）
            GameObject prefab = null;
            var prefabs = _bundle.LoadAllAssets<GameObject>();
            if (prefabs != null && prefabs.Length > 0) prefab = prefabs[0];
            if (prefab == null)
            {
                Dispose();
                Status = "bundle 里没有 GameObject（控制器绑定的那套 rig 也要打进去）";
                return false;
            }

            // ④ 清掉热更新留下的旧影子，再实例化新的
            CleanupStrays();
            _rig = UnityEngine.Object.Instantiate(prefab);
            _rig.name = RigName;
            _rig.hideFlags = HideFlags.HideAndDontSave;
            _rig.transform.position = Vector3.zero;
            _rig.transform.rotation = Quaternion.identity;

            _animator = _rig.GetComponent<Animator>();
            if (_animator == null) _animator = _rig.AddComponent<Animator>();
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 影子在屏幕外，默认 culling 会让它不动
            _animator.runtimeAnimatorController = controller;
            _animator.Rebind();
            _animator.WriteDefaultValues();

            ParameterCount = _animator.parameters != null ? _animator.parameters.Length : 0;

            // ⑤ 收集要采的网格与形状名
            _meshes.Clear();
            _shapeNames.Clear();
            _rig.GetComponentsInChildren(true, _meshes);
            for (int m = 0; m < _meshes.Count; m++)
            {
                Mesh mesh = _meshes[m].sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++) _shapeNames.Add(mesh.GetBlendShapeName(i));
            }

            _restCaptured = false;
            Status = "已载入：" + FileName(path) + "（参数 " + ParameterCount + " 个，形状 "
                + _shapeNames.Count + " 个，网格 " + _meshes.Count + " 个）";
            Report = BuildReport();

            // 同一句话也写进 Player.log：`状态` 口要接线才看得到，日志不用。
            // （2026-09-25 的教训：`状态` 多了一行用户却没看见，白等了一轮。）
            Debug.Log("[Ho 面捕] 控制器 " + Status + " · 自检 " + Report);
            return true;
        }

        /// <summary>
        /// 载入后的一次性自检（见 <see cref="Report"/> 的说明）。**只写进 `状态` 文字，不影响求值** ——
        /// 往返测试写完立刻用原值还原。
        /// </summary>
        private string BuildReport()
        {
            var builder = new StringBuilder();

            // 网格上真实存在的形状名（前 8 个）——控制器里的 `blendShape.xxx` 必须在这里面才可能有非 0 权重
            builder.Append("shapes[");
            int shown = _shapeNames.Count < 8 ? _shapeNames.Count : 8;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(_shapeNames[i]);
            }
            if (_shapeNames.Count > shown) builder.Append(",…");
            builder.Append(']');

            // 往返：绕开 Animator，验这个代理网格自己会不会动
            builder.Append("  ·  roundtrip ");
            if (_meshes.Count == 0 || _shapeNames.Count == 0)
            {
                builder.Append("（没有网格/形状，测不了）");
            }
            else
            {
                var renderer = _meshes[0];
                float before = renderer.GetBlendShapeWeight(0);
                renderer.SetBlendShapeWeight(0, 37f);
                float after = renderer.GetBlendShapeWeight(0);
                renderer.SetBlendShapeWeight(0, before);
                builder.Append(before.ToString("F1")).Append("→37→").Append(after.ToString("F1"))
                    .Append(after > 36.9f && after < 37.1f ? " 可写✓" : " 写不进去✗");
            }

            // 状态机在不在跑（全路径哈希）
            if (_animator != null)
            {
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                builder.Append("  ·  state=").Append(info.fullPathHash)
                    .Append(" t=").Append(info.normalizedTime.ToString("F2"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 把"上一帧写进去的参数"拼成一行（前 <see cref="ReportLimit"/> 个，超出只报数）。
        /// 事件/Trigger 类参数不算"对上"（它们不是这一帧的值，我们不主动触发）。
        /// </summary>
        private string BuildMatchedText(int matched)
        {
            if (matched <= 0) return "";

            var builder = new StringBuilder();
            int shown = matched < ReportLimit ? matched : ReportLimit;
            for (int i = 0; i < shown; i++)
            {
                if (builder.Length > 0) builder.Append(" / ");
                builder.Append(_matchedNames[i]).Append(' ').Append(_matchedValues[i].ToString("F3"));
            }
            if (matched > shown) builder.Append(" …共 ").Append(matched).Append(" 个");

            return builder.ToString();
        }

        /// <summary>
        /// 从路径里抠出文件名（只为状态文字好看）。
        /// ⚠️ **不能用 `System.IO.Path.GetFileName`** —— UMod 的安全校验禁掉整个 `System.IO` 命名空间
        /// （2026-09-25 实测：真机构建报 `Illegal reference to disallowed namespace: System.IO`
        /// + `Illegal reference to disallowed type: System.IO.Path`，就毙在这一句上）。
        /// 所以这里自己按分隔符切一刀。
        /// </summary>
        private static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            int cut = path.LastIndexOf('/');
            int backslash = path.LastIndexOf('\\');
            if (backslash > cut) cut = backslash;
            return cut >= 0 && cut + 1 < path.Length ? path.Substring(cut + 1) : path;
        }

        /// <summary>
        /// 走一帧：把参数写进控制器 → 求值 → 从代理上采结果。
        /// <paramref name="parameters"/> 就是上游那份字典（键 = 参数名，值 = 数值）。
        /// </summary>
        public void Solve(Dictionary<string, float> parameters)
        {
            if (_animator == null) return;

            // ① 写参数：只写控制器**真有**的那些口（`SetFloat` 写不存在的名字会每帧刷警告）
            int matched = 0;
            var declared = _animator.parameters;
            if (declared != null && parameters != null)
            {
                for (int i = 0; i < declared.Length && matched < MaxParameters; i++)
                {
                    var parameter = declared[i];
                    float value;
                    if (!parameters.TryGetValue(parameter.name, out value)) continue;

                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            _animator.SetFloat(parameter.name, value);
                            break;
                        case AnimatorControllerParameterType.Bool:
                            _animator.SetBool(parameter.name, value != 0f);
                            break;
                        case AnimatorControllerParameterType.Int:
                            _animator.SetInteger(parameter.name, Mathf.RoundToInt(value));
                            break;
                        default:
                            continue;   // Trigger：不主动触发（那是"事件"，不是"这一帧的值"）—— 不算对上
                    }

                    // 留证：前几个对上名字的（固定数组原地写，不每帧 new List）
                    if (matched < ReportLimit)
                    {
                        _matchedNames[matched] = parameter.name;
                        _matchedValues[matched] = value;
                    }
                    matched++;
                }
            }

            MatchedParameters = matched;
            MatchedText = BuildMatchedText(matched);

            // ② 求值（时间步 0：我们要的是"当前参数下的姿势"，不是推进动画）
            _animator.Update(0f);

            // ③ 采融合形状（Unity 是 0..100 → 我们 0..1）
            BlendShapes.Clear();
            for (int m = 0; m < _meshes.Count; m++)
            {
                Mesh mesh = _meshes[m].sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    BlendShapes[mesh.GetBlendShapeName(i)] = _meshes[m].GetBlendShapeWeight(i) * 0.01f;
            }

            // ④ 采骨骼偏移：相对**第一帧姿势**（控制器的默认状态），单位四元数 = 不改那根骨头
            for (int i = 0; i < _boneCount; i++)
            {
                Transform bone = _animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null) { BoneRotations[i] = Quaternion.identity; continue; }

                if (!_restCaptured) { _restPose[i] = bone.localRotation; BoneRotations[i] = Quaternion.identity; continue; }
                BoneRotations[i] = Quaternion.Inverse(_restPose[i]) * bone.localRotation;
            }
            _restCaptured = true;

            // ⑤ 采样结果写一次日志（**只写一次**，只在"有输入"时）。
            // 为什么要有它：`状态`/`BlendShapes` 两个口都得接线才看得见，而"控制器到底动没动"
            // 是每次联调的第一个问题。写一次就够了 —— 它报的是"某一帧采到什么"，不是每帧变化。
            if (!_loggedSample && !string.IsNullOrEmpty(MatchedText))
            {
                _loggedSample = true;
                var line = new StringBuilder();
                line.Append("[Ho 面捕] 控制器采样 写入 ").Append(MatchedText).Append("  →  采到 ");
                bool first = true;
                foreach (var pair in BlendShapes)
                {
                    if (!first) line.Append(" / ");
                    line.Append(pair.Key).Append(' ').Append(pair.Value.ToString("F4"));
                    first = false;
                }
                Debug.Log(line.ToString());
            }
        }

        /// <summary>释放：销毁影子、卸掉 bundle。</summary>
        public void Dispose()
        {
            if (_rig != null)
            {
                try { UnityEngine.Object.Destroy(_rig); }
                catch (Exception e) { Debug.LogException(e); }
                _rig = null;
            }
            _animator = null;

            if (_bundle != null)
            {
                try { _bundle.Unload(true); }        // true = 连实例化出来的东西一起卸（影子已经先销毁了）
                catch (Exception e) { Debug.LogException(e); }
                _bundle = null;
            }

            _meshes.Clear();
            _shapeNames.Clear();
            BlendShapes.Clear();
            ParameterCount = 0;
            MatchedParameters = 0;
            MatchedText = null;
            Report = null;
            _loggedSample = false;
            _restCaptured = false;
        }

        /// <summary>
        /// 清掉热更新留下的同名影子。
        /// 为什么要扫全部：`HideFlags.HideAndDontSave` 的对象**不进场景**，`GameObject.Find` 找不到它；
        /// 而热更新会把我们的静态引用丢掉、对象却还活着 —— 不扫就会一层层叠起来。
        /// </summary>
        private static void CleanupStrays()
        {
            var animators = Resources.FindObjectsOfTypeAll<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                var candidate = animators[i];
                if (candidate == null || candidate.gameObject == null) continue;
                if (candidate.gameObject.name != RigName) continue;
                try { UnityEngine.Object.Destroy(candidate.gameObject); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static Quaternion[] NewIdentity()
        {
            var array = new Quaternion[(int)HumanBodyBones.LastBone];
            for (int i = 0; i < array.Length; i++) array[i] = Quaternion.identity;
            return array;
        }

        /// <summary>异常摘要：**不要**用 `GetType().Name`（安全校验禁 `System.Reflection`）。</summary>
        private static string Short(Exception e)
        {
            return e != null && e.Message != null ? e.Message : "（没有消息）";
        }
    }
}

// HoDebugBundleBuilder.cs  --  调试用：造一个「控制器 + rig」的 AssetBundle，给「HoFace控制求解」用
//
// 【为什么需要它】节点的输入是一个 **AssetBundle**（不是 `.controller` 文件 —— 那是编辑器格式，
// 运行时读不了），而且 bundle 里必须带**控制器绑定的那套 rig**（运行时枚举不了 `AnimationClip` 的绑定，
// 所以造不出代理去接住输出）。这个脚本就是把这两样东西造出来、打成一个文件。
//
// 【它造出来的东西】（全都是"最小可验证"，不是美术资产）
//   · `HoDebugProxyMesh`：一个三角形 + 两个 blend shape（`jawOpen`、`mouthSmileLeft`）
//     —— 名字**故意跟规范参数名一致**：控制器模式的参数是按名字对上的（对不上的数量会显示在节点状态里）
//   · `HoDebugRig`（预制体）：上面挂 `Animator` + `SkinnedMeshRenderer`（`updateWhenOffscreen = true`）
//   · `HoDebugController`：**一层**，一条 `FreeformDirectional2D` 混合树（X = `jawOpen`、Y = `mouthSmileLeft`），
//     四个角各一条 clip、每个 clip **同时**写两个形状。这样"哪个形状该动"完全由参数定，没有层的歧义。
//     ⚠️ 第一版是"两层、每层一条 1D 树"（一层一个参数）：两层都 `Override`、状态默认 `WriteDefaultValues = 1`，
//        互相把对方的属性写回默认值 ⇒ 实测 `mouthSmileLeft` 恒 0、`jawOpen` 只剩极小值。别改回去。
//
// 【⚠️ 落在哪：直接打进 **Warudo 的插件沙箱**】（`SandboxFolder`，与中间层配置同一个目录）
//   运行时读的就是那儿 —— 节点上那个下拉列表（`[AutoComplete]`）列出的就是沙箱里的 `*.bundle`，
//   选中的是个**文件名**，不是绝对路径（`HoFaceController.Prepare` 用 `ReadFileBytes` + `LoadFromMemory`）。
//   2026-09-25 之前打到工程根的 `_hodebug/`，而 Warudo 读沙箱 ⇒ "重打了却没生效"，白查两轮。
//   装 Warudo 时（或路径不同）才退回 `_hodebug/`，并且日志里会明确警告"Warudo 读不到这里"。
//
// 【怎么用】菜单 `HoWarudoModTests/造调试用控制器 bundle（AssetBundle）` → 在「HoFace控制求解」的
// `控制器` 下拉里选中它 → 按「重读控制器」（**同名文件被替换时 `Prepare` 认不出来，必须按**）→ 看 `状态`：
//   已载入：hoface-controller-test.bundle（参数 2 个，形状 2 个，网格 1 个）
//   然后 `BlendShapes` 里应出现 `jawOpen` / `mouthSmileLeft`，值跟着输入变（形状权重 0..100 → 我们/100 成 0..1）。
//
// 【⚠️ 这个脚本是 Editor 专用】放在 `Editor/` 文件夹下，所以不会进 mod 的构建（UMod 只打包 mod 工作区，
// 而且这条链是 2021.3.45f2 —— **与 Warudo 本体同版本**，AssetBundle 只有同版本才读得了，别用别的编辑器打）。
// 它自己可以用 `System.IO`（安全审查只管 mod 运行时脚本，不管编辑器脚本）。
//
// 【没验到的】骨骼那条路：`Animator.GetBoneTransform` 需要 **Humanoid Avatar**，这个最小 rig 没有，
// 所以 `Bone Rotations` 会全是 identity（我们代码里 null 就到 identity）—— 要验骨骼得塞一个带 Avatar 的人形模型。

using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Hollow.HoWarudoModTests.Editor
{
    public static class HoDebugBundleBuilder
    {
        private const string WorkFolder = "Assets/HoWarudoModTests/_debugbundle";

        /// <summary>没装 Warudo（或者路径不一样）时的落点。**这不是运行时读的目录**，只是别丢文件。</summary>
        private const string FallbackFolder = "_hodebug";

        /// <summary>
        /// Warudo 的插件沙箱目录（`PluginPersistentDataManager.GetBasePath()` 就是它）。
        /// 中间层配置也在这儿 —— 两边同一套目录，节点上那个下拉列表列的就是这里。
        /// </summary>
        private const string SandboxFolder =
            "D:/Steam/steamapps/common/Warudo/Warudo_Data/StreamingAssets/Plugins/Data/hollow.hofacetracking";

        private const string BundleName = "hoface-controller-test.bundle";

        /// <summary>参数名 = blend shape 名 = 规范参数名（控制器模式就是按名字对上的）。</summary>
        private static readonly string[] Parameters = { "jawOpen", "mouthSmileLeft" };

        [MenuItem("HoWarudoModTests/造调试用控制器 bundle（AssetBundle）")]
        public static void BuildFromMenu()
        {
            string path = Build();
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>给 batchmode 用：`-executeMethod Hollow.HoWarudoModTests.Editor.HoDebugBundleBuilder.BuildBatch`。</summary>
        public static void BuildBatch()
        {
            Debug.Log("[HoDebugBundle] " + Build());
        }

        public static string Build()
        {
            EnsureFolders();

            GameObject rig = BuildRig();
            AnimatorController controller = BuildController();
            string prefabPath = WorkFolder + "/HoDebugRig.prefab";

            // 预制体上把控制器接好（我们运行时会自己再赋一次，这里接上便于在编辑器里直接看）
            var animator = rig.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            PrefabUtility.SaveAsPrefabAsset(rig, prefabPath);
            Object.DestroyImmediate(rig);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 打 bundle：ChunkBasedCompression（= LZ4）—— 我们走 `LoadFromMemory`，LZ4 读得了；
            // **别用默认 LZMA**（那份要整段解压，内存路线更容易炸）。
            //
            // ⚠️ 落点直接是 **Warudo 的插件沙箱**（跟中间层配置同一个目录）——
            // 2026-09-25 那次"重打了却没生效"就是因为打到了工程根的 `_hodebug/`，
            // 而 Warudo 读的是沙箱；现在菜单一按就到位。装 Warudo 时不存在才退回 `_hodebug`。
            string outDir = Directory.Exists(SandboxFolder)
                ? SandboxFolder
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, FallbackFolder);
            Directory.CreateDirectory(outDir);

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = BundleName,
                    assetNames = new[] { prefabPath, AssetDatabase.GetAssetPath(controller) }
                }
            };

            BuildPipeline.BuildAssetBundles(outDir,
                builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);

            string bundlePath = Path.Combine(outDir, BundleName);
            bool inSandbox = string.Equals(outDir, SandboxFolder, System.StringComparison.OrdinalIgnoreCase);
            Debug.Log("[HoDebugBundle] bundle 好了：" + bundlePath
                + (inSandbox
                    ? "\n  ✅ 已经直接落在 Warudo 的插件沙箱里 —— 不用再复制。"
                    : "\n  ⚠️ 没找到 Warudo 沙箱，落在 `" + FallbackFolder + "/`（**Warudo 读不到这里**，"
                        + "要自己复制到插件沙箱）。沙箱路径见「HoFace参数处理」的 `状态` 口。")
                + "\n  然后：「HoFace控制求解」的 `控制器` 下拉里选它 → 按「重读控制器」"
                + "\n  期望状态：已载入：hoface-controller-test.bundle（参数 " + Parameters.Length
                + " 个，形状 " + Parameters.Length + " 个，网格 1 个）"
                + "\n  ⚠️ 同名文件被替换时 `Prepare` 认不出来，必须按「重读控制器」。");
            return bundlePath;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/HoWarudoModTests"))
                AssetDatabase.CreateFolder("Assets", "HoWarudoModTests");
            if (!AssetDatabase.IsValidFolder(WorkFolder))
                AssetDatabase.CreateFolder("Assets/HoWarudoModTests", "_debugbundle");
        }

        /// <summary>造"网格 + SkinnedMeshRenderer + Animator"的 rig。</summary>
        private static GameObject BuildRig()
        {
            // 一个三角形就够 —— 我们只读 blend shape 权重，不看画面
            var mesh = new Mesh { name = "HoDebugProxyMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // 每个形状两帧：权重 0（不动）与 100（顶点往上挪一点，纯为了"有变化"）
            var delta = new[] { Vector3.zero, Vector3.up * 0.05f, Vector3.up * 0.05f };
            foreach (string shape in Parameters)
            {
                mesh.AddBlendShapeFrame(shape, 0f, new Vector3[3], null, null);
                mesh.AddBlendShapeFrame(shape, 100f, delta, null, null);
            }

            string meshPath = WorkFolder + "/HoDebugProxyMesh.asset";
            AssetDatabase.CreateAsset(mesh, meshPath);

            var rig = new GameObject("HoDebugRig");
            var renderer = rig.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new Transform[0];
            renderer.updateWhenOffscreen = true;   // 影子在屏幕外，不然有些情况下不更新
            rig.AddComponent<Animator>();
            return rig;
        }

        /// <summary>
        /// 造控制器：**一层 + 一条 2D 混合树**（四个角各一条 clip，每个 clip 同时写两个形状）。
        ///
        /// 为什么不用"一层一个参数"（第一版就是那么写的）：多层 + `Override` 混写时，
        /// 到底哪个形状被哪一层"覆盖"取决于层的混合语义，出问题很难看出来
        /// （实测现象：`jawOpen` 会动、`mouthSmileLeft` 恒为 0，但分不清是输入是 0 还是第二层没生效）。
        /// 一条树 + 一个状态就没有这个问题：所有绑定都在**同一个 motion** 里，权重完全由参数决定。
        /// </summary>
        private static AnimatorController BuildController()
        {
            string controllerPath = WorkFolder + "/HoDebugController.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            for (int i = 0; i < Parameters.Length; i++)
                controller.AddParameter(Parameters[i], AnimatorControllerParameterType.Float);

            // 四个角：0 = 不动，100 = 该形状推到满（Unity 的 blend shape 权重是 0..100）
            AnimationClip off = BuildCornerClip(0f, 0f, "corner_00");
            AnimationClip jawOnly = BuildCornerClip(100f, 0f, "corner_10");
            AnimationClip smileOnly = BuildCornerClip(0f, 100f, "corner_01");
            AnimationClip both = BuildCornerClip(100f, 100f, "corner_11");

            AnimatorControllerLayer layer = controller.layers[0];      // 用模板自带的 Base Layer
            layer.name = "Drive";
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            var stateMachine = layer.stateMachine;
            AnimatorState state = stateMachine.AddState("Drive");
            stateMachine.defaultState = state;

            var tree = new BlendTree
            {
                name = "Drive 2D",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = Parameters[0],
                blendParameterY = Parameters[1],
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            // 位置就是"两个参数的值"这一坐标；SimpleDirectional2D 也能用，FreeformDirectional2D 更宽容
            tree.AddChild(off, new Vector2(0f, 0f));
            tree.AddChild(jawOnly, new Vector2(1f, 0f));
            tree.AddChild(smileOnly, new Vector2(0f, 1f));
            tree.AddChild(both, new Vector2(1f, 1f));
            state.motion = tree;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// 造一条"两个形状都写死常量"的 clip：绑到 `SkinnedMeshRenderer.blendShape.&lt;形状名&gt;`。
        /// path 用空串 = 就挂在 Animator 那个对象自己身上（我们的 rig 正是这样）。
        ///
        /// 曲线用 **两个相同的键**（`Linear(0, w, 1/30, w)`）而不是 `AnimationCurve.Constant`：
        /// 两者算出来都是常量，但"一个键的曲线"在 Unity 里有个著名的坑 —— 采样点落在唯一那个键之外时，
        /// 单键曲线会被求值成 **0**（多键曲线才按端点夹取）。状态机是拿自己的时间轴去采样的，
        /// 我们控制不了它落在哪；两个键 + `m_PreInfinity/m_PostInfinity = 2`（夹取）就把它钉死了。
        /// ⚠️ 这一条**没有实测对照**（没做过"单键 vs 双键"的 A/B），是防御性写法。
        /// </summary>
        private static AnimationClip BuildCornerClip(float jawWeight, float smileWeight, string clipName)
        {
            var clip = new AnimationClip { name = clipName, frameRate = 30f };

            for (int i = 0; i < Parameters.Length; i++)
            {
                float weight = i == 0 ? jawWeight : smileWeight;
                var binding = new EditorCurveBinding
                {
                    path = "",
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = "blendShape." + Parameters[i]
                };
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0f, weight, 1f / 30f, weight));
            }

            string path = WorkFolder + "/" + clipName + ".anim";
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}

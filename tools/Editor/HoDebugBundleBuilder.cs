// HoDebugBundleBuilder.cs  --  调试用：造一个「控制器 + rig」的 AssetBundle，给「HoFace控制求解」的控制器模式用
//
// 【为什么需要它】控制器模式的输入是一个 **AssetBundle 路径**（不是 `.controller` 文件 —— 那是编辑器格式，
// 运行时读不了），而且 bundle 里必须带**控制器绑定的那套 rig**（运行时枚举不了 `AnimationClip` 的绑定，
// 所以造不出代理去接住输出）。这个脚本就是把这两样东西造出来、打成一个文件。
//
// 【它造出来的东西】（全都是"最小可验证"，不是美术资产）
//   · `HoDebugProxyMesh`：一个三角形 + 两个 blend shape（`jawOpen`、`mouthSmileLeft`）
//     —— 名字**故意跟规范参数名一致**：控制器模式的参数是按名字对上的（对不上的数量会显示在节点状态里）
//   · `HoDebugRig`（预制体）：上面挂 `Animator` + `SkinnedMeshRenderer`（`updateWhenOffscreen = true`）
//   · `HoDebugController`：**两层**，每层一条 1D 混合树（参数 0 → 权重 0；参数 1 → 权重 100）
//     ⚠️ 用两层是因为"一个状态机一次只播一个状态"，而两层是同时播的 —— 这样两个参数能各自驱动一个形状
//   · 打成一个 bundle：`<工程根>/_hodebug/hoface-controller-test.bundle`
//
// 【怎么用】菜单 `HoWarudoModTests/造调试用控制器 bundle（AssetBundle）` → 把弹出来/日志里的路径
// 粘进「HoFace控制求解」的 `控制器（可选，AssetBundle 路径）` → 看节点 `状态` 那一行：
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
        private const string OutputFolder = "_hodebug";
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

            // 打 bundle：ChunkBasedCompression（= LZ4）—— LoadFromFile 读得了；**别用默认 LZMA**
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutputFolder);
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
            Debug.Log("[HoDebugBundle] bundle 好了：" + bundlePath
                + "\n  把它粘进「HoFace控制求解」的 `控制器（可选，AssetBundle 路径）`"
                + "\n  期望状态：已载入：hoface-controller-test.bundle（参数 " + Parameters.Length
                + " 个，形状 " + Parameters.Length + " 个，网格 1 个）");
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

        /// <summary>造控制器：一层一个参数，每层一条 1D 混合树（0 → 权重 0，1 → 权重 100）。</summary>
        private static AnimatorController BuildController()
        {
            string controllerPath = WorkFolder + "/HoDebugController.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // 模板自带一层 Base Layer：**复用它**做第一个参数（别把层删光 —— 一个控制器总得有一层）。
            for (int i = 0; i < Parameters.Length; i++)
            {
                string parameter = Parameters[i];
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);

                AnimationClip low = BuildClip(parameter, 0f, parameter + "_0");
                AnimationClip high = BuildClip(parameter, 100f, parameter + "_100");

                AnimatorControllerLayer layer;
                if (i == 0)
                {
                    layer = controller.layers[0];
                }
                else
                {
                    controller.AddLayer(parameter);
                    layer = controller.layers[controller.layers.Length - 1];
                }

                layer.name = parameter;
                layer.defaultWeight = 1f;
                layer.blendingMode = AnimatorLayerBlendingMode.Override;

                // 每层一个状态机、一个状态，状态里放混合树
                var stateMachine = layer.stateMachine;
                AnimatorState state = stateMachine.AddState("Drive " + parameter);
                stateMachine.defaultState = state;

                var tree = new BlendTree
                {
                    name = "Tree " + parameter,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameter,
                    useAutomaticThresholds = false
                };
                AssetDatabase.AddObjectToAsset(tree, controller);

                tree.AddChild(low, 0f);
                tree.AddChild(high, 1f);
                state.motion = tree;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// 造一条"恒定权重"的 clip：绑到 `SkinnedMeshRenderer.blendShape.<形状名>`。
        /// path 用空串 = 就挂在 Animator 那个对象自己身上（我们的 rig 正是这样）。
        /// </summary>
        private static AnimationClip BuildClip(string shape, float weight, string clipName)
        {
            var clip = new AnimationClip { name = clipName, frameRate = 30f };
            var curve = AnimationCurve.Constant(0f, 1f / 30f, weight);

            var binding = new EditorCurveBinding
            {
                path = "",
                type = typeof(SkinnedMeshRenderer),
                propertyName = "blendShape." + shape
            };
            AnimationUtility.SetEditorCurve(clip, binding, curve);

            string path = WorkFolder + "/" + clipName + ".anim";
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}

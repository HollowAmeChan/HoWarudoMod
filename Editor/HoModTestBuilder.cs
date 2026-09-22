// HoModTestBuilder.cs
// =============================================================================
// HoWarudoModTests 的构建入口。
//
// 这个仓库是「Warudo 各类 Mod 的最小可参考实现」：
//   Mods/Props                道具       入口 Prop.prefab
//   Mods/Particles            粒子       入口 Particle.prefab
//   Mods/Environments         环境       入口 Environment.unity
//   Mods/CharacterAnimations  角色动画   入口 Animation.anim
//   Mods/Plugins              插件       入口 [PluginType] 类
//
// 目录名就是 Warudo 数据目录里的目标目录名 —— 一个目录一个类别，一一对应。
// ⚠️ 不能把多个类别的入口塞进同一个目录：一个 .warudo 只会被落点目录对应的
//    那一个加载器认领，其余的静默不生效（不报错）。
//
// 菜单：
//   1 生成各类别最小示例   补齐缺失的入口资产
//   2 同步工作区           每个 Mods/<类别>/ 建一个同名 Export Profile
//   3 构建全部             对每个工作区调 UMod 官方构建入口
//   4 校验产物             自己解析 .warudo，报告包内条目
//
// batchmode：
//   Unity.exe -batchmode -projectPath <工程> \
//     -executeMethod HoWarudoModTests.EditorTools.HoModTestBuilder.RunAll -logFile <log>
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HoWarudoModTests.EditorTools
{
    public static class HoModTestBuilder
    {
        public const string RepoAssetRoot = "Assets/HoWarudoModTests";
        public const string ModsRoot = RepoAssetRoot + "/Mods";
        public const string OutputRoot = RepoAssetRoot + "/out";

        private enum EntryKind
        {
            Prefab,
            Scene,
            AnimationClip,
            Plugin,
        }

        private sealed class ModTemplate
        {
            public string folderName;     // Mods/<folderName>，同时也是 Warudo 数据目录名
            public EntryKind entryKind;
            public string entryAssetName; // 入口资产文件名（不含扩展名）；插件为空
        }

        private static readonly ModTemplate[] Templates =
        {
            new ModTemplate { folderName = "Props", entryKind = EntryKind.Prefab, entryAssetName = "Prop" },
            new ModTemplate { folderName = "Particles", entryKind = EntryKind.Prefab, entryAssetName = "Particle" },
            new ModTemplate { folderName = "Environments", entryKind = EntryKind.Scene, entryAssetName = "Environment" },
            new ModTemplate { folderName = "CharacterAnimations", entryKind = EntryKind.AnimationClip,
                entryAssetName = "Animation" },
            new ModTemplate { folderName = "Plugins", entryKind = EntryKind.Plugin, entryAssetName = string.Empty },
        };

        private static readonly string[] WarudoModFolders =
        {
            "Characters", "CharacterAnimations", "Environments", "Props", "Particles", "Plugins",
        };

        private static readonly List<string> Report = new List<string>();

        private static void Say(string message)
        {
            Report.Add(message);
            Debug.Log("[HoModTest] " + message);
        }

        // ---------------------------------------------------------------------
        // batchmode 入口
        // ---------------------------------------------------------------------

        public static void RunAll()
        {
            int exitCode = 1;
            Report.Clear();
            try
            {
                Scaffold();
                SyncWorkspaces();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                BuildAll();
                VerifyOutputs();
                Say("全部完成");
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Say("FAILED: " + exception);
            }
            finally
            {
                try
                {
                    Directory.CreateDirectory(ToAbsolute(OutputRoot));
                    File.WriteAllText(Path.Combine(ToAbsolute(OutputRoot), "build-report.txt"),
                        string.Join("\n", Report));
                }
                catch (Exception writeException)
                {
                    Debug.LogError(writeException);
                }
            }

            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }

        // ---------------------------------------------------------------------
        // 1. 生成各类别最小示例
        // ---------------------------------------------------------------------

        [MenuItem("HoWarudoModTests/1 - 生成各类别最小示例", priority = 0)]
        public static void Scaffold()
        {
            foreach (ModTemplate template in Templates)
            {
                string folder = ModsRoot + "/" + template.folderName;
                Directory.CreateDirectory(ToAbsolute(folder));

                if (template.entryKind == EntryKind.Plugin)
                {
                    // 插件入口是源码，已经在仓库里，这里只确认存在。
                    bool hasPlugin = Directory.GetFiles(ToAbsolute(folder), "*.cs", SearchOption.AllDirectories)
                        .Any(LooksLikePlugin);
                    Say((hasPlugin ? "OK   " : "缺少 ") + template.folderName + "：[PluginType] 源码");
                    continue;
                }

                string entryPath = folder + "/" + template.entryAssetName + EntryExtension(template.entryKind);
                if (File.Exists(ToAbsolute(entryPath)))
                {
                    Say("OK   " + template.folderName + "：入口已存在 " + template.entryAssetName);
                    continue;
                }

                CreateEntryAsset(template, entryPath);
                Say("新建 " + template.folderName + "：入口 " + template.entryAssetName);
            }

            AssetDatabase.SaveAssets();
        }

        private static string EntryExtension(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Prefab: return ".prefab";
                case EntryKind.Scene: return ".unity";
                case EntryKind.AnimationClip: return ".anim";
                default: return string.Empty;
            }
        }

        private static void CreateEntryAsset(ModTemplate template, string entryPath)
        {
            switch (template.entryKind)
            {
                case EntryKind.Prefab:
                    CreateEntryPrefab(template, entryPath);
                    break;
                case EntryKind.Scene:
                    CreateEntryScene(entryPath);
                    break;
                case EntryKind.AnimationClip:
                    CreateEntryAnimationClip(entryPath);
                    break;
            }
        }

        /// <summary>
        /// 在预览场景里搭 Prefab，避免动到用户当前打开的场景。
        /// Props 的根节点挂 Mod 工作区里的脚本 —— 这是"脚本有没有进 Mod 程序集"的验证点。
        /// </summary>
        private static void CreateEntryPrefab(ModTemplate template, string entryPath)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject(template.entryAssetName);
                SceneManager.MoveGameObjectToScene(root, preview);

                if (template.entryAssetName == "Particle")
                {
                    root.AddComponent<ParticleSystem>();
                }
                else
                {
                    GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.name = "Body";
                    SceneManager.MoveGameObjectToScene(body, preview);
                    body.transform.SetParent(root.transform, false);
                    body.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                    root.AddComponent<HoWarudoModTests.Props.HoTestPropSpinner>();
                }

                PrefabUtility.SaveAsPrefabAsset(root, entryPath);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>
        /// 环境入口是场景。用 additive 建、存完立刻关，不动用户当前打开的场景。
        /// </summary>
        private static void CreateEntryScene(string entryPath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var settings = new GameObject("Environment Settings");
                SceneManager.MoveGameObjectToScene(settings, scene);

                // EnvironmentSettings 来自 Warudo.Plugins.Core，用反射挂上，避免硬依赖 SDK 类型。
                Type settingsType = FindType("Warudo.Plugins.Core.Assets.Environment.EnvironmentSettings");
                if (settingsType != null)
                    settings.AddComponent(settingsType);
                else
                    Debug.LogWarning("[HoModTest] 没找到 EnvironmentSettings 类型，环境场景里没有挂它。");

                // 放一个一眼能认出来的标记物：切到这个环境时立刻能看出"环境换掉了"。
                // 没有它的话空场景切过去和没切一样，没法确认加载成功。
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "HoWarudoModTests Marker";
                SceneManager.MoveGameObjectToScene(marker, scene);
                marker.transform.position = new Vector3(0f, 1.5f, 2f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                marker.transform.Rotate(0f, 30f, 0f, Space.Self);

                var light = new GameObject("HoWarudoModTests Light");
                SceneManager.MoveGameObjectToScene(light, scene);
                var lightComponent = light.AddComponent<Light>();
                lightComponent.type = LightType.Directional;
                lightComponent.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                EditorSceneManager.SaveScene(scene, entryPath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>角色动画入口是名为 Animation 的 AnimationClip。</summary>
        private static void CreateEntryAnimationClip(string entryPath)
        {
            var clip = new AnimationClip { name = "Animation" };
            // 放一条无害的曲线，让 clip 不是完全空的。
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x",
                AnimationCurve.Linear(0f, 0f, 1f, 0f));
            AssetDatabase.CreateAsset(clip, entryPath);
        }

        // ---------------------------------------------------------------------
        // 2. 同步工作区：一目录一工作区
        // ---------------------------------------------------------------------

        [MenuItem("HoWarudoModTests/2 - 同步工作区（一目录一工作区）", priority = 1)]
        public static void SyncWorkspaces()
        {
            var settings = GetOrCreateExportSettings();
            if (settings == null)
                throw new InvalidOperationException("拿不到 UMod ExportSettings，请先导入 Warudo Mod SDK。");

            string dataRoot = ResolveWarudoDataRoot(settings);
            if (string.IsNullOrEmpty(dataRoot))
                throw new InvalidOperationException(
                    "推不出 Warudo 数据目录。请先用官方窗口建一个工作区，把导出目录指到 " +
                    "StreamingAssets 下的某个类别目录（例如 .../StreamingAssets/Characters）。");
            Say("Warudo 数据目录：" + dataRoot);

            foreach (ModTemplate template in Templates)
            {
                string folder = ModsRoot + "/" + template.folderName;
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Say("跳过 " + template.folderName + "：目录不存在");
                    continue;
                }

                string exportPath = dataRoot + "/" + template.folderName;
                Directory.CreateDirectory(exportPath.Replace('/', Path.DirectorySeparatorChar));
                UpsertProfile(settings, template.folderName, ToAbsolute(folder), exportPath);
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Say("工作区同步完成");
        }

        /// <summary>
        /// 找到或创建「名称 + 资产目录」都匹配的工作区，写入当前配置，并设为活动工作区。
        /// 已存在就更新，不重复创建，也不还原。
        /// </summary>
        private static void UpsertProfile(UnityEngine.Object settings, string modName, string assetFolder,
            string exportPath)
        {
            var profiles = GetMember(settings, "ExportProfiles") as Array;
            int index = -1;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Length; i++)
                {
                    object candidate = profiles.GetValue(i);
                    if (string.Equals(GetMember(candidate, "ModName") as string, modName, StringComparison.Ordinal) &&
                        string.Equals(ToCanonicalAssetPath(GetMember(candidate, "ModAssetsPath") as string),
                            ToCanonicalAssetPath(assetFolder),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                if (profiles != null && profiles.Cast<object>().Any(candidate =>
                        string.Equals(GetMember(candidate, "ModName") as string, modName, StringComparison.Ordinal)))
                {
                    Say("!! 已有同名工作区「" + modName + "」但资产目录不同，跳过");
                    return;
                }

                InvokeMember(settings, "CreateNewExportProfile", new object[] { true });
                index = (int)GetMember(settings, "ActiveExportProfileIndex");
                profiles = GetMember(settings, "ExportProfiles") as Array;
                Say("  + 新建工作区 " + modName);
            }
            else
            {
                InvokeMember(settings, "SetActiveExportProfile", new object[] { index });
                Say("  ~ 更新工作区 " + modName);
            }

            if (profiles == null || index < 0 || index >= profiles.Length)
                throw new InvalidOperationException("定位工作区失败：" + modName);

            object profile = profiles.GetValue(index);
            SetMember(profile, "ModName", modName);
            SetMember(profile, "ModAuthor", "Hollow");
            SetMember(profile, "ModVersion", "1.0.0");
            SetMember(profile, "ModDescription", "HoWarudoModTests 最小示例：" + modName);
            SetMember(profile, "ModAssetsPath", assetFolder);
            SetMember(profile, "ModExportPath", exportPath);
        }

        /// <summary>
        /// 从任意一个已有工作区的导出目录反推 Warudo 数据目录：末段是已知类别目录就退一级。
        /// </summary>
        private static string ResolveWarudoDataRoot(UnityEngine.Object settings)
        {
            var profiles = GetMember(settings, "ExportProfiles") as Array;
            if (profiles == null)
                return string.Empty;

            foreach (object profile in profiles)
            {
                string export = NormalizePath(GetMember(profile, "ModExportPath") as string);
                if (string.IsNullOrEmpty(export))
                    continue;

                int lastSlash = export.LastIndexOf('/');
                if (lastSlash < 0)
                    continue;

                string leaf = export.Substring(lastSlash + 1);
                if (WarudoModFolders.Contains(leaf, StringComparer.OrdinalIgnoreCase))
                    return export.Substring(0, lastSlash);
            }

            return string.Empty;
        }

        // ---------------------------------------------------------------------
        // 3. 构建全部
        // ---------------------------------------------------------------------

        [MenuItem("HoWarudoModTests/3 - 构建全部", priority = 2)]
        public static void BuildAll()
        {
            var settings = GetOrCreateExportSettings();
            if (settings == null)
                throw new InvalidOperationException("拿不到 UMod ExportSettings。");

            EnsureEditorProjectFiles();

            var profiles = GetMember(settings, "ExportProfiles") as Array;
            if (profiles == null)
                return;

            string modsRootAbsolute = ToAbsolute(ModsRoot);
            for (int index = 0; index < profiles.Length; index++)
            {
                object profile = profiles.GetValue(index);
                var modName = GetMember(profile, "ModName") as string;
                string assetPath = ToCanonicalAssetPath(GetMember(profile, "ModAssetsPath") as string);
                if (!assetPath.StartsWith(modsRootAbsolute, StringComparison.OrdinalIgnoreCase))
                    continue;   // 不是本仓库的工作区，不碰

                var exportPath = GetMember(profile, "ModExportPath") as string;
                Directory.CreateDirectory(exportPath);

                InvokeMember(settings, "SetActiveExportProfile", new object[] { index });
                Say("=== 构建 " + modName + " -> " + exportPath);

                object result = InvokeStartBuild(settings);
                if (result == null)
                    throw new InvalidOperationException(modName + "：StartBuild 返回 null");

                bool ok = GetMember(result, "Successful") is bool flag && flag;
                var file = GetMember(result, "BuiltModFile") as FileInfo;
                var error = GetMember(result, "ErrorMessage") as string;
                Say("    successful=" + ok + " file=" + (file != null ? file.FullName : "(none)") +
                    " error=" + (error ?? "(none)"));

                if (!ok)
                    throw new InvalidOperationException(modName + " 构建失败：" + error);
            }
        }

        // ---------------------------------------------------------------------
        // 4. 校验产物
        // ---------------------------------------------------------------------

        [MenuItem("HoWarudoModTests/4 - 校验产物 .warudo", priority = 3)]
        public static void VerifyOutputs()
        {
            var settings = GetOrCreateExportSettings();
            string dataRoot = settings == null ? string.Empty : ResolveWarudoDataRoot(settings);

            var directories = new List<string> { ToAbsolute(OutputRoot) };
            if (!string.IsNullOrEmpty(dataRoot))
                directories.AddRange(WarudoModFolders.Select(folder => dataRoot + "/" + folder));

            int found = 0;
            foreach (string directory in directories.Where(Directory.Exists).Distinct())
            {
                foreach (string file in Directory.GetFiles(directory, "*.warudo", SearchOption.TopDirectoryOnly))
                {
                    Say(DescribePackage(file));
                    found++;
                }
            }

            if (found == 0)
                Say("没有找到任何 .warudo 产物。");
        }

        /// <summary>
        /// 读 Warudo 的日志，确认它到底认没认这些包。
        ///
        /// 说实话：日志只记录 Plugins 与 Characters 的加载（走 ModHost / PluginMonitor）。
        /// Props / Particles / Environments / CharacterAnimations 只会写一行
        /// "Started monitoring &lt;目录&gt;"，不记具体文件 —— 那几类只能进 Warudo 在对应下拉里看。
        /// 但对插件包这条是硬证据，而且能抓到"加载失败"（例如运行时安全审查）。
        /// </summary>
        [MenuItem("HoWarudoModTests/5 - 检查 Warudo 日志（认没认）", priority = 4)]
        public static void CheckWarudoLog()
        {
            string logDirectory = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "..", "LocalLow", "HakuyaLabs", "Warudo", "Logs"));

            if (!Directory.Exists(logDirectory))
            {
                Debug.LogWarning("[HoModTest] 找不到 Warudo 日志目录：" + logDirectory);
                return;
            }

            string newest = Directory.GetFiles(logDirectory, "*.log.gz", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(newest))
            {
                Debug.LogWarning("[HoModTest] 日志目录里没有 *.log.gz：" + logDirectory);
                return;
            }

            string[] lines;
            using (var file = File.OpenRead(newest))
            using (var gzip = new System.IO.Compression.GZipStream(file,
                       System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                lines = reader.ReadToEnd().Split('\n');
            }

            var sb = new StringBuilder();
            sb.AppendLine("Warudo 日志：" + newest);
            sb.AppendLine("最后写入：" + File.GetLastWriteTime(newest).ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("说明：日志只记录 Plugins / Characters 的加载过程；");
            sb.AppendLine("      Props/Particles/Environments/CharacterAnimations 只在启动时写一行");
            sb.AppendLine("      \"Started monitoring <目录>\"，不记具体文件 —— 那几类要进 Warudo 在对应下拉里看。");
            sb.AppendLine();

            foreach (ModTemplate template in Templates)
            {
                sb.AppendLine("=== " + template.folderName + "  (" + template.entryKind + ")");

                string artifactName = template.folderName + ".warudo";
                var related = lines
                    .Where(line => line.IndexOf(artifactName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   line.IndexOf("Load mod:  " + template.folderName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   line.IndexOf("Started monitoring " + template.folderName + " ",
                                       StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(line => line.Trim())
                    .Distinct()
                    .ToArray();

                if (related.Length == 0)
                    sb.AppendLine("    日志里没有它的记录。资源类 Mod 正常如此；若是 Plugins，说明 Warudo 还没扫到这个包。");
                else
                    foreach (string line in related)
                        sb.AppendLine("    " + line);

                sb.AppendLine();
            }

            // 全局扫一遍失败记录：可能是我们的包，也可能是别人的。
            var failures = lines
                .Where(line => line.IndexOf("Failed to load", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               line.IndexOf("has failed code security verification", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(line => line.Trim())
                .Distinct()
                .ToArray();

            if (failures.Length > 0)
            {
                sb.AppendLine("=== 日志里出现的加载失败 ===");
                foreach (string line in failures)
                    sb.AppendLine("    " + line);
            }

            string report = sb.ToString();
            Debug.Log("[HoModTest] Warudo 日志检查\n" + report);

            string outDir = ToAbsolute(OutputRoot);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "warudo-log-check.txt"), report);
        }

        /// <summary>解析 .warudo（12 字节 UMOD 头 + 标准 ZIP），列出包内条目。</summary>
        public static string DescribePackage(string path)
        {
            var sb = new StringBuilder();
            var info = new FileInfo(path);
            sb.AppendLine("产物：" + path);
            sb.AppendLine("  大小：" + info.Length.ToString("N0") + " B");

            if (info.Length > 256L * 1024 * 1024)
            {
                sb.AppendLine("  （文件过大，跳过条目解析）");
                return sb.ToString();
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 'U' || bytes[1] != 'M' || bytes[2] != 'O' || bytes[3] != 'D')
            {
                sb.AppendLine("  !! 不是 UMOD 容器（缺少 UMOD 魔数）");
                return sb.ToString();
            }
            sb.AppendLine("  容器：UMOD 头 12 字节 + ZIP");

            int eocd = -1;
            int lowest = Math.Max(0, bytes.Length - 22 - 0xFFFF);
            for (int i = bytes.Length - 22; i >= lowest; i--)
            {
                if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
                {
                    eocd = i;
                    break;
                }
            }
            if (eocd < 0)
            {
                sb.AppendLine("  !! 找不到 ZIP 中央目录结尾记录");
                return sb.ToString();
            }

            int total = BitConverter.ToUInt16(bytes, eocd + 10);
            uint cdOffset = BitConverter.ToUInt32(bytes, eocd + 16);
            int pos = 12 + (int)cdOffset;

            var names = new List<string>();
            for (int entry = 0; entry < total; entry++)
            {
                if (pos + 46 > bytes.Length) break;
                if (!(bytes[pos] == 0x50 && bytes[pos + 1] == 0x4B && bytes[pos + 2] == 0x01 &&
                      bytes[pos + 3] == 0x02)) break;
                uint size = BitConverter.ToUInt32(bytes, pos + 24);
                ushort nameLength = BitConverter.ToUInt16(bytes, pos + 28);
                ushort extraLength = BitConverter.ToUInt16(bytes, pos + 30);
                ushort commentLength = BitConverter.ToUInt16(bytes, pos + 32);
                string name = Encoding.UTF8.GetString(bytes, pos + 46, nameLength);
                names.Add(name);
                sb.AppendLine(string.Format("    {0,-26} {1,14:N0} B", name, size));
                pos += 46 + nameLength + extraLength + commentLength;
            }

            bool hasAssembly = names.Any(n => n.Equals("assemblymodules.dat", StringComparison.OrdinalIgnoreCase));
            bool hasShared = names.Any(n => n.StartsWith("sharedassets", StringComparison.OrdinalIgnoreCase));
            bool hasScene = names.Any(n => n.StartsWith("sceneassets", StringComparison.OrdinalIgnoreCase));

            sb.AppendLine("  判定：");
            sb.AppendLine("    modinfo.dat        : " + names.Contains("modinfo.dat"));
            sb.AppendLine("    sharedassets.*     : " + hasShared);
            sb.AppendLine("    sceneassets.*      : " + hasScene);
            sb.AppendLine("    assemblymodules.dat: " + hasAssembly +
                          (hasAssembly ? "   <- 脚本已编入 Mod 程序集" : "   <- 没有任何脚本被编译"));
            return sb.ToString();
        }

        // ---------------------------------------------------------------------
        // UMod 访问（全部反射）
        // ---------------------------------------------------------------------

        private static UnityEngine.Object GetOrCreateExportSettings()
        {
            Type settingsType = FindType("UMod.ModTools.Export.ExportSettings");
            if (settingsType == null)
                return null;

            UnityEngine.Object settings = null;
            PropertyInfo active = FindStaticProperty(settingsType, "Active");
            if (active != null)
                settings = active.GetValue(null) as UnityEngine.Object;

            if (settings == null)
            {
                foreach (string guid in AssetDatabase.FindAssets("ExportSettings"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null && asset.GetType() == settingsType) { settings = asset; break; }
                }
            }

            if (settings == null)
            {
                Directory.CreateDirectory(ToAbsolute(RepoAssetRoot) + "/ExportSettings");
                settings = ScriptableObject.CreateInstance(settingsType);
                settings.name = "ExportSettings";
                AssetDatabase.CreateAsset(settings, RepoAssetRoot + "/ExportSettings/ExportSettings.asset");
                AssetDatabase.SaveAssets();
            }

            MethodInfo load = settingsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Instance);
            if (load != null && load.GetParameters().Length == 0)
                load.Invoke(settings, null);

            return settings;
        }

        private static object InvokeStartBuild(UnityEngine.Object settings)
        {
            Type toolsType = FindType("UMod.BuildEngine.ModToolsUtil");
            if (toolsType == null)
                throw new InvalidOperationException("没找到 UMod.BuildEngine.ModToolsUtil");

            MethodInfo method = toolsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(candidate => candidate.Name == "StartBuild")
                .Where(candidate =>
                {
                    var parameters = candidate.GetParameters();
                    return parameters.Length >= 1 && parameters.Length <= 2 &&
                           parameters[0].ParameterType.IsInstanceOfType(settings) &&
                           parameters.Skip(1).All(parameter =>
                               parameter.HasDefaultValue || typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
                })
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();

            if (method == null)
                throw new MissingMethodException("没有兼容的 ModToolsUtil.StartBuild(ExportSettings)");

            ParameterInfo[] methodParameters = method.GetParameters();
            object[] arguments = new object[methodParameters.Length];
            arguments[0] = settings;
            for (int index = 1; index < arguments.Length; index++)
            {
                arguments[index] = methodParameters[index].HasDefaultValue
                    ? methodParameters[index].DefaultValue
                    : null;
            }

            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }

        /// <summary>
        /// UMod 是靠 Unity 生成的 .csproj 里的 &lt;Compile Include="Assets\..."&gt; 决定编译哪些脚本的，
        /// batchmode 不会自动生成，这里补一次。
        /// </summary>
        private static void EnsureEditorProjectFiles()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (SafeGetProjectFiles(projectRoot).Length > 0)
                return;

            string[] syncTypes = { "UnityEditor.CodeEditorProjectSync", "UnityEditor.SyncVS" };
            string[] syncMethods = { "SyncEditorProject", "SyncSolution" };
            foreach (string typeName in syncTypes)
            {
                Type type = FindType(typeName);
                if (type == null) continue;
                foreach (string methodName in syncMethods)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                           BindingFlags.Static)
                                               .Where(m => m.Name == methodName))
                    {
                        if (method.GetParameters().Length > 1) continue;
                        try
                        {
                            method.Invoke(null, method.GetParameters().Length == 0 ? null : new object[] { null });
                            Say("已调用 " + typeName + "." + methodName + "()");
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("[HoModTest] " + typeName + "." + methodName + " 异常：" +
                                             exception.Message);
                        }
                    }
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Say("工程根目录 .csproj 数量：" + SafeGetProjectFiles(projectRoot).Length);
        }

        private static string[] SafeGetProjectFiles(string projectRoot)
        {
            try { return Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly); }
            catch (Exception) { return new string[0]; }
        }

        // ---------------------------------------------------------------------
        // 小工具
        // ---------------------------------------------------------------------

        private static bool LooksLikePlugin(string assetPath)
        {
            try
            {
                string absolute = Path.GetFullPath(assetPath);
                return File.Exists(absolute) && File.ReadAllText(absolute).Contains("[PluginType");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace('\\', '/');
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// 工作区里的 modAssetPath 有两种写法并存：官方「New Mod」向导写相对路径
        /// （Assets/...），本仓库写绝对路径（D:/.../Assets/...）。
        /// 比较前必须归一化，否则同一条工作区会被当成"新的"而重复创建。
        /// </summary>
        private static string ToCanonicalAssetPath(string path)
        {
            string normalized = NormalizePath(path);
            if (normalized.Length == 0)
                return string.Empty;

            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return ToAbsolute(normalized);

            return normalized;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType(fullName, false); }
                catch (Exception) { continue; }
                if (type != null) return type;
            }
            return null;
        }

        private static PropertyInfo FindStaticProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null) return property.GetValue(target);
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(target);
        }

        private static void SetMember(object target, string name, object value)
        {
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) { property.SetValue(target, value); return; }
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) { field.SetValue(target, value); return; }
            Debug.LogWarning("[HoModTest] 无法设置 " + type.Name + "." + name);
        }

        private static object InvokeMember(object target, string name, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, arguments);
        }
    }
}

// HoModTestBuilder.cs
// =============================================================================
// HoWarudoModTests 仓库的构建入口（Editor）。
//
// 它做四件事：
//   1) 生成道具 Mod 需要的根预制体  Mods/HoTestProp/Prop.prefab
//   2) 配置 UMod 的两个导出工作区（Export Profile）：HoTestProp / HoTestPlugin
//   3) 调用官方构建入口 UMod.BuildEngine.ModToolsUtil.StartBuild
//   4) 自己解析产物 .warudo，报告包内条目 —— 这就是"最小打包验证"
//
// 全部 UMod API 都通过反射访问：SDK 版本之间成员会变，这里不想因为签名变化编译失败。
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

namespace HoWarudoModTests.EditorTools
{
    public static class HoModTestBuilder
    {
        public const string RepoAssetRoot = "Assets/HoWarudoModTests";
        public const string PropModAssetRoot = RepoAssetRoot + "/Mods/HoTestProp";
        public const string PluginModAssetRoot = RepoAssetRoot + "/Mods/HoTestPlugin";
        public const string OutputRoot = RepoAssetRoot + "/out";

        private static readonly List<string> Report = new List<string>();

        private static void Say(string message)
        {
            Report.Add(message);
            Debug.Log("[HoModTest] " + message);
        }

        // ---------------------------------------------------------------------
        // 菜单入口
        // ---------------------------------------------------------------------

        [MenuItem("HoWarudoModTests/1 - 生成 Prop 预制体", priority = 0)]
        public static void CreatePropPrefab()
        {
            string folder = ToAbsolute(PropModAssetRoot);
            Directory.CreateDirectory(folder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 官方约定：道具 Mod 的根 Prefab 必须叫 Prop。
            var root = new GameObject("Prop");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            // 挂上 Mod 工作区里的脚本 —— 这是"脚本是否真的进了 Mod 程序集"的验证点。
            root.AddComponent<HoWarudoModTests.Prop.HoTestPropSpinner>();

            string prefabPath = PropModAssetRoot + "/Prop.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log("[HoModTest] Prop 预制体 -> " + prefabPath + " (" + (prefab != null ? "OK" : "失败") + ")");
        }

        [MenuItem("HoWarudoModTests/2 - 配置导出工作区", priority = 1)]
        public static void ConfigureProfiles()
        {
            var settings = GetOrCreateExportSettings();
            if (settings == null) throw new InvalidOperationException("拿不到 UMod ExportSettings");

            // 幂等：先清空再建，反复执行结果一致。
            while ((int)GetMember(settings, "ExportProfileCount") > 0)
            {
                InvokeMember(settings, "DeleteExportProfile", new object[] { 0 });
            }

            AddProfile(settings, "HoTestProp", ToAbsolute(PropModAssetRoot), ToAbsolute(OutputRoot + "/Props"));
            AddProfile(settings, "HoTestPlugin", ToAbsolute(PluginModAssetRoot), ToAbsolute(OutputRoot + "/Plugins"));

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[HoModTest] 导出工作区已配置：HoTestProp -> out/Props，HoTestPlugin -> out/Plugins");
        }

        [MenuItem("HoWarudoModTests/3 - 构建全部 Mod", priority = 2)]
        public static void BuildAll()
        {
            var settings = GetOrCreateExportSettings();
            if (settings == null) throw new InvalidOperationException("拿不到 UMod ExportSettings");

            EnsureEditorProjectFiles();

            int count = (int)GetMember(settings, "ExportProfileCount");
            for (int index = 0; index < count; index++)
            {
                InvokeMember(settings, "SetActiveExportProfile", new object[] { index });
                object profile = GetMember(settings, "ActiveExportProfile");
                string modName = (string)GetMember(profile, "ModName");
                string exportPath = (string)GetMember(profile, "ModExportPath");
                Directory.CreateDirectory(exportPath);

                Debug.Log("[HoModTest] === 构建 " + modName + " -> " + exportPath);
                object result = InvokeStartBuild(settings);
                if (result == null) throw new InvalidOperationException(modName + "：StartBuild 返回 null");

                bool ok = (bool)GetMember(result, "Successful");
                var file = GetMember(result, "BuiltModFile") as FileInfo;
                var error = GetMember(result, "ErrorMessage") as string;
                Debug.Log("[HoModTest] " + modName + " successful=" + ok +
                          " file=" + (file != null ? file.FullName : "(none)") +
                          " error=" + (error ?? "(none)"));

                if (!ok) throw new InvalidOperationException(modName + " 构建失败：" + error);
            }
        }

        [MenuItem("HoWarudoModTests/4 - 校验产物 .warudo", priority = 3)]
        public static void VerifyOutputs()
        {
            string outDir = ToAbsolute(OutputRoot);
            if (!Directory.Exists(outDir))
            {
                Debug.LogWarning("[HoModTest] 还没有产物目录：" + outDir);
                return;
            }
            foreach (string file in Directory.GetFiles(outDir, "*.warudo", SearchOption.AllDirectories))
            {
                Debug.Log(DescribePackage(file));
            }
        }

        // ---------------------------------------------------------------------
        // batchmode 入口
        // ---------------------------------------------------------------------

        /// <summary>
        /// Unity.exe -batchmode -projectPath &lt;工程&gt; \
        ///   -executeMethod HoWarudoModTests.EditorTools.HoModTestBuilder.RunAll -logFile &lt;log&gt;
        /// </summary>
        public static void RunAll()
        {
            int exitCode = 1;
            Report.Clear();
            try
            {
                CreatePropPrefab();
                ConfigureProfiles();
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
                    string outDir = ToAbsolute(OutputRoot);
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, "build-report.txt"), string.Join("\n", Report));
                }
                catch (Exception writeException)
                {
                    Debug.LogError(writeException);
                }
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        // ---------------------------------------------------------------------
        // .warudo 产物解析（12 字节 UMod 头 + 标准 ZIP）
        // ---------------------------------------------------------------------

        /// <summary>解析 .warudo 的 ZIP 中央目录，列出包内条目。</summary>
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
                if (!(bytes[pos] == 0x50 && bytes[pos + 1] == 0x4B && bytes[pos + 2] == 0x01 && bytes[pos + 3] == 0x02)) break;
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
            {
                Debug.LogError("[HoModTest] 没找到 UMod.ModTools.Export.ExportSettings —— Warudo Mod SDK 没导入？");
                return null;
            }

            UnityEngine.Object settings = null;
            PropertyInfo active = FindStaticProperty(settingsType, "Active");
            if (active != null) settings = active.GetValue(null) as UnityEngine.Object;

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
                string dir = ToAbsolute(RepoAssetRoot) + "/ExportSettings";
                Directory.CreateDirectory(dir);
                settings = ScriptableObject.CreateInstance(settingsType);
                settings.name = "ExportSettings";
                AssetDatabase.CreateAsset(settings, RepoAssetRoot + "/ExportSettings/ExportSettings.asset");
                AssetDatabase.SaveAssets();
                Debug.Log("[HoModTest] 新建了 ExportSettings 资源");
            }

            MethodInfo load = settingsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Instance);
            if (load != null && load.GetParameters().Length == 0) load.Invoke(settings, null);

            return settings;
        }

        private static void AddProfile(UnityEngine.Object settings, string modName, string assetsPath, string exportPath)
        {
            MethodInfo create = settings.GetType().GetMethod("CreateNewExportProfile", new[] { typeof(bool) });
            if (create == null) throw new MissingMethodException("ExportSettings.CreateNewExportProfile(bool) 不存在");

            object profile = create.Invoke(settings, new object[] { true });
            SetMember(profile, "ModName", modName);
            SetMember(profile, "ModAuthor", "Hollow");
            SetMember(profile, "ModVersion", "1.0.0");
            SetMember(profile, "ModDescription", "Warudo mod packaging test (" + modName + ").");
            SetMember(profile, "ModAssetsPath", assetsPath);
            SetMember(profile, "ModExportPath", exportPath);
            Debug.Log("[HoModTest]   + 工作区 " + modName + "  assets=" + assetsPath);
        }

        private static object InvokeStartBuild(UnityEngine.Object settings)
        {
            Type toolsType = FindType("UMod.BuildEngine.ModToolsUtil");
            if (toolsType == null) throw new InvalidOperationException("没找到 UMod.BuildEngine.ModToolsUtil");

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

            if (method == null) throw new MissingMethodException("没有兼容的 ModToolsUtil.StartBuild(ExportSettings)");

            ParameterInfo[] methodParameters = method.GetParameters();
            object[] arguments = new object[methodParameters.Length];
            arguments[0] = settings;
            for (int index = 1; index < arguments.Length; index++)
            {
                arguments[index] = methodParameters[index].HasDefaultValue ? methodParameters[index].DefaultValue : null;
            }

            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }

        /// <summary>
        /// UMod 是靠 Unity 生成的 .csproj 里的 &lt;Compile Include="Assets\..."&gt; 决定编译哪些脚本的。
        /// 工程根目录没有 .csproj 时，脚本会被整段跳过，产物里不会有 assemblymodules.dat。
        /// </summary>
        private static void EnsureEditorProjectFiles()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string[] before = SafeGetProjectFiles(projectRoot);
            Debug.Log("[HoModTest] 构建前 .csproj 数量：" + before.Length);
            if (before.Length > 0) return;

            string[] syncTypes = { "UnityEditor.CodeEditorProjectSync", "UnityEditor.SyncVS" };
            string[] syncMethods = { "SyncEditorProject", "SyncSolution" };
            foreach (string typeName in syncTypes)
            {
                Type type = FindType(typeName);
                if (type == null) continue;
                foreach (string methodName in syncMethods)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                                 .Where(m => m.Name == methodName))
                    {
                        if (method.GetParameters().Length > 1) continue;
                        try
                        {
                            method.Invoke(null, method.GetParameters().Length == 0 ? null : new object[] { null });
                            Debug.Log("[HoModTest] 已调用 " + typeName + "." + methodName + "()");
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("[HoModTest] " + typeName + "." + methodName + " 异常：" + exception.Message);
                        }
                    }
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[HoModTest] 构建后 .csproj 数量：" + SafeGetProjectFiles(projectRoot).Length);
        }

        private static string[] SafeGetProjectFiles(string projectRoot)
        {
            try { return Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly); }
            catch (Exception) { return new string[0]; }
        }

        // ---------------------------------------------------------------------
        // 小工具
        // ---------------------------------------------------------------------

        private static string ToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace('\\', '/');
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
            if (target == null) throw new ArgumentNullException("target");
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null) return property.GetValue(target);
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return field.GetValue(target);
            throw new MissingMemberException(type.FullName, name);
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

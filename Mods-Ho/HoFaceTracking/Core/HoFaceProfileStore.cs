// HoFaceProfileStore.cs  --  中间层配置文件在**插件沙箱**里的存取
//
// 【为什么是文件，不是 Mod 资源】
// 资源型打包（sharedassets）那条路我们砍了：中间层是我们自己的 JSON，它应该能 diff、
// 能用任何编辑器打开、能随手发给别人，而不是被塞进 Unity 的资源包里。
// Warudo 为此提供了沙箱化的文件 API：`Plugin.PersistentData`（PluginPersistentDataManager），
// 挂在插件上，**只有插件拿得到** —— 所以节点走 HoFaceTrackingPlugin.Files 把它递进来。
//
// 【⚠️ 为什么不用 GetFiles，只用 GetFileEntries】
// `GetFiles(relativePath, searchPattern, SearchOption)` 的第三个参数类型是
// **`System.IO.SearchOption`**，而 UMod 的构建期 API 审查**禁止引用 `System.IO.*`** ——
// 写了它这个 Mod 直接构建失败。`GetFileEntries(relativePath, searchPattern, Func<string,bool>)`
// 签名里没有任何 System.IO 类型，所以这是唯一能列目录的入口。
//
// 【一次读盘、按时间戳失效】
// 面板上改完文件想在 Warudo 里立刻看到效果，就得知道文件变没变。`FileEntry.lastModifiedTime`
// 正好是干这个的：同一路径 + 同一时间戳就复用上次解析的结果。

using System;
using System.Collections.Generic;
using Warudo.Core.Persistence;

namespace HoFaceTracking.Core
{
    /// <summary>沙箱里 `*.hoface.json` 的列表与读取。静态单例式，由节点每帧喂插件句柄进来。</summary>
    public static class HoFaceProfileStore
    {
        /// <summary>配置文件后缀（与 Unity 侧 <c>HoFaceProfile.Extension</c> 一致）。</summary>
        public const string Extension = ".hoface.json";

        private sealed class Cached
        {
            public long stamp;
            public HoFaceMiddleware middleware;
            public string error;
        }

        private static PluginPersistentDataManager files;
        private static readonly List<FileEntry> entries = new List<FileEntry>();
        private static readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.Ordinal);

        /// <summary>
        /// 上次重新列目录的时刻。
        ///
        /// 为什么需要它：<see cref="Attach"/> 只在插件句柄**变了**的时候才列目录，
        /// 所以运行期往沙箱里新丢一个配置文件，不重新列就永远看不见（"改文件能生效、
        /// 加文件不生效"这种半吊子行为比不生效还难查）。这里改成**找不到就重列一次**，
        /// 用一秒的冷却挡住"名字打错 → 每帧重列"的空转。
        /// </summary>
        private static float lastRefresh;

        private const float RefreshCooldown = 1f;

        /// <summary>沙箱根目录（面板上要告诉用户"把文件放这儿"）。还没接上时是空串。</summary>
        public static string Root { get; private set; }

        /// <summary>列表 / 读盘本身出的问题（不是"配置文件内容有错"）。</summary>
        public static string Error { get; private set; }

        /// <summary>插件句柄到了没有。</summary>
        public static bool Ready { get { return files != null; } }

        /// <summary>沙箱里现有的配置文件（相对路径，按名字排序）。</summary>
        public static IList<FileEntry> Entries { get { return entries; } }

        /// <summary>
        /// 接上插件的沙箱句柄。**每帧调也无所谓**（同一个句柄就直接返回），
        /// 这样插件被重建、或者节点比插件先跑一帧，都能自愈。
        /// </summary>
        public static void Attach(PluginPersistentDataManager manager)
        {
            if (manager == null) return;
            if (ReferenceEquals(manager, files)) return;

            files = manager;
            cache.Clear();
            Refresh();
        }

        /// <summary>重新列一遍沙箱里的配置文件。列出错时把原因留在 <see cref="Error"/> 里。</summary>
        public static void Refresh()
        {
            entries.Clear();
            Error = null;
            if (files == null) { Error = "插件的沙箱还没就绪。"; return; }

            try
            {
                Root = files.GetBasePath();
            }
            catch (Exception e)
            {
                Root = null;
                Error = "拿不到沙箱根目录：" + e.Message;
                return;
            }

            try
            {
                // predicate 传 null 有风险（实现里可能直接调用它），所以给个恒真的。
                var found = files.GetFileEntries("", "*" + Extension, path => true);
                if (found != null)
                    foreach (var entry in found)
                        if (entry != null && !string.IsNullOrEmpty(entry.relativePath)) entries.Add(entry);
                entries.Sort((a, b) => string.CompareOrdinal(a.fileName, b.fileName));
            }
            catch (Exception e)
            {
                Error = "列沙箱目录失败：" + e.Message;
                return;
            }

            // ⚠️ **不再自动写样板**（2026-09-25 用户明确要求："我不允许他有默认配置"）。
            // 以前沙箱空着时会写一份内置默认（`StarterName`）下去 —— 那份表还是过时的 iFacialMocap 那套，
            // 而且"删了配置它又自己冒出来、参数还在输出"这件事就是这里造成的，非常难查。
            // 现在的规矩：**沙箱里有什么就是什么**，一份都没有 = 参数处理这一层不做事，
            // 该做什么由节点 `状态` 口里那几句话告诉你（路径 + 可以往里丢什么）。
        }

        /// <summary>按名字找一份配置：认 `fileName` 也认 `relativePath`。找不到返回 null。</summary>
        private static FileEntry Find(string name)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.Equals(entry.fileName, name, StringComparison.Ordinal)
                    || string.Equals(entry.relativePath, name, StringComparison.Ordinal))
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// 按名字取一份配置（认 `fileName` 也认 `relativePath`）。解析结果按文件时间戳缓存。
        /// </summary>
        /// <param name="name">用户在节点上填的文件名。</param>
        /// <param name="stamp">这份配置的版本号（时间戳）。换了配置它就会变，用来判断要不要重新编译链。</param>
        public static bool TryGet(string name, out HoFaceMiddleware middleware, out long stamp, out string error)
        {
            middleware = null;
            stamp = 0L;
            error = null;
            if (files == null) { error = "插件的沙箱还没就绪。"; return false; }
            if (string.IsNullOrEmpty(name)) { error = "没填配置文件名。"; return false; }

            FileEntry found = Find(name);

            // 没找到就重新列一次目录（带冷却）—— 运行中新丢进来的配置文件这样才看得见。
            if (found == null && UnityEngine.Time.realtimeSinceStartup - lastRefresh > RefreshCooldown)
            {
                lastRefresh = UnityEngine.Time.realtimeSinceStartup;
                Refresh();
                found = Find(name);
            }

            if (found == null)
            {
                error = "沙箱里没有这个配置文件：" + name;
                return false;
            }

            Cached hit;
            if (cache.TryGetValue(found.relativePath, out hit) && hit.stamp == found.lastModifiedTime)
            {
                middleware = hit.middleware;
                error = hit.error;
                stamp = hit.stamp;
                return middleware != null;
            }

            var fresh = new Cached { stamp = found.lastModifiedTime };
            try
            {
                string text = files.ReadFile(found.relativePath);
                HoFaceMiddleware parsed;
                string parseError;
                if (HoFaceProfile.TryParse(text, out parsed, out parseError))
                    fresh.middleware = parsed;
                else
                    fresh.error = parseError;
            }
            catch (Exception e)
            {
                fresh.error = "读文件失败：" + e.Message;
            }

            cache[found.relativePath] = fresh;
            middleware = fresh.middleware;
            error = fresh.error;
            stamp = fresh.stamp;
            return middleware != null;
        }
    }
}

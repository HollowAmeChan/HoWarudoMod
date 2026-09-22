// HoTestSourceAsset.cs  -- FromSourceGameObjectAsset 基类（「从来源加载」型资源）
//
// 这是道具（PropAsset）和角色（CharacterAsset）走的**同一条路**：
// 不自己造 GameObject，而是先让用户从「来源」列表里挑一个，再把那个来源加载成 GameObject。
//
// ✅ 探针实测，FromSourceGameObjectAsset 只要求子类实现**一个**抽象成员：
//
//       protected override UniTask<AutoCompleteList> GetSources();
//
//   也就是说 CreateGameObject() 基类已经实现了（它负责从选中的 Source 加载）。
//   它自带两个数据输入： [DataInput] string Source、[Markdown] string SourceMeta。
//
// ─────────────────────────────────────────────────────────────
// 「来源」从哪来：Warudo 的资源提供器体系
//
// ✅ 本机元数据转储实测到的 API 链：
//
//   Warudo.Core.Context.get_ResourceManager()            -> ResourceManager
//   ResourceManager.ProvideResources(string kind)        -> List<ResourceProviderResult>
//   ResourceProviderResult { string providerName; List<Resource> resources; }
//   Resource              { string category; string label; Uri uri; }
//   ResourceUriProviderResultExtensions.ToAutoCompleteList(List<ResourceProviderResult>)
//                                                        -> AutoCompleteList   (扩展方法)
//
// kind 的取值是本机在 Warudo.Plugins.Core.dll 的 **UTF-16 字面量堆**里实测到的：
//   prop / character / particle / environment / image / sound / video / LUT

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Warudo.Core;
using Warudo.Core.Attributes;
using Warudo.Core.Data;
using Warudo.Core.Resource;
using Warudo.Plugins.Core.Assets;

namespace HoWarudoModTests.CustomAsset
{
    [AssetType(
        Id = "17da3e48-90fb-42c6-e47d-bf805c9d3107",
        Title = "Ho Test Source Prop",
        Category = "CATEGORY_PROP")]
    public class HoTestSourceAsset : FromSourceGameObjectAsset
    {
        // 列出来源候选。用户选中后 Source 字段拿到 Resource.uri 的字符串形式，
        // 基类再拿它去加载 GameObject。
        protected override UniTask<AutoCompleteList> GetSources()
        {
            // ⚠️ 0.15.0 里 `Context` 不是 Asset 上的成员（官方 VMC 示例里的
            //    `Context.PluginManager` 是 0.14.x 的写法），它是 `Warudo.Core.Context`
            //    这个类型上的**静态**成员，必须 `using Warudo.Core;` 之后用
            //    `Context.ResourceManager` 取，不能写成 `this.Context` 或 `Context.Instance.xxx`。
            var results = Context.ResourceManager.ProvideResources("prop");

            // ❓ 待实测：ProvideResources("prop") 是否真的能列出工程里的道具来源。
            //    "prop" 这个 kind 字面量是本机从 Plugins.Core 的 UTF-16 字面量堆里
            //    实测到的，但它到底是喂给 ProvideResources 的那个参数、还是别处的用途，
            //    没有实机确认过。如果下拉框是空的，第一个要试的是换 kind。
            return UniTask.FromResult(results.ToAutoCompleteList());
        }
    }
}

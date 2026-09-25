// HoFaceTrackingPlugin.cs  --  插件 Mod 入口
//
// 【这个 Mod 是什么】
// 面捕：**接收 + 处理**，两半都在这一个 Mod 里。
//   ① 接收器节点：把来源发来的原始数据收进来，**原样**交出去（不做改名、不做量纲）。
//      两种模式：手机（VTS 直连，UDP）与 **VTS 服务端**（本机的 VB 当客户端连我们）。
//   ② 参数处理节点：拿配置文件（`*.hoface.json`）把原始线名**硬转**成规范参数，
//      只交出一份字典（`参数`）+ 一个判断（`有脸`）。
//   ③ 控制求解节点：**从参数反求动画输出**（零配置），输出与官方接收器 Mod 的节点同形：
//      IsTracked / BlendShapes / HeadPosition / RootPosition / BoneRotations。
//      角色不在这边 —— 挂到角色上由官方节点在图上选
//      （`Set Character Tracking BlendShapes` / `Override Character Bone Rotation Offsets` /
//      `Override Character Root Position`），所以本 Mod 一个角色引用都没有。
// （②③ 是 2026-09-25 从原来的「Ho Face 处理链」拆出来的：这样别的来源 —— 比如 VB 走 VTS 服务端模式 ——
//   可以**跳过参数处理**直接喂控制求解。）
//
// 【为什么合并成一个 Mod，而不是两个】
// Warudo 是**每个 Mod 各自编译成一个程序集**，同名类型在两个 Mod 里是**不同的 Type**，
// 互相看不见。处理链要用接收器的状态、要跑同一份求值代码，拆成两个 Mod 就得把代码
// 复制两份、还得靠端口通信。所以边界是"**Mod 的种类**"（角色/插件），不是"功能模块"。
//
// 【NodeTypes 必须列全】漏掉的节点即使编译进程序集也不会出现在节点面板里
// （见 docs/打包与脚本规范.md §3、§9 第 7 条）。
//
// ⚠️ 命名空间不要叫 ...Plugin：那样 using Warudo.Core.Plugins 里的 Plugin 基类
//    会被自己所在的命名空间遮蔽，报 CS0118。这里用 PluginMod 避开这个坑。

using HoFaceTracking.Core;
using HoFaceTracking.Nodes;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Persistence;
using Warudo.Core.Plugins;

namespace HoFaceTracking.PluginMod
{
    [PluginType(
        Id = "hollow.hofacetracking",
        Name = "Ho Face Tracking",
        Description = "面捕：接收来源发来的原始数据（VTS 手机 / 本机 VB 的 VTS 服务端模式），再按配置文件转成参数、反求成 Warudo 的追踪数据形状。",
        Version = "0.2.0",
        Author = "Hollow",
        NodeTypes = new[]
        {
            typeof(HoFaceReceiverStatusNode),
            typeof(HoFaceParameterNode),
            typeof(HoFaceSolverNode),
            typeof(HoFaceHubWriteNode),
            typeof(HoDebugLogNode),
            // 通用节点（不是面捕专用，只是暂时放在这个 mod 里）：
            //   · HoStringFloat  —— 面板手填若干 `(名字, 值)`，出来一份字典（图里没有"手填字典"这回事，
            //     所以只能做成节点；它是这类表的**产出**端）。
            //   · HoDictionaryMergeNode —— 两块"名字 → 浮点"的表合并/覆盖（把控制器里恒 1 的门控
            //     用覆盖的方式关掉：覆盖发生在参数输出之后、写进控制器之前）。
            typeof(HoStringFloatNode),
            typeof(HoDictionaryMergeNode)
        })]
    public class HoFaceTrackingPlugin : Plugin
    {
        /// <summary>
        /// 本插件的**沙箱目录**（中间层配置文件就放这儿）。
        /// 基类的 <c>PersistentData</c> 是 protected，所以只能由插件自己开个口子给节点用。
        /// </summary>
        public PluginPersistentDataManager Files
        {
            get { return PersistentData; }
        }

        public override void OneTimeSetup()
        {
            base.OneTimeSetup();

            // 顺手把沙箱接上并把目录打到日志里 —— 用户不用先翻面板就知道配置文件该放哪。
            HoFaceProfileStore.Attach(PersistentData);
            if (HoFaceProfileStore.Ready)
                Debug.Log("[Ho 面捕] 中间层配置目录：" + HoFaceProfileStore.Root
                    + "（现有 " + HoFaceProfileStore.Entries.Count + " 份配置）");
            else
                Debug.Log("[Ho 面捕] 插件的沙箱还没就绪，参数处理节点会在第一帧再试一次。");
        }

        /// <summary>
        /// 插件被卸载（含热更新换程序集）时把 UDP socket 关掉。
        ///
        /// 【为什么必须有】每次 Build + 热更新都会换掉插件程序集，这个 socket 属于**旧的**那一份；
        /// 不清掉它就一直占着 `本机端口`，新程序集"点连接"绑不上同一个端口 ——
        /// 实测表现就是**只能重启 Warudo**。基类给的就是这个钩子（`Entity.OnDestroy`），
        /// 另外接收器自己也有端口回退（`HoVtsIphoneReceiver.PortFallbacks`）兜住"没走到这里"的情况。
        /// </summary>
        protected override void OnDestroy()
        {
            HoFaceInputState.Stop();
            Debug.Log("[Ho 面捕] 插件被卸载：接收器已关（下次点 Connect 会重新绑端口）。");
            base.OnDestroy();
        }
    }
}

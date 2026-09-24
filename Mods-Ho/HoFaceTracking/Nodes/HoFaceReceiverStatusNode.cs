// HoFaceReceiverStatusNode.cs  --  VTS 接收器（面板标题 `HoFaceVTS接收器`）：开/关 + 原始值 + 状态
//
// 用法（"第一步"的验收现场）：
//   蓝图里放这个节点 -> 填手机 IP -> 点 Connect -> 把 `原始值` 接到「Ho调试日志」上看
//   （那边把整张表摊成 `线名 = 值`）。手机开着 VTS 且打开了「3rd Party PC Clients」，
//   表里的值就应该随着你的脸变。**另一种模式**见下面的 `ApiMode`（本机 VB 连我们）。
//
// 端口规则（Warudo 强制，见 docs/打包与脚本规范.md §3）：
//   [DataInput] -> public 字段；[DataOutput] -> public 方法；
//   [FlowInput] -> 返回 Continuation 的 public 方法；[FlowOutput] -> Continuation 字段；
//   [Trigger] -> 纯按钮（不占口）。
// ⚠️ 数据输入别叫 Name（撞 Node 基类成员，CS0108）。

using System.Collections.Generic;
using System.Text;
using HoFaceTracking.Core;
using UnityEngine;
using Warudo.Core.Attributes;
using Warudo.Core.Graphs;

namespace HoFaceTracking.Nodes
{
    [NodeType(
        Id = "1f4b7c2e-9a3d-4e51-b8c7-2d6a0f9e4b73",
        Title = "HoFaceVTS接收器",
        Category = "Ho Face Tracking")]
    public class HoFaceReceiverStatusNode : Node
    {
        [DataInput]
        [Label("手机 IPv4")]
        public string PhoneIp = "192.168.1.100";

        [DataInput]
        [Label("手机端口")]
        public int PhonePort = HoVtsIphoneReceiver.DefaultPhonePort;

        [DataInput]
        [Label("本机端口")]
        public int LocalPort = 49985;

        /// <summary>
        /// 勾上 = **VTS 服务端模式**：我们自己当 VTube Studio，让**本机的 VB** 连我们。
        ///
        /// 为什么要有它：VBridger 的「发送到 VTube Studio」模式里 VB 是**客户端**，它只会去连一个 VTS 服务端；
        /// 勾上这个之后用户**不用改自己的 VB 用法**（不必切成 VMC 模式），VB 的客户端列表里会多出我们这一项
        /// （靠 UDP 47779 的状态广播，见 `Core/HoVtsApiServer.cs` 的头注释）。
        /// 两种模式**互斥**：点 Connect 时按这个勾选决定起哪一个。
        /// </summary>
        [DataInput]
        [Label("VTS 服务端模式（本机 VB）")]
        public bool ApiMode;

        /// <summary>VTS 服务端模式监听的端口。**别用 8001**（那是 VTS 自己的），默认 8002。</summary>
        [DataInput]
        [Label("API 端口")]
        public int ApiPort = HoVtsApiServer.DefaultApiPort;

        /// <summary>上一次已经打进日志的状态 —— 只在**变了**的时候才写，免得每帧刷屏。</summary>
        private string loggedState;

        public override void OnUpdate()
        {
            // 接收器是轮询式的：只要这个节点在跑，就由它驱动（第一步先用节点当宿主，
            // 以后交给插件级生命周期时，这里就不用再 Poll）。
            HoFaceInputState.Poll();

            // 状态一变就写一行日志。理由和中间层节点一样：Warudo 没有界面控制台，
            // 而"到底有没有在监听、有没有收到包、有没有因为源 IP 对不上被丢"
            // 是收不到数据时唯一要看的几件事 —— 写进 Player.log 就不用截图了。
            //
            // ⚠️ 比对的字符串里**不放累计帧数**：帧数每帧都在涨，那会让这行日志变成每帧一条
            // （实测一份 Player.log 被它刷掉一万五千行）。帧数只跟着"有意义的"那条一起打出来。
            string state = "运行中=" + HoFaceInputState.Running
                + "  状态=" + HoFaceInputState.Status
                + "  本帧键=" + HoFaceInputState.LastKeyCount
                + (HoFaceInputState.ForeignPackets > 0
                    ? "  外来丢包=" + HoFaceInputState.ForeignPackets + "（最近 " + HoFaceInputState.ForeignSource + "）"
                    : "");
            if (state != loggedState)
            {
                loggedState = state;
                Debug.Log("[Ho 面捕] 接收器 " + state
                    + "  帧=" + HoFaceInputState.Frames + "/坏=" + HoFaceInputState.InvalidFrames);
            }
        }

        [FlowInput]
        public Continuation Connect()
        {
            if (ApiMode) HoFaceInputState.StartApi(ApiPort);
            else HoFaceInputState.Start(PhoneIp, PhonePort, LocalPort);

            // 把结果写进日志：热更新之后"点了连接没反应"时，这一行就是唯一的现场。
            Debug.Log("[Ho 面捕] Connect（" + (ApiMode ? "VTS 服务端模式" : "手机模式") + "）→ "
                + HoFaceInputState.Status
                + "（运行中=" + HoFaceInputState.Running + "，本机端口 " + HoFaceInputState.LocalPort + "）");
            return Exit;
        }

        [FlowInput]
        public Continuation Disconnect()
        {
            HoFaceInputState.Stop();
            Debug.Log("[Ho 面捕] Disconnect → " + HoFaceInputState.Status);
            return Exit;
        }

        // ── 输出：只有三个口（2026-09-25 从九个砍到这里）─────────────────────────
        //
        // 顺序用**显式 order**定死，而且**数据口在前、`状态` 在最后**（2026-09-25 用户要求）：
        //   · `原始值`(10)（字典）—— **列表语义，必须单独一个口**：参数处理要它，接起来最干净；
        //   · `新鲜`(20)（布尔）—— 喂参数处理的「输入新鲜」，断流回中性靠它（这是个信号，不是给人看的）；
        //   · `状态`(30)（文本）—— 其余全部合并进这一行，**摆在最下面**（它只是给人看的）。
        //
        // 砍掉的六个（`运行中` / `本帧键数` / `距上帧秒` / `累计帧坏帧` / `外来来源` / `本帧原始值`）
        // 都只是"给人看一眼"，不驱动任何节点；其中「本帧原始值」是「原始值」的文本版，纯重复 ——
        // 想看那份文本就把「原始值」接到「Ho调试日志」，那边会把整张表摊成 `线名 = 值`。
        // 状态本身也已经每变一次就往 Player.log 写一行（见 OnUpdate），节点不在图上也能查。

        /// <summary>
        /// **整帧原始值**（线名 → 原值），喂给参数处理节点。名字就是来源发来的样子，
        /// 没改名、没换算 —— 改名与量纲全在参数处理那份配置文件里。
        /// </summary>
        [DataOutput(10)]
        [Label("原始值")]
        public Dictionary<string, float> RawValues()
        {
            return HoFaceInputState.Snapshot();
        }

        /// <summary>
        /// 这一秒还有没有包。参数处理节点的"输入新鲜"接它 —— 断流时那边才能把「有脸」降下去
        /// （接收器自己**不**做断流处理：它只负责收 + 原样交出，见 HoFaceInputState 的头注释）。
        /// </summary>
        [DataOutput(20)]
        [Label("新鲜")]
        public bool Fresh()
        {
            return HoFaceInputState.IsFresh();
        }

        /// <summary>
        /// 一行状态：够定性就够了 —— 在不在收、键多少、帧/坏帧、断了多久。
        /// **外来丢包只在真丢了的时候才出现**：手机 IP 填错时包会被静默丢掉，那是唯一线索。
        /// VTS 服务端模式下换成"几个客户端 / 最近一次注入几个参数 / 谁连上来的"。
        /// </summary>
        [DataOutput(30)]
        [Label("状态")]
        public string Status()
        {
            var text = new StringBuilder();
            text.Append("来源=").Append(HoFaceInputState.ApiMode ? "VTS 服务端（本机 VB）" : "手机 VTS 直连");
            text.Append("  ·  ").Append(HoFaceInputState.Status);
            text.Append("  ·  本帧键 ").Append(HoFaceInputState.LastKeyCount);
            text.Append("  ·  帧 ").Append(HoFaceInputState.Frames).Append(" / 坏 ").Append(HoFaceInputState.InvalidFrames);
            text.Append("  ·  ").Append(AgeText());

            if (HoFaceInputState.ApiMode)
            {
                text.Append("\n客户端 ").Append(HoFaceInputState.ApiClients).Append(" 个")
                    .Append("，累计注入 ").Append(HoFaceInputState.ApiInjections).Append(" 次");
                if (!string.IsNullOrEmpty(HoFaceInputState.ApiLastClient))
                    text.Append("，最近一个来自 ").Append(HoFaceInputState.ApiLastClient);
                if (HoFaceInputState.ApiInjections == 0)
                    text.Append("\n⚠ 还没有客户端连上来 —— 确认 VB 的客户端列表里能选到「"
                        + HoVtsApiPacket.WindowTitle + "」");
            }
            else if (HoFaceInputState.ForeignPackets > 0)
            {
                text.Append("\n⚠ 丢了 ").Append(HoFaceInputState.ForeignPackets)
                    .Append(" 个包，最近来自 ").Append(HoFaceInputState.ForeignSource)
                    .Append(" —— 如果这就是你的手机，把「手机 IPv4」改成这个 IP。");
            }

            return text.ToString();
        }

        /// <summary>断流多久了（没收到过任何一帧时要说清楚，别显示一个负数的秒数）。</summary>
        private static string AgeText()
        {
            float last = HoFaceInputState.ApiMode
                ? HoFaceInputState.ApiLastFrameTime
                : HoFaceInputState.LastFrameTime;
            if (last <= 0f) return "还没收到过包";

            float age = UnityEngine.Time.realtimeSinceStartup - last;
            return "距上帧 " + age.ToString("F3") + "s";
        }

        [FlowOutput]
        public Continuation Exit;
    }
}

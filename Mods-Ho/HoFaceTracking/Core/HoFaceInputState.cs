// HoFaceInputState.cs  --  面捕输入的共享状态（本 Mod 内部唯一的"当前值"来源）
//
// 为什么要有它：接收器（线程/轮询）与节点（蓝图里读值）不是同一个对象，
// 而 Warudo 的节点只在自己那棵树里；直接互相引用会把两个类型绑死。
// 这里用一个静态状态当**唯一交接点**：
//   接收器 写 -> HoFaceInputState -> 节点 读
//
// 【名字约定：只交原样】
// 这里存的是"来源发来的名字 -> 原值"，**改名与量纲一律不做** —— 那是处理链那一步的事。
//   VTS 手机：形态键 `EyeBlinkLeft`，值 0..1（iOS 原始值）
//   头/眼姿态：`Rotation_x/y/z`、`Position_x/y/z`、`EyeLeft_x/y/z`、`EyeRight_x/y/z`
//   iFacialMocap 那条路同理是 `eyeBlink_L` / `head_0..5`（以后接）
//   **VTS 服务端模式（本机 VB）**：VB 注入时用的参数名（VB 的 VTS 兼容预设 = VTS 那套名字，
//   例如 `MouthOpen` / `EyeOpenLeft`）—— 也一样**原样**交出，别假设它跟 ARKit 对齐。
//
// 【两条来源互斥】手机（我们发请求、手机发回来）与 VTS 服务端（VB 连我们）**不能同时开**：
// 它们的帧会互相覆盖同一份状态，"这一帧是谁发的"就没法说了。见下面的 Start / StartApi。
//
// 证据：官方载荷定义 VTubeStudioRawTrackingData.cs（DenchiSoft 的 receiver 测试仓库），
// 字段名就是这些；README 原话 "Apps like VSeeFace and VBridger use this."

using System;
using System.Collections.Generic;
using System.Text;

namespace HoFaceTracking.Core
{
    /// <summary>最近一帧的原始输入，以及接收器的统计。全部在主线程读写（两个来源都是轮询的）。</summary>
    public static class HoFaceInputState
    {
        private static readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly HoVtsIphoneReceiver Receiver = new HoVtsIphoneReceiver();
        private static readonly HoVtsApiServer ApiServer = new HoVtsApiServer();

        /// <summary>
        /// 现在用的是哪条来源：false = 手机（我们主动发请求，手机发回来）；
        /// true = **VTS 服务端模式**（本机的 VB 当客户端连我们，见 <see cref="HoVtsApiServer"/>）。
        /// **两者互斥** —— 一起开会互相覆盖同一份状态，而且"这一帧是谁发的"就没法说了。
        /// </summary>
        public static bool ApiMode { get; private set; }

        /// <summary>最近一帧到达的时刻（<see cref="UnityEngine.Time.realtimeSinceStartup"/>）。</summary>
        public static float LastFrameTime { get; private set; }

        /// <summary>累计收到的有效帧数。</summary>
        public static long Frames { get; private set; }

        /// <summary>累计解析失败的帧数。</summary>
        public static long InvalidFrames { get; private set; }

        /// <summary>这一帧带了多少个键。</summary>
        public static int LastKeyCount { get; private set; }

        /// <summary>最近一帧里的 <c>FaceFound</c>（协议里有才有）。</summary>
        public static bool FaceFound { get; private set; }

        public static bool Running
        {
            get { return ApiMode ? ApiServer.Running : Receiver.Running; }
        }

        /// <summary>这次**真正绑上**的本机端口（端口被占时会自动往后挪，见各接收器的说明）。</summary>
        public static int LocalPort
        {
            get { return ApiMode ? ApiServer.Port : Receiver.LocalPort; }
        }

        /// <summary>人看的短状态（节点上直接显示）。</summary>
        public static string Status
        {
            get { return ApiMode ? ApiServer.Status : Receiver.Status; }
        }

        /// <summary>被"源 IP 对不上"丢掉的包的来源（没丢过就是 null）。**只有手机模式有这个概念。**</summary>
        public static string ForeignSource
        {
            get { return ApiMode ? null : Receiver.ForeignSource; }
        }

        /// <summary>被丢掉的包累计数。**只有手机模式有这个概念。**</summary>
        public static long ForeignPackets
        {
            get { return ApiMode ? 0 : Receiver.ForeignPackets; }
        }

        /// <summary>VTS 服务端模式下：现在连着我们几个客户端（0 = VB 还没连上来）。</summary>
        public static int ApiClients
        {
            get { return ApiMode ? ApiServer.ClientCount : 0; }
        }

        /// <summary>VTS 服务端模式下：最近一次注入带了多少个参数（0 = 还没收到过）。</summary>
        public static int ApiLastParameterCount
        {
            get { return ApiMode ? ApiServer.LastParameterCount : 0; }
        }

        /// <summary>最近一次成功注入的时刻（<c>0</c> = 还没收到过）。</summary>
        public static float ApiLastFrameTime
        {
            get { return ApiMode ? ApiServer.LastSeenFrameTime : 0f; }
        }

        /// <summary>VTS 服务端模式下最近握手上的客户端地址（排查"VB 到底连没连上"）。</summary>
        public static string ApiLastClient
        {
            get { return ApiMode ? ApiServer.LastClient : null; }
        }

        /// <summary>VTS 服务端模式下客户端的个数 / 累计注入次数 / 解析失败次数。</summary>
        public static long ApiInjections
        {
            get { return ApiMode ? ApiServer.Injections : 0; }
        }

        /// <summary>手机模式：往手机发请求、等它发回来。**会顺手停掉 VTS 服务端模式。**</summary>
        public static void Start(string phoneIp, int phonePort, int localPort)
        {
            ApiServer.Stop();
            ApiMode = false;
            Receiver.Start(phoneIp, phonePort, localPort);
        }

        /// <summary>VTS 服务端模式：本机的 VB 会连我们。**会顺手停掉手机模式。**</summary>
        public static void StartApi(int port)
        {
            Receiver.Stop();
            ApiMode = true;
            ApiServer.Start(port);
        }

        public static void Stop()
        {
            Receiver.Stop();
            ApiServer.Stop();
            Values.Clear();
            LastFrameTime = 0f;
            LastKeyCount = 0;
        }

        /// <summary>每帧调一次：续约/广播 + 把能收的包全收掉（最新一帧生效）。两个来源互斥，没跑的那个是空操作。</summary>
        public static void Poll()
        {
            Receiver.Poll(Apply);
            ApiServer.Poll(Apply);
        }

        /// <summary>接收器解析出一帧后回调这里。</summary>
        private static void Apply(Dictionary<string, float> values, bool faceFound)
        {
            Values.Clear();
            foreach (var pair in values) Values[pair.Key] = pair.Value;
            LastFrameTime = UnityEngine.Time.realtimeSinceStartup;
            LastKeyCount = values.Count;
            FaceFound = faceFound;
            Frames++;
        }

        public static void NoteInvalid()
        {
            InvalidFrames++;
        }

        /// <summary>取一个线名的原值；没收到过就是 false（**不要**自己当 0 用）。</summary>
        public static bool TryGet(string key, out float value)
        {
            if (key != null && Values.TryGetValue(key, out value)) return true;
            value = 0f;
            return false;
        }

        /// <summary>这一秒算不算"新鲜"（手机 60 FPS，超过 1 秒没包就是断流）。</summary>
        public static bool IsFresh(float seconds = 1f)
        {
            return LastFrameTime > 0f && UnityEngine.Time.realtimeSinceStartup - LastFrameTime <= seconds;
        }

        /// <summary>
        /// 把这一帧摊成"线名 → 原值"的**副本**，给接收器节点当数据端口用。
        ///
        /// 为什么给副本：端口的值会被下游节点一直拿着，如果把它接到内部那个字典上，
        /// 下一帧我们改掉它，下游看到的东西也跟着变 —— 那不是"一帧的值"，是个活引用。
        /// 52~200 个键拷一份，每帧一次，代价可以忽略。
        /// </summary>
        public static Dictionary<string, float> Snapshot()
        {
            var copy = new Dictionary<string, float>(Values.Count, StringComparer.Ordinal);
            foreach (var pair in Values) copy[pair.Key] = pair.Value;
            return copy;
        }

        // 这里原本还有个 `Dump(max)`（把这一帧摊成一行文本，给旧调试节点的「本帧原始值」口用）。
        // 2026-09-25 那个口并进「状态」之后它就没人调了 —— 而"摊开整张表"这件事现在是
        // 「Ho调试日志」节点在做（`Describe`，而且是不截断的完整版）。删掉免得留一条假的活口。
    }
}

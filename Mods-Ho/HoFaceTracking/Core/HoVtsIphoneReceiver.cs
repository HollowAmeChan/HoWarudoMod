// HoVtsIphoneReceiver.cs  --  VTubeStudio 手机版的接收器（UDP + JSON 协议）
//
// 【为什么和官方接收器 mod 不一样】
// 官方那套（ARKitFaceTrackingTemplate / MediaPipe 派生）拿到数据后直接把它**当成面捕追踪数据**交给 Warudo；
// 我们这里只做**接收 + 原样交出**（线名 -> 原值），改名/量纲/混合树那套放到处理链那一步
// （那是我们自己的中间层配置，Unity 侧和 Warudo 侧共用同一份）。
//
// 【协议：来自官方开发者文档，不是逆向】
//   1) 手机 VTS 打开「3rd Party PC Clients」，它在 iPhone 上开 UDP 监听（默认 21412）；
//   2) 我们往 `手机:21412` 发 JSON：
//        {"messageType":"iOSTrackingDataRequest","time":5,"sentBy":"HoFaceTracking","ports":[本机端口]}
//      `time` 只允许 0.5–10 秒 → **必须每秒续一次**；
//   3) 手机按帧回 JSON：Timestamp / FaceFound / Hotkey / Rotation / Position /
//      BlendShapes[{k,v}] / EyeLeft / EyeRight。
//   来源：https://github.com/DenchiSoft/VTubeStudioBlendshapeUDPReceiverTest （README 原话：
//   "Apps like VSeeFace and VBridger use this."；载荷定义 VTubeStudioRawTrackingData.cs）
//
// ⚠️ **这套协议不是"手机主动推流"**：手机不接受目标地址，它把数据发回**请求包的源 IP**，
//    端口用请求里 `ports` 指定的。所以手机上（除了那个开关）没有任何要填的东西；
//    我们这边 `手机 IPv4` / `手机端口` 是用来发请求的，`本机端口` 才是数据回来的落点。
//
// 【包的解析在哪儿】
// 搬去 `HoVtsPacket`（纯静态、不碰 socket），这样它能脱离 Unity 离线测 ——
// 它曾经用 `[Serializable]` DTO + `JsonUtility.FromJson`，下场是
// **12 个头眼分量全在、52 个形态键全丢**（本机实测"本帧键 15"）。原因见 HoJson.cs。**别再改回去。**
//
// 【为什么不用线程】
// Warudo 的实体（插件/资源/节点）每帧都有 OnUpdate，我们只要把 UDP socket 设成非阻塞、
// 每帧把能收的包收掉就够了。少一个线程 = 少一类崩法，也不用碰 UMod 的安全审查边界
// （禁用 System.IO / 反射 / P-Invoke；System.Net.Sockets 官方 VMC 插件就在用，是允许的）。
// 以后真要线程化，再照 `Warudo-Mod-Tool-0.14.4.8` 里现成的写法加。

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace HoFaceTracking.Core
{
    public sealed class HoVtsIphoneReceiver
    {
        /// <summary>手机上那个 UDP 监听口的默认值（官方文档：`21412` 或 App 上显示的那个）。</summary>
        public const int DefaultPhonePort = 21412;

        /// <summary>每次买多少秒的数据。真正的定义在 <c>HoVtsPacket</c>，这里只是转发，免得两处各写一个数。</summary>
        public const float RequestSeconds = HoVtsPacket.RequestSeconds;

        /// <summary>续约间隔。必须远小于 <see cref="RequestSeconds"/>，否则中间会断流。</summary>
        private const float RenewInterval = 1f;

        private UdpClient _socket;
        private IPAddress _phone;
        private int _phonePort = DefaultPhonePort;
        private int _localPort;
        private float _lastRequest;
        private string _status = "未启动";

        /// <summary>
        /// 端口被占时最多往后挪几次（49985 → 49986 → …）。
        ///
        /// 【为什么需要它】每次 Build + Warudo 热更新，插件程序集被换掉，而**旧的那个 `UdpClient`
        /// 不一定被回收** —— 它还把 `本机端口` 占着。于是"重新点连接"永远绑不上同一个端口，
        /// 表现就是**只能重启 Warudo**（用户实测）。VTS 协议里手机是往**我们请求里列出的端口**回的，
        /// 所以本机换个端口对手机完全透明 —— 挪一格就能重新连上。
        /// </summary>
        private const int PortFallbacks = 10;

        public bool Running
        {
            get { return _socket != null; }
        }

        /// <summary>这次**真正绑上**的本机端口（可能是自动往后挪过的那个）。</summary>
        public int LocalPort
        {
            get { return _localPort; }
        }

        public string Status
        {
            get { return _status; }
        }

        /// <summary>
        /// 最近一个**来源 IP 对不上**的包长什么样（形如 `192.168.1.9:52000`），以及累计有多少个被丢。
        ///
        /// 为什么专门记这个：`手机 IPv4` 填错时，包会被下面那条源 IP 过滤**静默丢掉** ——
        /// 现象和"手机没发""防火墙挡了""不在同一网段"**完全一样**，最难查。
        /// 记下外来来源之后，一眼就能分辨"是它但 IP 填错了"还是"根本没包来"。
        /// </summary>
        public string ForeignSource { get; private set; }

        /// <summary>被源 IP 过滤丢掉的包累计数。</summary>
        public long ForeignPackets { get; private set; }

        /// <summary>最近一次解析失败的原因（解析本身在 <see cref="HoVtsPacket"/> 里）。</summary>
        public string LastParseError { get; private set; }

        public void Start(string phoneIp, int phonePort, int localPort)
        {
            Stop();

            IPAddress parsed;
            if (!IPAddress.TryParse(phoneIp, out parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                _status = "手机 IP 不合法：" + phoneIp;
                return;
            }

            if (phonePort < 1 || phonePort > 65535) phonePort = DefaultPhonePort;
            if (localPort < 1024 || localPort > 65535)
            {
                _status = "本机端口要在 1024–65535";
                return;
            }

            _phone = parsed;
            _phonePort = phonePort;

            // 先试用户填的那个端口；绑不上（多半是热更新之后旧 socket 还占着）就往后挪一格再试。
            // 手机是往"我们请求里列出的端口"回的，所以本机换端口对它透明。
            Exception firstFailure = null;
            for (int attempt = 0; attempt < PortFallbacks; attempt++)
            {
                int candidate = localPort + attempt;
                if (candidate > 65535) break;

                try
                {
                    var socket = new UdpClient(AddressFamily.InterNetwork);
                    socket.Client.ExclusiveAddressUse = true;
                    socket.Client.Bind(new IPEndPoint(IPAddress.Any, candidate));
                    socket.Client.Blocking = false;

                    _socket = socket;
                    _localPort = candidate;
                    _lastRequest = 0f;
                    SendRequest();
                    _status = attempt == 0
                        ? "监听 " + candidate + " ← " + phoneIp + ":" + phonePort
                        : "监听 " + candidate + " ← " + phoneIp + ":" + phonePort
                          + "（" + localPort + " 被占，已自动换端口）";
                    return;
                }
                catch (Exception e)
                {
                    if (firstFailure == null) firstFailure = e;
                }
            }

            // 一个都没绑上：把第一次的异常原文留下来（UMod 会拒 exception.GetType().Name，所以只写固定文本 + 本体）。
            _status = "启动失败（本机 UDP " + localPort + " 起 " + PortFallbacks + " 个端口都绑不上）";
            if (firstFailure != null) Debug.LogException(firstFailure);
        }

        public void Stop()
        {
            if (_socket != null)
            {
                try { _socket.Close(); }
                catch (Exception e) { Debug.LogException(e); }
                _socket = null;
            }

            _status = "已停止";
        }

        /// <summary>每帧调一次：到点就续约，然后把已经到达的包全部收掉（只有最后一帧会生效）。</summary>
        public void Poll(Action<Dictionary<string, float>, bool> onFrame)
        {
            if (_socket == null) return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastRequest >= RenewInterval) SendRequest();

            // 非阻塞：把这一帧已经排队的包全收掉，避免积压。
            int guard = 0;
            while (_socket != null && guard++ < 32)
            {
                if (_socket.Available <= 0) break;

                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try
                {
                    data = _socket.Receive(ref endpoint);
                }
                catch (SocketException)
                {
                    break;   // WouldBlock 之类：这一帧就到此为止
                }

                if (!endpoint.Address.Equals(_phone))
                {
                    // 只认手机那个 IP。但**要记下来**：填错时唯一的线索就是这个。
                    ForeignSource = endpoint.Address + ":" + endpoint.Port;
                    ForeignPackets++;
                    continue;
                }

                if (data.Length > 65536) continue;

                Dictionary<string, float> values;
                bool faceFound;
                string parseError;
                if (!HoVtsPacket.TryParse(Encoding.UTF8.GetString(data), out values, out faceFound, out parseError))
                {
                    LastParseError = parseError;
                    HoFaceInputState.NoteInvalid();
                    continue;
                }

                onFrame(values, faceFound);
            }
        }

        /// <summary>要数据（每次只买几秒，所以要每秒续）。包的原文由 <see cref="HoVtsPacket.BuildRequest"/> 给。</summary>
        private void SendRequest()
        {
            if (_socket == null) return;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(HoVtsPacket.BuildRequest(_localPort));
                _socket.Send(bytes, bytes.Length, new IPEndPoint(_phone, _phonePort));
                _lastRequest = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                _status = "续约发送失败（手机可达吗？）";
                Debug.LogException(e);
            }
        }
    }
}

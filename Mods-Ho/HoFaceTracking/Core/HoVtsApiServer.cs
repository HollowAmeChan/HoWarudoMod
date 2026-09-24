// HoVtsApiServer.cs  --  VTS 公开 API 的**服务端**那一侧（给本机的 VB 用）
//
// 【它解决什么】VBridger 的「发送到 VTube Studio」模式里，**VB 是客户端**：它去连一个 VTS 服务端，
// 用 `InjectParameterDataRequest` 把参数值注进去（其中还带一个 `faceFound`）。
// 我们想收这份数据，就得**扮演那个服务端** —— 这样用户不用改自己的 VB 用法（不必切成 VMC 模式）。
//
// 【和手机那条路的区别（方向是反的，别混）】
//   手机（HoVtsIphoneReceiver）：**我们发** `iOSTrackingDataRequest`，手机把数据发回我们的 UDP 端口。
//   本文件（VTS API 服务端）：**VB 主动连我们**（TCP + WebSocket），我们只负责应答与收包。
//
// 【协议出处：官方文档，不是逆向】
//   · WebSocket 服务端默认 `ws://localhost:8001`（`vts-api.md:124`）；端口可换，用户改哪儿我们跟哪儿。
//   · UDP `47779` 上每 2 秒广播一次 API 状态（`VTubeStudioAPIStateBroadcast`，**unsolicited**，
//     不是请求应答）—— 客户端（VB）就是靠听这个把"可用的 VTube Studio 客户端"列出来的（`vts-api.md:183-204`）。
//   · 连上之后的插件握手：`AuthenticationTokenRequest` → 拿 token；`AuthenticationRequest` → 认证（`vts-api.md:206-300`）。
//   · 数据注入：`InjectParameterDataRequest` = `{faceFound, mode, parameterValues:[{id,value,weight?}]}`
//     → 我们回 `InjectParameterDataResponse`（`vts-api.md:1369-1412`）。
//     ⚠️ 真 VTS 对"不存在的参数"会报错；**我们照收不误**（我们不是 VTS，没有"参数表"这个概念）。
//
// 【⚠️ SHA-1 是自己写的，不是用 System.Security.Cryptography】
//   WebSocket 握手必须算 `Sec-WebSocket-Accept = base64(sha1(key + GUID))`，而 UMod 的安全校验
//   **禁掉 `System.Security.Cryptography`**（本机实测：拿 Trivial.CodeSecurity 的默认规则集跑一个
//   只用 `SHA1.Create()` 的探针 → Illegal namespace = 1；同条件的控制组是 0）。
//   所以 `Sha1` / `Base64` 都手写（Base64 也要手写吗？不 —— `System.Convert.ToBase64String` 是 `System`
//   命名空间，控制组探针用过它、通过。只有 crypto 那一个命名空间不能碰）。
//
// 【为什么不用线程】跟手机接收器同一个理由（见它的文件头）：Warudo 每帧都有 OnUpdate，
//   把 socket 全设成非阻塞、每帧 Poll 一次就够 —— 少一个线程就少一类崩法，也不用碰安全审查边界。
//   代价是"每帧最多搬多少字节"有上限，对 60 FPS 的小包完全够（每次注入约 1–3 KB）。
//
// 【名字约定：只交原样】
//   收下来的是 VB 注入时用的**参数名**（VB 的 VTS 兼容预设用的就是 VTS 那套名字，例如 `MouthOpen` /
//   `EyeOpenLeft`），**不是** ARKit 那 52 个名字 —— 改名/量纲照旧留给处理链的输入行。
//   别假设"它一定跟 ARKit 对齐"：这一层只保证"名字 → 值"原样交出。

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace HoFaceTracking.Core
{
    public sealed class HoVtsApiServer
    {
        /// <summary>默认端口。**不要用 8001** —— 那是 VTS 自己的；我们要能被单独选中，所以默认挪一位。</summary>
        public const int DefaultApiPort = 8002;

        /// <summary>VTS 广播 API 状态用的 UDP 口（官方文档写的，客户端就听这个）。</summary>
        private const int DiscoveryPort = 47779;

        /// <summary>广播间隔。VTS 是每 2 秒一次，我们照抄。</summary>
        private const float BroadcastInterval = 2f;

        /// <summary>端口被占时最多往后挪几次（同手机接收器：热更新后旧 socket 可能还占着）。</summary>
        private const int PortFallbacks = 10;

        /// <summary>同时最多接几个客户端（正常只有 VB 一个；多留几个方便排查）。</summary>
        private const int MaxClients = 4;

        /// <summary>握手请求头最长多少字节；超了就断开（防呆，不是防攻击）。</summary>
        private const int MaxHandshakeBytes = 8192;

        /// <summary>单条 WebSocket 消息最长多少字节。一次注入约 1–3 KB，给足余量。</summary>
        private const int MaxMessageBytes = 262144;

        /// <summary>多久没有数据就把客户端踢掉（秒）。防止热更新/异常退出留下半死的连接。</summary>
        private const float ClientIdleTimeout = 15f;

        /// <summary>握手用的固定 GUID（RFC 6455 写死的那个常量）。</summary>
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>我们发给客户端的 token。固定值：VB 那边可能把 token 记下来，跨次运行要能对上。</summary>
        private const string AuthToken = "ho-face-tracking-local-0001";

        private sealed class Client
        {
            public Socket Socket;
            public byte[] Buffer = new byte[16384];
            public int Length;
            public bool Upgraded;
            public float LastSeen;
            public readonly StringBuilder Message = new StringBuilder();   // 分片消息攒在这里
        }

        private TcpListener _listener;
        private UdpClient _broadcast;
        private int _port;
        private float _lastBroadcast;
        private readonly List<Client> _clients = new List<Client>();
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private string _status = "未启动";

        /// <summary>最近一次注入用了多少个参数（0 = 还没收到过）。</summary>
        public int LastParameterCount { get; private set; }

        /// <summary>累计收到的注入次数。</summary>
        public long Injections { get; private set; }

        /// <summary>累计解析失败的注入次数。</summary>
        public long InvalidMessages { get; private set; }

        /// <summary>最近一次解析失败的原因。</summary>
        public string LastParseError { get; private set; }

        /// <summary>最近握手上的客户端地址（形如 `127.0.0.1:52344`），没见过就是 null。</summary>
        public string LastClient { get; private set; }

        /// <summary>最近一次收到注入的时刻（<c>0</c> = 还没收到过）。排查"VB 到底还在不在发"。</summary>
        public float LastSeenFrameTime { get; private set; }

        public bool Running
        {
            get { return _listener != null; }
        }

        /// <summary>这次**真正绑上**的端口（被占时会自动往后挪）。</summary>
        public int Port
        {
            get { return _port; }
        }

        public string Status
        {
            get { return _status; }
        }

        /// <summary>现在连着的客户端数（含还没握完手的）。</summary>
        public int ClientCount
        {
            get { return _clients.Count; }
        }

        public void Start(int port)
        {
            Stop();

            if (port < 1024 || port > 65535) port = DefaultApiPort;

            // 端口被占（多半是热更新后旧 socket 还占着）就往后挪：广播会把**真实端口**告诉 VB，
            // 所以挪了它也能找到我们。
            Exception firstFailure = null;
            for (int attempt = 0; attempt < PortFallbacks; attempt++)
            {
                int candidate = port + attempt;
                if (candidate > 65535) break;

                try
                {
                    var listener = new TcpListener(IPAddress.Any, candidate);
                    listener.Start();
                    listener.Server.Blocking = false;      // 每帧 Poll，绝不在 Accept 上卡住

                    _listener = listener;
                    _port = candidate;

                    try
                    {
                        // 广播 socket 只用来发；绑不绑到固定端口都行，绑了就少一类"端口被占"的意外。
                        _broadcast = new UdpClient(AddressFamily.InterNetwork);
                        _broadcast.EnableBroadcast = true;
                    }
                    catch (Exception e)
                    {
                        // 广播起不来不影响收数据：VB 也可能是手动填的地址。留着状态文字说明。
                        Debug.LogException(e);
                        _broadcast = null;
                    }

                    _lastBroadcast = 0f;   // 立刻广播一次，让 VB 尽快看到我们
                    Poll(null);

                    _status = attempt == 0
                        ? "VTS 服务端监听 " + candidate + "（广播 " + DiscoveryPort + "）"
                        : "VTS 服务端监听 " + candidate + "（" + port + " 被占，已自动换端口）";
                    return;
                }
                catch (Exception e)
                {
                    if (firstFailure == null) firstFailure = e;
                }
            }

            _status = "启动失败（TCP " + port + " 起 " + PortFallbacks + " 个端口都绑不上）";
            if (firstFailure != null) Debug.LogException(firstFailure);
        }

        public void Stop()
        {
            for (int i = 0; i < _clients.Count; i++) CloseClient(_clients[i]);
            _clients.Clear();

            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch (Exception e) { Debug.LogException(e); }
                _listener = null;
            }

            if (_broadcast != null)
            {
                try { _broadcast.Close(); }
                catch (Exception e) { Debug.LogException(e); }
                _broadcast = null;
            }

            _port = 0;
            LastParameterCount = 0;
            _status = "已停止";
        }

        /// <summary>
        /// 每帧调一次：广播状态、收新连接、把每个客户端已经到达的字节处理掉。
        /// <paramref name="onFrame"/> 在**主线程**上被调用（注入一帧就调一次），与手机接收器同一个签名。
        /// </summary>
        public void Poll(Action<Dictionary<string, float>, bool> onFrame)
        {
            if (_listener == null) return;

            float now = Time.realtimeSinceStartup;
            BroadcastIfDue(now);
            AcceptClients(now);

            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                Client client = _clients[i];
                if (!PumpClient(client, now, onFrame)) continue;          // 读完了，还好
                CloseClient(client);
                _clients.RemoveAt(i);
            }
        }

        // ── 广播：让 VB 的"VTube Studio 客户端列表"里出现我们 ──────────────────────

        private void BroadcastIfDue(float now)
        {
            if (_broadcast == null) return;
            if (now - _lastBroadcast < BroadcastInterval) return;
            _lastBroadcast = now;

            string json = HoVtsApiPacket.BuildBroadcast(_port, _instanceId, NowMilliseconds());

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            try
            {
                // 照抄 VTS：往广播地址发。本机监听 0.0.0.0:47779 的客户端收得到。
                _broadcast.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
            }
            catch (Exception e)
            {
                _status = "广播发不出去（端口 " + DiscoveryPort + "）";
                Debug.LogException(e);
            }
        }

        private static long NowMilliseconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        // ── 连接与收发 ──────────────────────────────────────────────────────────

        private void AcceptClients(float now)
        {
            int guard = 0;
            while (_clients.Count < MaxClients && guard++ < 8)
            {
                if (!_listener.Pending()) return;

                Socket socket;
                try { socket = _listener.AcceptSocket(); }
                catch (SocketException) { return; }     // WouldBlock：这帧就到此为止
                catch (Exception e) { Debug.LogException(e); return; }

                try
                {
                    socket.Blocking = false;
                    socket.NoDelay = true;
                }
                catch (Exception e) { Debug.LogException(e); }

                _clients.Add(new Client { Socket = socket, LastSeen = now });
                LastClient = Describe(socket);
                _status = "VTS 服务端 " + _port + "：客户端已连接（" + LastClient + "）";
            }
        }

        /// <summary>把某个客户端这一帧能读的字节全读掉。返回 false 表示"这条连接结束了，请关掉它"。</summary>
        private bool PumpClient(Client client, float now, Action<Dictionary<string, float>, bool> onFrame)
        {
            if (now - client.LastSeen > ClientIdleTimeout) return false;

            int guard = 0;
            while (guard++ < 32)
            {
                int room = client.Buffer.Length - client.Length;
                if (room <= 0) return false;                     // 缓冲塞满了还没解析出一条完整消息：不正常

                int read;
                try
                {
                    read = client.Socket.Receive(client.Buffer, client.Length, room, SocketFlags.None);
                }
                catch (SocketException)
                {
                    return true;                                  // WouldBlock：这帧没更多数据了
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return false;
                }

                if (read <= 0) return false;                      // 对端关了
                client.Length += read;
                client.LastSeen = now;

                if (!client.Upgraded)
                {
                    if (!TryUpgrade(client)) return true;         // 头还没收全，等下一帧
                }

                if (!DrainFrames(client, onFrame)) return false;
            }

            return true;
        }

        /// <summary>握手：把 HTTP 升级请求换成一帧 `101 Switching Protocols`。头没收全就返回 true 等下一帧。</summary>
        private bool TryUpgrade(Client client)
        {
            int end = FindHeaderEnd(client.Buffer, client.Length);
            if (end < 0)
            {
                if (client.Length >= MaxHandshakeBytes) return false;   // 头太大了
                return false;
            }

            string request = Encoding.UTF8.GetString(client.Buffer, 0, end);
            string key = FindHeader(request, "Sec-WebSocket-Key");
            if (string.IsNullOrEmpty(key))
            {
                // 不是 WebSocket 升级（可能是别的程序来敲这个端口）：礼貌回 400 然后断开。
                SendRaw(client, "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n");
                return false;
            }

            string accept = Convert.ToBase64String(Sha1(Encoding.UTF8.GetBytes(key.Trim() + WebSocketGuid)));
            SendRaw(client, "HTTP/1.1 101 Switching Protocols\r\n"
                + "Upgrade: websocket\r\n"
                + "Connection: Upgrade\r\n"
                + "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");

            // 头后面可能已经跟着第一帧了：把它挪到缓冲开头。
            int consumed = end + 4;
            int rest = client.Length - consumed;
            if (rest > 0) Array.Copy(client.Buffer, consumed, client.Buffer, 0, rest);
            client.Length = rest;
            client.Upgraded = true;

            _status = "VTS 服务端 " + _port + "：已握手（" + LastClient + "）";
            return true;
        }

        /// <summary>把缓冲里能解析的 WebSocket 帧全处理掉。返回 false = 这条连接该关了。</summary>
        private bool DrainFrames(Client client, Action<Dictionary<string, float>, bool> onFrame)
        {
            while (true)
            {
                int consumed;
                bool close;
                if (!TryReadFrame(client, onFrame, out consumed, out close)) return true;   // 帧没收全
                if (close) return false;

                int rest = client.Length - consumed;
                if (rest > 0) Array.Copy(client.Buffer, consumed, client.Buffer, 0, rest);
                client.Length = rest;
            }
        }

        /// <summary>
        /// 解析一帧。返回 false = 数据不够（等下一帧再试）。
        /// 只实现我们真用得到的那些：文本 / 分片 / ping / pong / close。
        /// </summary>
        private bool TryReadFrame(Client client, Action<Dictionary<string, float>, bool> onFrame,
            out int consumed, out bool close)
        {
            consumed = 0;
            close = false;

            byte[] buffer = client.Buffer;
            int length = client.Length;
            if (length < 2) return false;

            int opcode = buffer[0] & 0x0F;
            bool fin = (buffer[0] & 0x80) != 0;
            bool masked = (buffer[1] & 0x80) != 0;
            long payloadLength = buffer[1] & 0x7F;
            int offset = 2;

            if (payloadLength == 126)
            {
                if (length < offset + 2) return false;
                payloadLength = (buffer[offset] << 8) | buffer[offset + 1];
                offset += 2;
            }
            else if (payloadLength == 127)
            {
                if (length < offset + 8) return false;
                payloadLength = 0;
                for (int i = 0; i < 8; i++) payloadLength = (payloadLength << 8) | buffer[offset + i];
                offset += 8;
            }

            if (payloadLength < 0 || payloadLength > MaxMessageBytes) { close = true; return true; }
            if (masked) offset += 4;                        // 客户端发来的**必须**带掩码，我们按 RFC 解
            if (length < offset + payloadLength) return false;

            string text = null;
            if (opcode == 0x1 || opcode == 0x0)
            {
                var payload = new byte[payloadLength];
                Array.Copy(buffer, offset, payload, 0, (int)payloadLength);
                if (masked) Unmask(payload, buffer, offset - 4);

                if (opcode == 0x1) client.Message.Length = 0;             // 新消息，清掉上一轮分片
                if (client.Message.Length + payload.Length > MaxMessageBytes) { close = true; return true; }
                client.Message.Append(Encoding.UTF8.GetString(payload));

                if (fin)
                {
                    text = client.Message.ToString();
                    client.Message.Length = 0;
                }
            }
            else if (opcode == 0x8)                          // close
            {
                close = true;
            }
            else if (opcode == 0x9)                          // ping → pong
            {
                var payload = new byte[payloadLength];
                Array.Copy(buffer, offset, payload, 0, (int)payloadLength);
                if (masked) Unmask(payload, buffer, offset - 4);
                SendFrame(client, 0xA, payload);
            }
            // 0xA（pong）不需要处理

            consumed = offset + (int)payloadLength;

            if (text != null && onFrame != null) HandleMessage(client, text, onFrame);
            return true;
        }

        // ── 消息处理：握手 + 注入 ────────────────────────────────────────────────

        private void HandleMessage(Client client, string text, Action<Dictionary<string, float>, bool> onFrame)
        {
            string messageType, requestId;
            Dictionary<string, float> values;
            bool faceFound;
            string error;
            if (!HoVtsApiPacket.TryParse(text, out messageType, out requestId, out values, out faceFound, out error))
            {
                InvalidMessages++;
                LastParseError = error;
                HoFaceInputState.NoteInvalid();
                return;
            }

            if (values != null && values.Count > 0)
            {
                LastParameterCount = values.Count;
                Injections++;
                LastSeenFrameTime = Time.realtimeSinceStartup;
                onFrame(values, faceFound);
            }

            string response = HoVtsApiPacket.BuildResponse(messageType, requestId, AuthToken, _port, _instanceId);
            if (response != null) SendFrame(client, 0x1, Encoding.UTF8.GetBytes(response));
        }

        private void SendRaw(Client client, string text)
        {
            try { client.Socket.Send(Encoding.UTF8.GetBytes(text)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void SendFrame(Client client, int opcode, byte[] payload)
        {
            int head = payload.Length < 126 ? 2 : (payload.Length <= 65535 ? 4 : 10);
            var frame = new byte[head + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            if (payload.Length < 126)
            {
                frame[1] = (byte)payload.Length;
            }
            else if (payload.Length <= 65535)
            {
                frame[1] = 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
            }
            else
            {
                frame[1] = 127;
                long n = payload.Length;
                for (int i = 0; i < 8; i++) frame[2 + i] = (byte)(n >> (56 - i * 8));
            }

            Array.Copy(payload, 0, frame, head, payload.Length);

            try { client.Socket.Send(frame); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void CloseClient(Client client)
        {
            if (client == null || client.Socket == null) return;
            try { client.Socket.Close(); }
            catch (Exception e) { Debug.LogException(e); }
            client.Socket = null;
        }

        // ── 小工具 ──────────────────────────────────────────────────────────────

        private static void Unmask(byte[] payload, byte[] buffer, int maskOffset)
        {
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(payload[i] ^ buffer[maskOffset + (i & 3)]);
        }

        /// <summary>找 `\r\n\r\n`；返回头结束的位置（不含那 4 个字节），找不到返回 -1。</summary>
        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i + 3 < length; i++)
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
                    return i;
            return -1;
        }

        /// <summary>在 HTTP 头里按名字找值（忽略大小写）。</summary>
        private static string FindHeader(string request, string name)
        {
            string[] lines = request.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (!line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        private static string Describe(Socket socket)
        {
            try
            {
                var endpoint = socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Address + ":" + endpoint.Port : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        // ── SHA-1（手写，理由见文件头）────────────────────────────────────────────

        /// <summary>
        /// 标准 SHA-1。**只为了 WebSocket 握手**（RFC 6455 规定 `Sec-WebSocket-Accept` 必须是
        /// `base64(sha1(key + GUID))`），而 `System.Security.Cryptography` 被 UMod 安全校验禁掉。
        ///
        /// 自检向量（RFC 6455 §1.3 的例子）：key = `dGhlIHNhbXBsZSBub25jZQ==`
        /// → 应得 `s3pPLMBiTxaQ9kYGzzhZRbK+xOo=`。
        /// </summary>
        internal static byte[] Sha1(byte[] data)
        {
            uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;

            // 填充：0x80 + 若干 0 + 64 位大端比特长度
            int padded = ((data.Length + 8) / 64 + 1) * 64;
            var block = new byte[padded];
            Array.Copy(data, block, data.Length);
            block[data.Length] = 0x80;
            ulong bits = (ulong)data.Length * 8UL;
            for (int i = 0; i < 8; i++) block[padded - 1 - i] = (byte)(bits >> (i * 8));

            var w = new uint[80];
            for (int offset = 0; offset < padded; offset += 64)
            {
                for (int i = 0; i < 16; i++)
                    w[i] = (uint)((block[offset + i * 4] << 24) | (block[offset + i * 4 + 1] << 16)
                                | (block[offset + i * 4 + 2] << 8) | block[offset + i * 4 + 3]);
                for (int i = 16; i < 80; i++)
                    w[i] = RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

                uint a = h0, b = h1, c = h2, d = h3, e = h4;
                for (int i = 0; i < 80; i++)
                {
                    uint f, k;
                    if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                    else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                    else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                    else { f = b ^ c ^ d; k = 0xCA62C1D6; }

                    uint temp = RotateLeft(a, 5) + f + e + k + w[i];
                    e = d; d = c; c = RotateLeft(b, 30); b = a; a = temp;
                }

                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
            }

            var hash = new byte[20];
            WriteBigEndian(hash, 0, h0);
            WriteBigEndian(hash, 4, h1);
            WriteBigEndian(hash, 8, h2);
            WriteBigEndian(hash, 12, h3);
            WriteBigEndian(hash, 16, h4);
            return hash;
        }

        private static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}

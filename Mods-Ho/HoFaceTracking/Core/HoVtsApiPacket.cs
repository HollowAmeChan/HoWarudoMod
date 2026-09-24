// HoVtsApiPacket.cs  --  VTS 公开 API 的报文：解析 + 应答（**纯静态、不碰 socket**）
//
// 跟 `HoVtsPacket`（手机那条 UDP 协议）分开写的理由一样：解析要能脱离 Unity/网络离线测，
// 而且要能把"这一帧是什么消息"讲清楚。socket 在 `HoVtsApiServer` 里。
//
// 【协议出处】官方文档 https://github.com/DenchiSoft/VTubeStudio（本地副本
// `.research/vts-creator-workflow/vts-api.md`）：
//   · 每条消息都是 `{apiName, apiVersion, timestamp, requestID, messageType, data}`，
//     应答要把 `requestID` **原样带回来**（客户端靠它配对）。
//   · `InjectParameterDataRequest.data = {faceFound, mode, parameterValues:[{id, value, weight?}]}`（`:1369-1396`）
//   · 认证两步：`AuthenticationTokenRequest` → `AuthenticationTokenResponse`（带 token）；
//     `AuthenticationRequest`（带 token）→ `AuthenticationResponse`（`:206-300`）。
//
// 【热路径不许用闭包】`HoJsonReader.ReadObject/ReadArray` 会为每一项分配闭包，
// 那份文件里明确写了"逐帧的热路径别用它们"。VB 是每帧注入一次，所以这里的解析是
// **用原语手写的循环**（`ReadString` / `ReadFloat` / `SkipValue`）。
//
// 【我们不是 VTS】所以有两条刻意的宽容：
//   ① 参数名一律照收（真 VTS 会对"不存在的参数"报错，我们没有参数表这个概念）；
//   ② `faceFound` 缺省当 `true`（数据在流 = 有脸），有就按它给的。
//   证书 token 是**固定值**：VB 可能把 token 记下来，跨次运行要能对上（见服务端里的常量）。

using System;
using System.Collections.Generic;
using System.Text;

namespace HoFaceTracking.Core
{
    public static class HoVtsApiPacket
    {
        /// <summary>API 名字与版本：客户端会校验这两个字段（照抄 VTS 广播里的值）。</summary>
        public const string ApiName = "VTubeStudioPublicAPI";
        public const string ApiVersion = "1.0";

        /// <summary>窗口标题：会出现在 VB 的客户端列表里，认人用。</summary>
        public const string WindowTitle = "Ho Face Tracking (Warudo)";

        /// <summary>
        /// 解析一条客户端消息。
        /// <paramref name="values"/> 只有注入请求才有内容（其它消息为 null）。
        /// </summary>
        public static bool TryParse(string json, out string messageType, out string requestId,
            out Dictionary<string, float> values, out bool faceFound, out string error)
        {
            messageType = null;
            requestId = null;
            values = null;
            faceFound = true;          // 缺省当有脸：数据在流就说明脸在
            error = null;

            try
            {
                var reader = new HoJsonReader(json);
                reader.Expect('{');
                if (reader.TryConsume('}')) { error = "空对象"; return false; }

                while (true)
                {
                    string key = reader.ReadString();
                    reader.Expect(':');

                    if (key == "messageType") messageType = reader.ReadString();
                    else if (key == "requestID") requestId = reader.ReadString();
                    else if (key == "apiName" || key == "apiVersion" || key == "timestamp") reader.SkipValue();
                    else if (key == "data")
                    {
                        var bag = new Dictionary<string, float>(StringComparer.Ordinal);
                        if (ReadData(reader, bag, ref faceFound)) values = bag;
                    }
                    else reader.SkipValue();

                    if (reader.TryConsume(',')) continue;
                    reader.Expect('}');
                    break;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }

            if (string.IsNullOrEmpty(messageType)) { error = "没有 messageType"; return false; }
            return true;
        }

        /// <summary>读 `data` 这层。返回 true = 里面有参数值（也就是一条注入）。</summary>
        private static bool ReadData(HoJsonReader reader, Dictionary<string, float> bag, ref bool faceFound)
        {
            bool sawParameters = false;

            reader.Expect('{');
            if (reader.TryConsume('}')) return false;

            while (true)
            {
                string key = reader.ReadString();
                reader.Expect(':');

                if (key == "faceFound") faceFound = reader.ReadBool();
                else if (key == "parameterValues")
                {
                    sawParameters = true;
                    ReadParameterValues(reader, bag);
                }
                else reader.SkipValue();

                if (reader.TryConsume(',')) continue;
                reader.Expect('}');
                return sawParameters;
            }
        }

        /// <summary>读 `parameterValues: [{id, value, weight?}, …]`。`weight` 我们不做混合，只记 `value`。</summary>
        private static void ReadParameterValues(HoJsonReader reader, Dictionary<string, float> bag)
        {
            reader.Expect('[');
            if (reader.TryConsume(']')) return;

            while (true)
            {
                reader.Expect('{');
                if (!reader.TryConsume('}'))
                {
                    string id = null;
                    float value = 0f;

                    while (true)
                    {
                        string key = reader.ReadString();
                        reader.Expect(':');

                        if (key == "id") id = reader.ReadString();
                        else if (key == "value") value = reader.ReadFloat();
                        else reader.SkipValue();

                        if (reader.TryConsume(',')) continue;
                        reader.Expect('}');
                        break;
                    }

                    // 同名参数后写的生效（跟 profile 里"最后一行覆盖"一个规矩）
                    if (!string.IsNullOrEmpty(id)) bag[id] = value;
                }

                if (reader.TryConsume(',')) continue;
                reader.Expect(']');
                return;
            }
        }

        /// <summary>
        /// 给一条客户端消息拼应答。**返回 null = 这条消息不需要回**（我们不认识、也不想装作认识）。
        /// </summary>
        public static string BuildResponse(string messageType, string requestId, string token, int port, string instanceId)
        {
            switch (messageType)
            {
                case "AuthenticationTokenRequest":
                    return Envelope("AuthenticationTokenResponse", requestId,
                        "{\"authenticationToken\":\"" + Escape(token) + "\"}");

                case "AuthenticationRequest":
                    // 我们不是 VTS，没有"用户点允许"那一步：一律放行。
                    return Envelope("AuthenticationResponse", requestId, "{\"authenticated\":true}");

                case "InjectParameterDataRequest":
                    // 真 VTS 会为不认识的参数报错；我们照收，所以永远成功、data 为空。
                    return Envelope("InjectParameterDataResponse", requestId, "{}");

                case "APIStateRequest":
                    return Envelope("APIStateResponse", requestId,
                        "{\"active\":true,\"port\":" + port
                        + ",\"instanceID\":\"" + Escape(instanceId) + "\""
                        + ",\"windowTitle\":\"" + Escape(WindowTitle) + "\""
                        + ",\"currentSessionAuthenticated\":true}");

                default:
                    return Envelope("APIError", requestId,
                        "{\"errorID\":200,\"message\":\"Ho Face Tracking 不处理这种消息：" + Escape(messageType) + "\"}");
            }
        }

        /// <summary>拼 API 状态广播（UDP 47779 上每 2 秒一份；客户端靠它列出"可用的 VTS 客户端"）。</summary>
        public static string BuildBroadcast(int port, string instanceId, long timestampMilliseconds)
        {
            return "{\"apiName\":\"" + ApiName + "\",\"apiVersion\":\"" + ApiVersion + "\""
                + ",\"timestamp\":" + timestampMilliseconds
                + ",\"messageType\":\"VTubeStudioAPIStateBroadcast\""
                + ",\"requestID\":\"VTubeStudioAPIStateBroadcast\""
                + ",\"data\":{\"active\":true,\"port\":" + port
                + ",\"instanceID\":\"" + Escape(instanceId) + "\""
                + ",\"windowTitle\":\"" + Escape(WindowTitle) + "\"}}";
        }

        private static string Envelope(string messageType, string requestId, string data)
        {
            return "{\"apiName\":\"" + ApiName + "\",\"apiVersion\":\"" + ApiVersion + "\""
                + ",\"timestamp\":" + NowMilliseconds()
                + ",\"requestID\":\"" + Escape(requestId) + "\""
                + ",\"messageType\":\"" + messageType + "\""
                + ",\"data\":" + data + "}";
        }

        private static long NowMilliseconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>最小 JSON 转义（我们只往外发文件路径/参数名这类东西，够用就行）。</summary>
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var builder = new StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\\') builder.Append('\\').Append(c);
                else if (c < ' ') builder.Append(' ');
                else builder.Append(c);
            }
            return builder.ToString();
        }
    }
}

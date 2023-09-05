﻿using BepInEx.Logging;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SIT.Coop.Core.Matchmaker;
using SIT.Core.Configuration;
using SIT.Core.Coop;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SIT.Core.Core
{
    public class AkiBackendCommunication : IDisposable
    {
        public const int DEFAULT_TIMEOUT_MS = 5000;
        public const int DEFAULT_TIMEOUT_LONG_MS = 9999;

        private string m_Session;

        public string Session
        {
            get
            {
                return m_Session;
            }
            set { m_Session = value; }
        }



        private string m_RemoteEndPoint;

        public string RemoteEndPoint
        {
            get
            {
                if (string.IsNullOrEmpty(m_RemoteEndPoint))
                    m_RemoteEndPoint = PatchConstants.GetBackendUrl();

                return m_RemoteEndPoint;

            }
            set { m_RemoteEndPoint = value; }
        }

        //public bool isUnity;
        private Dictionary<string, string> m_RequestHeaders { get; set; }

        private static AkiBackendCommunication m_Instance { get; set; }
        public static AkiBackendCommunication Instance
        {
            get
            {
                if (m_Instance == null || m_Instance.Session == null || m_Instance.RemoteEndPoint == null)
                    m_Instance = new AkiBackendCommunication();

                return m_Instance;
            }
        }

        public HttpClient HttpClient { get; set; }

        protected ManualLogSource Logger;

        static WebSocketSharp.WebSocket WebSocket { get; set; }


        protected AkiBackendCommunication(ManualLogSource logger = null)
        {
            // disable SSL encryption
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            if (logger != null)
                Logger = logger;
            else
                Logger = BepInEx.Logging.Logger.CreateLogSource("Request");

            if (string.IsNullOrEmpty(RemoteEndPoint))
                RemoteEndPoint = PatchConstants.GetBackendUrl();

            GetHeaders();
            //CreateWebSocket();
            ConnectToAkiBackend();
            PeriodicallySendPing();
            PeriodicallySendPooledData();

            HttpClient = new HttpClient();
            foreach (var item in GetHeaders())
            {
                HttpClient.DefaultRequestHeaders.Add(item.Key, item.Value);
            }
            HttpClient.MaxResponseContentBufferSize = long.MaxValue;
            HttpClient.Timeout = new TimeSpan(0, 0, 0, 0, 1000);
        }

        private void ConnectToAkiBackend()
        {
            PooledJsonToPostToUrl.Add(new KeyValuePair<string, string>("/coop/connect", "{}"));
        }

        public void WebSocketCreate(Profile profile)
        {
            Logger.LogDebug("WebSocketCreate");
            if (WebSocket != null && WebSocket.ReadyState != WebSocketSharp.WebSocketState.Closed)
            {
                Logger.LogDebug("WebSocketCreate:WebSocket already exits");
                return;
            }

            Logger.LogDebug("Request Instance is connecting to WebSocket");

            var webSocketPort = PluginConfigSettings.Instance.CoopSettings.SITWebSocketPort;
            var wsUrl = $"{PatchConstants.GetREALWSURL()}:{webSocketPort}/{profile.ProfileId}?";
            Logger.LogDebug(webSocketPort);
            Logger.LogDebug(PatchConstants.GetREALWSURL());
            Logger.LogDebug(wsUrl);

            WebSocket = new WebSocketSharp.WebSocket(wsUrl);
            WebSocket.OnError += WebSocket_OnError;
            WebSocket.OnMessage += WebSocket_OnMessage;
            WebSocket.Connect();
            WebSocket.Send("CONNECTED FROM SIT COOP");
            // Continously Ping from SIT.Core (Keep Alive)
            _ = Task.Run(async () =>
            {

                while (WebSocket != null)
                {
                    WebSocket.Send((new { RandomPing = 0 }).ToJson());
                    await Task.Delay(1000);
                }

            });
        }

        public void WebSocketClose()
        {
            if(WebSocket != null)
            {
                Logger.LogDebug("WebSocketClose");
                WebSocket.OnError -= WebSocket_OnError;
                WebSocket.OnMessage -= WebSocket_OnMessage;
                WebSocket.Close(WebSocketSharp.CloseStatusCode.Normal);
                WebSocket = null;
            }
        }

        public async void PostDownWebSocketImmediately(Dictionary<string, object> packet)
        {
            await Task.Run(() =>
            {
                if(WebSocket != null)
                    WebSocket.Send(packet.SITToJson());
            });
        }

        public async void PostDownWebSocketImmediately(string packet)
        {
            await Task.Run(() =>
            {
                if(WebSocket != null)
                    WebSocket.Send(packet);
            });
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            Logger.LogError($"WebSocket_OnError: {e.Message} {Environment.NewLine}");
            WebSocket_OnError();
        }

        private void WebSocket_OnError()
        {
            Logger.LogError($"Your PC has failed to connect and send data to the WebSocket with the port {PluginConfigSettings.Instance.CoopSettings.SITWebSocketPort} on the Server {PatchConstants.GetBackendUrl()}! Application will now close.");
            if (
                CoopGameComponent.TryGetCoopGameComponent(out var coopGameComponent)
                && coopGameComponent.LocalGameInstance != null
                )
            {
                coopGameComponent.LocalGameInstance.Stop(Singleton<GameWorld>.Instance.MainPlayer.ProfileId, ExitStatus.Survived, null);
            }
            else
                Application.Quit();
        }

        private void WebSocket_OnMessage(object sender, WebSocketSharp.MessageEventArgs e)
        {
            if (e.Data == null)
                return;

            if (string.IsNullOrEmpty(e.Data))
                return;

            Dictionary<string, object> packet = null;
            if (e.Data != null)
            {
                if (e.Data.IndexOf("{") > 0)
                    return;

                if (!e.Data.EndsWith("}"))
                    return;
            }

            if (DEBUGPACKETS)
            {
                Logger.LogInfo(e.Data);

                try
                {
                    packet = JsonConvert.DeserializeObject<Dictionary<string, object>>(e.Data);
                }
                catch (Exception ex)
                {
                    Logger.LogError(e.Data);
                    Logger.LogError(ex);
                }
            }
            else
            {
                using (var streamReader = new StreamReader(new MemoryStream(e.RawData)))
                {
                    using (var reader = new JsonTextReader(streamReader))
                    {
                        var serializer = new JsonSerializer();
                        packet = serializer.Deserialize<Dictionary<string, object>>(reader);
                    }
                }
            }


            if (!CoopGameComponent.TryGetCoopGameComponent(out var coopGameComponent))
                return;


            // -------------------------------------------------------
            // WARNING: This can cause Unity crash (different threads) but would be ideal >> FAST << sync logic! If it worked!
            // coopGameComponent.ReadFromServerLastActionsParseData(packet);
            // -------------------------------------------------------

            // If this is a pong packet, resolve and create a smooth ping
            if (packet.ContainsKey("pong"))
            {
                var pongRaw = long.Parse(packet["pong"].ToString());
                var dtPong = new DateTime(pongRaw);
                var serverPing = (int)(DateTime.UtcNow - dtPong).TotalMilliseconds;
                if (coopGameComponent.ServerPingSmooth.Count > 60)
                    coopGameComponent.ServerPingSmooth.TryDequeue(out _);
                coopGameComponent.ServerPingSmooth.Enqueue(serverPing);
                coopGameComponent.ServerPing = coopGameComponent.ServerPingSmooth.Count > 0 ? (int)Math.Round(coopGameComponent.ServerPingSmooth.Average()) : 1;
                return;
            }

            if (packet.ContainsKey("HostPing"))
            {
                var dtHP = new DateTime(long.Parse(packet["HostPing"].ToString()));
                var timeSpanOfHostToMe = DateTime.Now - dtHP;
                Instance.HostPing = timeSpanOfHostToMe.Milliseconds;
            }

            // Syncronize RaidTimer
            if (packet.ContainsKey("RaidTimer"))
            {
                var tsRaidTimer = new TimeSpan(long.Parse(packet["RaidTimer"].ToString()));

                //if(coopGameComponent.LocalGameInstance is CoopGame)
                //{
                //    coopGameComponent.LocalGameInstance.GameTimer.ChangeSessionTime(tsRaidTimer);
                //}
                return;
            }

            // If this is an endSession packet, end the session for the clients
            if (packet.ContainsKey("endSession") && MatchmakerAcceptPatches.IsClient)
            {
                Logger.LogDebug("Received EndSession from Server. Ending Game.");
                if (coopGameComponent.LocalGameInstance == null)
                    return;

                coopGameComponent.ServerHasStopped = true;
                //coopGameComponent.LocalGameInstance.Stop(Singleton<GameWorld>.Instance.MainPlayer.ProfileId, ExitStatus.Runner, "", 0);
                return;
            }

            // If this is a SIT serialization packet
            if (packet.ContainsKey("data") && packet.ContainsKey("m"))
            {
                //Logger.LogInfo(" =============WebSocket_OnMessage========= ");
                //Logger.LogInfo(" ==================SIT Packet============= ");
                //Logger.LogInfo(packet.ToJson());
                //Logger.LogInfo(" ========================================= ");
                //if (!packet.ContainsKey("accountId"))
                if (!packet.ContainsKey("profileId"))
                {
                    packet.Add("profileId", packet["data"].ToString().Split(',')[0]);
                }
            }

            // -------------------------------------------------------
            // Check the packet doesn't already exist in Coop Game Component Action Packets
            //if (
            //    // Quick Check -> This would likely not work because Contains uses Equals which doesn't work very well with Dictionary
            //    coopGameComponent.ActionPackets.Contains(packet)
            //    // Timestamp Check -> This would only work on the Dictionary (not the SIT serialization) packet
            //    || coopGameComponent.ActionPackets.Any(x => packet.ContainsKey("t") && x.ContainsKey("t") && x["t"].ToString() == packet["t"].ToString())
            //    )
            //    return;

            //Logger.LogInfo(packet.ToJson());
            // -------------------------------------------------------
            // Add to the Coop Game Component Action Packets
            coopGameComponent.ActionPackets.TryAdd(packet);

        }

        public static AkiBackendCommunication GetRequestInstance(bool createInstance = false, ManualLogSource logger = null)
        {
            if (createInstance)
            {
                return new AkiBackendCommunication(logger);
            }

            return Instance;
        }

        public static bool DEBUGPACKETS { get; } = false;

        public BlockingCollection<string> PooledJsonToPost { get; } = new();
        public BlockingCollection<KeyValuePair<string, Dictionary<string, object>>> PooledDictionariesToPost { get; } = new();
        public BlockingCollection<List<Dictionary<string, object>>> PooledDictionaryCollectionToPost { get; } = new();

        public BlockingCollection<KeyValuePair<string, string>> PooledJsonToPostToUrl { get; } = new();

        public void SendDataToPool(string url, string serializedData)
        {
            PooledJsonToPostToUrl.Add(new(url, serializedData));
        }

        public void SendDataToPool(string serializedData)
        {
            //// ------------------------------------------------------------------------------------
            //// DEBUG: This is a sanity check to see if we are flooding packets.
            //if (DEBUGPACKETS)
            //{
            //    if (PooledJsonToPost.Count() >= 11)
            //    {
            //        Logger.LogError("Holy moly. There is too much data being OUTPUT from this client!");
            //        while (PooledJsonToPost.Any())
            //        {
            //            if (PooledJsonToPost.TryTake(out var item, -1))
            //                Logger.LogError($"{item}");
            //        }
            //        //Application.Quit();
            //    }
            //}

            //// Stop resending static "player states"
            //if (PooledJsonToPost.Any(x => x == serializedData))
            //    return;

            //PooledJsonToPost.Add(serializedData);

            if(WebSocket != null && WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
                WebSocket.SendAsync(serializedData, (bool b) => { });
        }
        public void SendDataToPool(string url, Dictionary<string, object> data)
        {
            PooledDictionariesToPost.Add(new(url, data));
        }

        public void SendListDataToPool(string url, List<Dictionary<string, object>> data)
        {
            PooledDictionaryCollectionToPost.Add(data);
        }

        public int HostPing { get; private set; } = 1;
        public int PostPing { get; private set; } = 1;
        public ConcurrentQueue<int> PostPingSmooth { get; } = new();

        private Task PeriodicallySendPooledDataTask;

        private void PeriodicallySendPooledData()
        {
            //PatchConstants.Logger.LogDebug($"PeriodicallySendPooledData()");

            PeriodicallySendPooledDataTask = Task.Run(async () =>
            {
                int awaitPeriod = 1;
                //GCHelpers.EnableGC();
                //GCHelpers.ClearGarbage();
                //PatchConstants.Logger.LogDebug($"PeriodicallySendPooledData():In Async Task");

                //while (m_Instance != null)
                Stopwatch swPing = new();

                while (true)
                {
                    if (WebSocket == null)
                    {
                        await Task.Delay(awaitPeriod);
                        continue;
                    }

                    swPing.Restart();
                    await Task.Delay(awaitPeriod);
                    //await Task.Delay(100);
                    while (PooledDictionariesToPost.Any())
                    {
                        await Task.Delay(awaitPeriod);

                        KeyValuePair<string, Dictionary<string, object>> d;
                        if (PooledDictionariesToPost.TryTake(out d))
                        {

                            var url = d.Key;
                            //var json = JsonConvert.SerializeObject(d.Value);
                            var json = d.Value.ToJson();
                            if (WebSocket != null)
                            {
                                if (WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
                                {
                                    WebSocket.Send(json);
                                }
                                else
                                {
                                    WebSocket_OnError();
                                }
                            }
                            GC.Collect();
                        }
                    }

                    if (PooledDictionaryCollectionToPost.TryTake(out var d2))
                    {
                        var json = JsonConvert.SerializeObject(d2);
                        if (WebSocket != null)
                        {
                            if (WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
                            {
                                WebSocket.Send(json);
                            }
                            else
                            {
                                PatchConstants.Logger.LogError($"WS:Periodic Send:PooledDictionaryCollectionToPost:Failed!");
                            }
                        }
                        json = null;
                    }

                    //do
                    //{

                    //    if (PooledJsonToPost.TryTake(out var json))
                    //    {
                    //        if (WebSocket != null)
                    //        {
                    //            if (WebSocket.ReadyState == WebSocketSharp.WebSocketState.Open)
                    //            {
                    //                PostDownWebSocketImmediately(json);
                    //            }
                    //        }
                    //        json = null;
                    //    }

                    //    await Task.Delay(awaitPeriod);

                    //} while (PooledJsonToPost.Any());


                    while (PooledJsonToPostToUrl.Any())
                    {
                        await Task.Delay(awaitPeriod);

                        if (PooledJsonToPostToUrl.TryTake(out var kvp))
                        {
                            _ = await PostJsonAsync(kvp.Key, kvp.Value, timeout: 1000, debug: true);
                        }
                    }

                    if (PostPingSmooth.Any() && PostPingSmooth.Count > 30)
                        PostPingSmooth.TryDequeue(out _);

                    PostPingSmooth.Enqueue((int)swPing.ElapsedMilliseconds - awaitPeriod);
                    PostPing = (int)Math.Round(PostPingSmooth.Average());

                }
            });
        }

        private Task PeriodicallySendPingTask { get; set; }

        private void PeriodicallySendPing()
        {
            PeriodicallySendPingTask = Task.Run(async () =>
            {
                int awaitPeriod = 2000;
                while (true)
                {
                    await Task.Delay(awaitPeriod);

                    if (WebSocket == null)
                        continue;

                    if (WebSocket.ReadyState != WebSocketSharp.WebSocketState.Open)
                        continue;

                    if (!CoopGameComponent.TryGetCoopGameComponent(out var coopGameComponent))
                        continue;

                    // PatchConstants.Logger.LogDebug($"WS:Ping Send");

                    var packet = new 
                    {
                        m = "Ping",
                        t = DateTime.UtcNow.Ticks.ToString("G") ,
                        profileId = coopGameComponent.OwnPlayer.ProfileId ,
                        serverId = coopGameComponent.ServerId 
                    };

                    WebSocket.Send(Encoding.UTF8.GetBytes(packet.ToJson()));
                    packet = null;
                }
            });
        }

        private Dictionary<string, string> GetHeaders()
        {
            if (m_RequestHeaders != null && m_RequestHeaders.Count > 0)
                return m_RequestHeaders;

            string[] args = Environment.GetCommandLineArgs();

            foreach (string arg in args)
            {
                if (arg.Contains("-token="))
                {
                    Session = arg.Replace("-token=", string.Empty);
                    m_RequestHeaders = new Dictionary<string, string>()
                        {
                            { "Cookie", $"PHPSESSID={Session}" },
                            { "SessionId", Session }
                        };
                    break;
                }
            }
            return m_RequestHeaders;
        }

        /// <summary>
        /// Send request to the server and get Stream of data back
        /// </summary>
        /// <param name="url">String url endpoint example: /start</param>
        /// <param name="method">POST or GET</param>
        /// <param name="data">string json data</param>
        /// <param name="compress">Should use compression gzip?</param>
        /// <returns>Stream or null</returns>
        private MemoryStream SendAndReceive(string url, string method = "GET", string data = null, bool compress = true, int timeout = 9999, bool debug = false)
        {
            // Force to DEBUG mode if not Compressing.
            debug = debug || !compress;

            HttpClient.Timeout = TimeSpan.FromMilliseconds(timeout);


            method = method.ToUpper();

            var fullUri = url;
            if (!Uri.IsWellFormedUriString(fullUri, UriKind.Absolute))
                fullUri = RemoteEndPoint + fullUri;

            if (method == "GET")
            {
                var ms = new MemoryStream();
                var stream = HttpClient.GetStreamAsync(fullUri);
                stream.Result.CopyTo(ms);
                return ms;
            }
            else if (method == "POST" || method == "PUT")
            {
                var uri = new Uri(fullUri);
                return SendAndReceivePostOld(uri, method, data, compress, timeout, debug);
            }

            throw new ArgumentException($"Unknown method {method}");
        }

        /// <summary>
        /// TODO: Replace this with a HTTPClient Post command. 
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="method"></param>
        /// <param name="data"></param>
        /// <param name="compress"></param>
        /// <param name="timeout"></param>
        /// <param name="debug"></param>
        /// <returns></returns>
        MemoryStream SendAndReceivePostOld(Uri uri, string method = "GET", string data = null, bool compress = true, int timeout = 9999, bool debug = false)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.ServerCertificateValidationCallback = delegate { return true; };

            foreach (var item in GetHeaders())
            {
                request.Headers.Add(item.Key, item.Value);
            }

            if (!debug && method == "POST")
            {
                request.Headers.Add("Accept-Encoding", "deflate");
            }

            request.Method = method;
            request.Timeout = timeout;

            if (!string.IsNullOrEmpty(data))
            {
                if (debug && method == "POST")
                {
                    compress = false;
                    request.Headers.Add("debug", "1");
                }

                // set request body
                var inputDataBytes = Encoding.UTF8.GetBytes(data);
                byte[] bytes = compress ? Zlib.Compress(inputDataBytes, ZlibCompression.Fastest) : inputDataBytes;
                data = null;
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                if (compress)
                    request.Headers.Add("content-encoding", "deflate");

                try
                {
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception e)
                {
                    PatchConstants.Logger.LogError(e);
                }
                finally
                {
                    bytes = null;
                    inputDataBytes = null;
                }
            }

            // get response stream
            //WebResponse response = null;
            var ms = new MemoryStream();
            try
            {
                using (var response = request.GetResponse())
                {
                    using (var responseStream = response.GetResponseStream())
                        responseStream.CopyTo(ms);
                }
            }
            catch (Exception e)
            {
                PatchConstants.Logger.LogError(e);
            }
            finally
            {
                request = null;
                uri = null;
            }
            return ms;
        }

        public byte[] GetData(string url, bool hasHost = false)
        {
            using (var dataStream = SendAndReceive(url, "GET"))
                return dataStream.ToArray();
        }

        public void PutJson(string url, string data, bool compress = true, int timeout = 9999, bool debug = false)
        {
            using (Stream stream = SendAndReceive(url, "PUT", data, compress, timeout, debug)) { }
        }

        public string GetJson(string url, bool compress = true, int timeout = 9999)
        {
            using (MemoryStream stream = SendAndReceive(url, "GET", null, compress, timeout))
            {
                if (stream == null)
                    return "";
                var bytes = stream.ToArray();
                var dec = Zlib.Decompress(bytes);
                var result = Encoding.UTF8.GetString(dec);
                dec = null;
                bytes = null;
                return result;
            }
        }

        public string PostJson(string url, string data, bool compress = true, int timeout = 9999, bool debug = false)
        {
            using (MemoryStream stream = SendAndReceive(url, "POST", data, compress, timeout, debug))
            {
                if (stream == null)
                    return "";
                var bytes = stream.ToArray();
                byte[] resultBytes;
                if (compress)
                {
                    if (Zlib.IsCompressed(bytes))
                        resultBytes = Zlib.Decompress(bytes);
                    else
                        resultBytes = bytes;
                }
                else
                {
                    resultBytes = bytes;
                }
                var result = Encoding.UTF8.GetString(resultBytes);
                bytes = null;
                return result;
            }
        }

        public async Task<string> PostJsonAsync(string url, string data, bool compress = true, int timeout = DEFAULT_TIMEOUT_MS, bool debug = false, int retryAttempts = 5)
        {
            int attempt = 0;
            while (attempt++ < retryAttempts)
            {
                try
                {
                    return await Task.FromResult(PostJson(url, data, compress, timeout, debug));
                }
                catch (Exception ex)
                {
                    PatchConstants.Logger.LogError(ex);
                }
            }
            throw new Exception($"Unable to communicate with Aki Server {url} to post json data: {data}");
        }

        public void PostJsonAndForgetAsync(string url, string data, bool compress = true, int timeout = DEFAULT_TIMEOUT_LONG_MS, bool debug = false)
        {
            SendDataToPool(url, data);
            //try
            //{
            //    _ = Task.Run(() => PostJson(url, data, compress, timeout, debug));
            //}
            //catch (Exception ex)
            //{
            //    PatchConstants.Logger.LogError(ex);
            //}
        }


        /// <summary>
        /// Retrieves data asyncronously and parses to the desired type
        /// </summary>
        /// <typeparam name="T">Desired type to Deserialize to</typeparam>
        /// <param name="url">URL to call</param>
        /// <param name="data">data to send</param>
        /// <returns></returns>
        public async Task<T> PostJsonAsync<T>(string url, string data, int timeout = DEFAULT_TIMEOUT_MS, int retryAttempts = 5, bool debug = true)
        {
            int attempt = 0;
            while (attempt++ < retryAttempts)
            {
                try
                {
                    var json = await PostJsonAsync(url, data, compress: false, timeout: timeout, debug);
                    return await Task.Run(() => JsonConvert.DeserializeObject<T>(json));
                }
                catch (Exception ex)
                {
                    PatchConstants.Logger.LogError(ex);
                }
            }
            throw new Exception($"Unable to communicate with Aki Server {url} to post json data: {data}");
        }

        public void Dispose()
        {
            Session = null;
            RemoteEndPoint = null;
        }
    }
}

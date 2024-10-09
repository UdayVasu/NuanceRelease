using CommonLoggerModule;
using Docpanel.NuanceReport.Proxy.Invoker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuanceClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Docpanel.NuanceReport.Proxy
{
    public class HttpListenerHost
    {
        static HttpListener listener = null;

        public static bool Host()
        {
            string listenerUrl = string.Empty;

            try
            {
                listener = new HttpListener();
                listenerUrl = CommonExtensions.ReadConfigValue("ListenerHostUrl");

                listener.Prefixes.Add($"{listenerUrl}");
                bool alrdyListening = listener.IsListening;
                if (alrdyListening)
                {
                    listener.Abort();
                    return true;
                }

                listener.Start();
                bool isListening = listener.IsListening;
                if (isListening)
                {
                    Log4Net.LogEvent(LogLevel.Information, "HttpHost", "StartHost", $"Http listener url:{listenerUrl} | IsListening:{isListening}");
                    listener.BeginGetContext(ListenerCallback, listener);
                }

            }
            catch (Exception e)
            {
                Log4Net.LogEvent(LogLevel.Error, "HttpHost", "StartHost", $"Http listener url:{listenerUrl} | Exception:{e}");
                return true;
            }
            return false;
        }

        public static void StopHost()
        {
            try
            {
                listener.Stop();
                listener.Abort();
            }
            catch
            { }
        }


        static void ListenerCallback(IAsyncResult result)
        {
            HttpListenerResponse response = null;
            HttpListener listener = null;
            HttpListenerWebSocketContext webSocketContext = null;
            string inputJson = string.Empty;
            HttpListenerContext context = null;
            try
            {
                listener = (HttpListener)result.AsyncState;

                context = listener.EndGetContext(result);
                HttpListenerRequest request = context.Request;
                response = context.Response;


                if (context.Request.IsWebSocketRequest)
                {
                    webSocketContext = Task.Run(async () => await context.AcceptWebSocketAsync(subProtocol: null)).Result;
                    NotifyStatus.webSocket = webSocketContext.WebSocket;
                    byte[] receiveBuffer = new byte[1024];
                    var receiveResult = Task.Run(async () => await webSocketContext.WebSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None)).Result;
                    inputJson = Encoding.ASCII.GetString(receiveBuffer, 0, receiveResult.Count);

                }
                else
                {

                    if (request.Headers["Origin"] != null)
                        response.AddHeader("Access-Control-Allow-Origin", request.Headers["Origin"]);

                    response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Access-Control-Allow-Headers, Authorization, X-Requested-With, session-id, session-user-id, session-user-role");
                    response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    response.AddHeader("Access-Control-Max-Age", "1728000");
                    response.AddHeader("Access-Control-Allow-Credentials", "true");

                    if (request.HttpMethod == "OPTIONS" || request.HttpMethod == HttpMethod.Put.ToString())
                    {
                        listener.BeginGetContext(ListenerCallback, listener);
                        response.Close();
                        return;
                    }

                    using (var reader = new StreamReader(request.InputStream))
                    {
                        inputJson = reader.ReadToEnd();
                    }
                }

                Log4Net.LogEvent(LogLevel.Information, "HttpHost", "ListenerCallback", $"Request json data :{inputJson}");

                if (string.IsNullOrEmpty(inputJson))
                {
                    response.StatusCode = 500;
                    response.StatusDescription = "Request json data is null/empty";
                    listener.BeginGetContext(ListenerCallback, listener);
                    if (!context.Request.IsWebSocketRequest)
                        response.Close();
                    return;
                }

                var caseDetails = JsonConvert.DeserializeObject<CaseData>(inputJson);

                NotifyStatus.docpanelUrl = caseDetails.DocpanelBaseUrl;
                NotifyStatus.statusmsg = string.Empty;
                NotifyStatus.closeargs = string.Empty;
               
                var closeReport = caseDetails.CloseReport;

                if (closeReport)
                {
                    Task.Run(async () => await InvokeClient.CloseNuanceReport(caseDetails));
                    response.StatusCode = 200;
                    response.StatusDescription = "Report closed successfully";
                    listener.BeginGetContext(ListenerCallback, listener);
                    if (!context.Request.IsWebSocketRequest)
                        response.Close();
                }
                else
                {
                    Task.Run(async () => await InvokeClient.InvokeNuanceClient(caseDetails));

                    response.StatusCode = 200;
                    response.StatusDescription = "Report opened successfully";
                    listener.BeginGetContext(ListenerCallback, listener);
                    if (!context.Request.IsWebSocketRequest)
                        response.Close();
                }
            }
            catch (Exception e)
            {
                Log4Net.LogEvent(LogLevel.Error, "HttpHost", "ListenerCallback", $"Exception:{e}");
                response.StatusCode = 500;
                response.StatusDescription = "Internal Server Error";
                listener.BeginGetContext(ListenerCallback, listener);
                if (!context.Request.IsWebSocketRequest)
                    response.Close();
                // listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);
            }
        }
    }
}


using Newtonsoft.Json;
using System;
using System.Linq;
using UnityEngine;

namespace SIT.Tarkov.Core.Web
{
    public class BackendConnection
    {
        public string BackendUrl { get; }
        public string Version { get; }

        public string PHPSESSID { get; private set; }
        public string WebsocketUrl { get; }

        public BackendConnection(string backendUrl, string websocketUrl, string version)
        {
            BackendUrl = backendUrl;
            Version = version;
            WebsocketUrl = websocketUrl;
        }

        private static BackendConnection CreateBackendConnectionFromEnvVars()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args == null)
                return null;

            var beUrl = string.Empty;
            var wsUrl = string.Empty;
            var php = string.Empty;

            /*foreach (string arg in args)
            {
                Debug.Log("Backend arg: " + arg);
            }*/
            /*{ "BackendUrl":"http://midge-robust-herring.ngrok-free.app","WebsocketUrl":"http://354a-50-98-199-244.ngrok-free.app","Version":"live"}*/

            // Get backend url
            foreach (string arg in args)
            {
                if (arg.Contains("BackendUrl") && arg.Contains("WebsocketUrl"))
                {
                    string json = arg.Replace("-config=", string.Empty);
                    var item = JsonConvert.DeserializeObject<BackendConnection>(json);
                    beUrl = item.BackendUrl;
                    wsUrl = item.WebsocketUrl;
                }
                if (arg.Contains("-token="))
                {
                    php = arg.Replace("-token=", string.Empty);
                }
            }

            if (!string.IsNullOrEmpty(php) && !string.IsNullOrEmpty(beUrl) && !string.IsNullOrEmpty(wsUrl))
            {
                return new BackendConnection(beUrl, wsUrl, php);
            }
            return null;
        }

        public static BackendConnection GetBackendConnection()
        {
            return CreateBackendConnectionFromEnvVars();
        }
    }
}

using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Networking
{
    public class NetworkCommandLine : MonoBehaviour
    {
        private NetworkManager _netManager;
        void Start()
        {
            _netManager = GetComponentInParent<NetworkManager>();
            
            if(Application.isEditor) return;
        
            var args = GetCommandLineArgs();

            if (!args.TryGetValue("-mode", out string mode)) return;
        
            StartUsingMode(mode);
        }

        private void StartUsingMode(string mode)
        {
            switch (mode)
            {
                case "server":
                    _netManager.StartServer();
                    break;
                case "client":
                    _netManager.StartClient();
                    break;
                case "host":
                    _netManager.StartHost();
                    break;
            }
        }

        private Dictionary<string, string> GetCommandLineArgs()
        {
            var argDictionary = new Dictionary<string, string>();
            var args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLower();
                if(arg.StartsWith("--"))
                {
                    var value = i < args.Length - 1 ? args[i + 1].ToLower() : null;
                    value = (value?.StartsWith("--") ?? false) ? null : value;
                
                    argDictionary.Add(arg,value);
                }
            }
            return argDictionary;
        }
    }
}

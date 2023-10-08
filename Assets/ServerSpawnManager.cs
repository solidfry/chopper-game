using Unity.Netcode;
using UnityEngine;

public class ServerSpawnManager : MonoBehaviour
{
    private NetworkManager _networkmanager;

    private void Start()
    {
        _networkmanager = GetComponent<NetworkManager>();
        if (_networkmanager != null)
        {
            _networkmanager.OnClientDisconnectCallback += OnClientDisconnectCallback;
            _networkmanager.ConnectionApprovalCallback = ApprovalCheck;
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        var id= request.ClientNetworkId;
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = new Vector3(Random.Range(0, 100), Random.Range(0, 100), Random.Range(0, 100));
        Debug.Log(response.Position + " The position");
        response.Reason = "Testing the Approved approval message";
    }

    private void OnClientDisconnectCallback(ulong obj)
    {
        if (!_networkmanager.IsServer && _networkmanager.DisconnectReason != string.Empty)
        {
            Debug.Log($"Approval Declined Reason: {_networkmanager.DisconnectReason}");
        }
    }
}

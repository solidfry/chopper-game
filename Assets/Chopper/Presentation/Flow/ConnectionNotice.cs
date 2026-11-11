using Unity.NetCode;

namespace Chopper.Presentation.Flow
{
    /// <summary>Toast title and text for a closed server link (<see cref="Events.ConnectionLost"/>).</summary>
    public static class ConnectionNotice
    {
        public static string Title(bool wasConnected) => wasConnected ? "Connection lost" : "Cannot connect";

        public static string Text(NetworkStreamDisconnectReason reason, bool wasConnected)
        {
            switch (reason)
            {
                case NetworkStreamDisconnectReason.BadProtocolVersion:
                    return "The server runs a different game version. Update the game.";
                case NetworkStreamDisconnectReason.ApprovalFailure:
                case NetworkStreamDisconnectReason.ApprovalTimeout:
                case NetworkStreamDisconnectReason.AuthenticationFailure:
                    return "The server refused this client.";
                case NetworkStreamDisconnectReason.MaxConnectionAttempts:
                case NetworkStreamDisconnectReason.HandshakeTimeout:
                    return "No answer from the server.";
                case NetworkStreamDisconnectReason.ClosedByRemote:
                    return wasConnected ? "The server closed the match." : "The server closed the connection.";
                case NetworkStreamDisconnectReason.Timeout:
                    return wasConnected ? "No data from the server. Check your network." : "No answer from the server.";
                case NetworkStreamDisconnectReason.InvalidRpc:
                case NetworkStreamDisconnectReason.ProtocolError:
                    return "Network data error. The server and the game may not match.";
                default:
                    return wasConnected ? "The link to the server closed." : "Could not reach the server.";
            }
        }
    }
}

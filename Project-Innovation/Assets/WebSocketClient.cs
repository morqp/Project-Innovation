using UnityEngine;
using WebSocketSharp;

public class WebSocketClient : MonoBehaviour
{
    // Replace this URL with your WebSocket server address
    private string serverAddress = "wss://congruous-remarkable-giraffe.glitch.me";

    private WebSocket webSocket;

    void Start()
    {
        ConnectWebSocket();
    }

    void Update()
    {
        // Example: Send a message to the server on mouse click (you can adapt this based on your needs)
        if (Input.GetMouseButtonDown(0))
        {
            SendMessageToServer("Hello server from Unity!");
        }
    }

    void OnDestroy()
    {
        // Close the WebSocket connection when the script is destroyed
        if (webSocket != null && webSocket.IsAlive)
        {
            webSocket.Close();
        }
    }

    void ConnectWebSocket()
    {
        webSocket = new WebSocket(serverAddress);

        // Subscribe to WebSocket events
        webSocket.OnOpen += (sender, e) =>
        {
            Debug.Log("WebSocket connection opened");
        };

        webSocket.OnMessage += (sender, e) =>
        {
            Debug.Log($"Received message from server: {e.Data}");
        };

        webSocket.OnClose += (sender, e) =>
        {
            Debug.Log("WebSocket connection closed");
        };

        // Start the WebSocket connection
        webSocket.Connect();
    }

    void SendMessageToServer(string message)
    {
        if (webSocket != null && webSocket.IsAlive)
        {
            webSocket.Send(message);
        }
    }
}

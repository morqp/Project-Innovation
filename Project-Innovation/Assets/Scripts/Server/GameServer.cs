using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

public class GameServer : MonoBehaviour
{
    private WebSocket serverSocket;
    public Text gameCodeText;

    void Start()
    {
        // Set up the WebSocket server connection
        serverSocket = new WebSocket("ws://congruous-remarkable-giraffe.glitch.me");
        serverSocket.OnOpen += (sender, e) =>
        {
            Debug.Log("WebSocket connection opened");
            // Generate and send a message to request a game code
            RequestGameCode();
        };

        serverSocket.OnMessage += (sender, e) =>
        {
            // Handle incoming messages from the Glitch server
            Debug.Log($"Received message from server: {e.Data}");
            // Process the received message (e.g., extract the game code)
            ProcessMessage(e.Data);
        };

        serverSocket.OnClose += (sender, e) =>
        {
            Debug.Log("WebSocket connection closed");
        };

        // Start the WebSocket server connection
        serverSocket.Connect();
    }

    void RequestGameCode()
    {
        // Send a message to request a game code
        serverSocket.Send("RequestGameCode");
    }

    void ProcessMessage(string message)
    {
        // Assume the server responds with the game code
        if (message.StartsWith("GameCode:"))
        {
            string gameCode = message.Substring("GameCode:".Length);
            // Display the received game code in the Unity UI
            gameCodeText.text = "Game Code: " + gameCode;
        }
    }

    void OnDestroy()
    {
        // Close the WebSocket connection when the script is destroyed
        if (serverSocket != null && serverSocket.IsAlive)
        {
            serverSocket.Close();
        }
    }
}

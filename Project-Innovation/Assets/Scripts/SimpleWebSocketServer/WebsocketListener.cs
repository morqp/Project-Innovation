using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Net;

namespace WebSockets {
	class WebsocketListener {
		TcpListener listener;

        // Contains both the TcpClients where we're still negotiating the handshake (part of the header may still be missing),
        // and those where the handshake failed (they're not correctly upgrading to websockets?)
        List<TcpClient> processing; 
		Dictionary<TcpClient, string> header;

        // Contains the TcpClients that are upgraded to a websocket connection, until they are returned by an Accept call:
		List<TcpClient> ready;
		
		public WebsocketListener(int port=80) {
			listener = new TcpListener(IPAddress.Any, port);

			processing = new List<TcpClient>();
            header = new Dictionary<TcpClient, string>();
			ready = new List<TcpClient>();
		}

		public void Start() {
			listener.Start();
		}

        public bool Pending() {
            return ready.Count > 0;
        }

        public WebSocketConnection AcceptConnection(WebSocketConnection.PacketReceiveCallback callback) {
            var newWebsocket = new WebSocketConnection(ready[0], callback);
            ready.RemoveAt(0);
            return newWebsocket;
		}

        public void Update() {
			while (listener.Pending()) {
				processing.Add(listener.AcceptTcpClient());
			}

			for (int i=0;i<processing.Count;i++) {
                TcpClient client = processing[i];
                NetworkStream stream = client.GetStream();

                if (!stream.DataAvailable || client.Available < 3) continue; // match against "get"

                byte[] bytes = new byte[client.Available];
                stream.Read(bytes, 0, client.Available);
                string s = Encoding.UTF8.GetString(bytes);

                Console.WriteLine("Packet received:\n" + s);

                if (!header.ContainsKey(client)) {
                    header[client] = s;
                } else {
                    header[client] += s;
				}

                if (Regex.IsMatch(header[client], "^GET", RegexOptions.IgnoreCase)) {

                    // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
                    // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
                    // 3. Compute SHA-1 and Base64 hash of the new value
                    // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response

                    string swk = Regex.Match(header[client], "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
                    if (swk.Length > 0) {
                        Console.WriteLine("=====Handshaking from client=====\n{0}", header[client]);

                        string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                        byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                        string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);

                        // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                        byte[] response = Encoding.UTF8.GetBytes(
                            "HTTP/1.1 101 Switching Protocols\r\n" +
                            "Connection: Upgrade\r\n" +
                            "Upgrade: websocket\r\n" +
                            "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

                        stream.Write(response, 0, response.Length);

                        // Move to the list of ready TcpClients:
                        ready.Add(client);
                        processing.Remove(client);
                        i--;
                        header.Remove(client);
                    }
                }

            }
        }
	}
}

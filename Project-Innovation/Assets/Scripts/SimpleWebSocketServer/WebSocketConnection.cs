using System;
using System.Net.Sockets;
using System.Net; // For IPEndPoint

namespace WebSockets {
	public enum ConnectionStatus { Connecting, Connected, Disconnected };

	class WebSocketConnection {
		public ConnectionStatus Status { get; protected set; }

		const int maxFrameLength = 65535;

		public delegate void PacketReceiveCallback(NetworkPacket packet, WebSocketConnection connection);
		public delegate void DisconnectCallback(WebSocketConnection connection);

		public event DisconnectCallback OnDisconnect;
		protected PacketReceiveCallback OnPacketReceive;

		public int LocalPort {
			get {
				return ((IPEndPoint)client.Client.LocalEndPoint).Port;
			}
		}
		public IPEndPoint RemoteEndPoint {
			get {
				return (IPEndPoint)client.Client.RemoteEndPoint;
			}
		}

		TcpClient client;

		// NOTE: Using TCP, we cannot assume that the whole websocket packet comes in at once (though this will be true in 99% of the cases).
		// On the other hand, we also don't want to block until the whole packet has arrived (which would allow malicious clients to easily break the server)
		//
		// Therefore, the solution is to split the reading into three stages (HeaderStart, LongHeader, Body), and only start reading
		// from the socket when we know that the number of bytes that we expect is available.
		// This is the idea behind the finite state machine implemented in this class (the fields below).

		enum ReadState { HeaderStart, LongHeader, Body };

		ReadState state = ReadState.HeaderStart;
		int headerLength;
		int msglen = 0;

		public WebSocketConnection(TcpClient pClient, PacketReceiveCallback callback) {
			client = pClient;
			OnPacketReceive = callback;
			Status = ConnectionStatus.Connected;
		}

		/// <summary>
		/// Sends a packet to the client. (Currently only text is supported, including JSON of course.)
		/// </summary>
		public void Send(NetworkPacket packet) {
			if (!client.Connected) return;
			try {
				NetworkStream stream = client.GetStream();
				stream.WriteTimeout = 1;
				if (stream.CanWrite) {
					// left bit: FIN (we ignore splitting up into multiple frames!)
					// last four bits: value = 1, meaning we send a string. (2=binary, 8=close, 9=ping, 10=pong - not implemented yet!)
					byte b1 = 0b10000001;
					stream.WriteByte(b1);
					if (packet.Data.Length < 126) {
						stream.WriteByte((byte)(packet.Data.Length));
					} else if (packet.Data.Length <= ushort.MaxValue) {
						stream.WriteByte(126); // up next: 2 bytes containing the length
						stream.WriteByte((byte)(packet.Data.Length >> 8));
						stream.WriteByte((byte)(packet.Data.Length & 255));
					} else {
						throw new Exception("Cannot send huge frames yet");
						// What we should do: write 127, and then in 8 bytes (ulong), write length. 
					}
					stream.Write(packet.Data,0,packet.Data.Length);
				} else {
					Console.WriteLine("Error: cannot send, because cannot write to network stream");
				}
			} catch (Exception error) {
				Console.WriteLine("NetworkConnection.Send: " + error.Message);
				client.Close();
				Status = ConnectionStatus.Disconnected;
				if (OnDisconnect != null) OnDisconnect(this);
			}
		}

		/// <summary>
		/// Reads the first two bytes of a new frame (the header start), and changes state accordingly.
		/// Sets msglen or headerLength, depending on the new state.
		/// </summary>
		void ReadHeaderStart() {
			byte[] data;
			NetworkStream stream = client.GetStream();

			Console.WriteLine("Reading header start");
			data = new byte[2];
			stream.Read(data, 0, 2);
			bool fin = (data[0] & 0b10000000) != 0,
				mask = (data[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"
			if (!mask) { // This is for server-side websockets. Client side maskless is allowed
				Console.WriteLine("Closing connection: no mask used");
				Close();
				return;
			}
			Console.WriteLine("Fin: {0} Mask: {1}", fin, mask);
			int opcode = data[0] & 0b00001111; // expecting 1 - text message // TODO: generalize (binary, ping, pong, close)
			msglen = data[1] - 128; // & 0111 1111
			if (msglen == 126) {
				headerLength = 2;
				state = ReadState.LongHeader; // or actually medium length header
				Console.WriteLine("Reading header of length {0}", headerLength);
			} else if (msglen == 127) {
				headerLength = 8;
				state = ReadState.LongHeader;
				Console.WriteLine("Reading header of length {0}", headerLength);
			} else {
				Console.WriteLine("Reading frame of length {0}", msglen);
				state = ReadState.Body;
			}
		}

		/// <summary>
		/// Reads the remainder of a (medium or long) header. headerLength must be set correctly. Sets msglen, and changes state to ReadState.Body.
		/// </summary>
		void ReadFullHeader() {
			byte[] data;
			NetworkStream stream = client.GetStream();

			Console.WriteLine("Reading full header");
			data = new byte[headerLength];
			stream.Read(data, 0, headerLength);
			if (headerLength == 2) {
				msglen = data[1] + data[0] << 8;
			} else {
				ulong length = 0;
				ulong mult = 1;
				for (int i = 0; i < 8; i++) {
					length += data[7 - i] * mult;
					mult *= 256;
				}
				Console.WriteLine("Message length: {0}", length);
				if (length > maxFrameLength) {
					Console.WriteLine("Message too long!");
					msglen = 0;
					Close();
				} else {
					msglen = (int)length;
				}
			}
			Console.WriteLine("Reading frame of length {0}", msglen);
			state = ReadState.Body;
		}

		/// <summary>
		/// Reads the body of a frame. Assumes msglen is correctly set (from reading the header).
		/// Calls OnPacketReceive, and changes the state to HeaderStart again.
		/// </summary>
		void ReadBody() {
			byte[] data;
			NetworkStream stream = client.GetStream();

			Console.WriteLine("Reading (masked) frame of length {0} Available: {1}", msglen, client.Available);

			data = new byte[msglen + 4];
			stream.Read(data, 0, msglen + 4);

			// Decode using XOR mask:
			byte[] decoded = new byte[msglen];
			for (int i = 0; i < msglen; i++) {
				decoded[i] = (byte)(data[4 + i] ^ data[i % 4]);
			}

			NetworkPacket packet = new NetworkPacket(decoded);
			if (OnPacketReceive != null) {
				OnPacketReceive(packet, this);
			}
			state = ReadState.HeaderStart;
		}

		/// <summary>
		/// Call update to check for incoming messages. If a full message has arrived, OnPacketReceive will be called.
		/// </summary>
		public void Update() {
			bool reading = client.Available > 0;

			while (reading && Status == ConnectionStatus.Connected) {
				switch (state) {
					case ReadState.HeaderStart: // We expect a new frame with header to arrive
						if (client.Available >= 2) { // minimum header size is two
							ReadHeaderStart();
						} else {
							reading = false;
						}
						break;
					case ReadState.LongHeader:
						if (client.Available >= headerLength) {
							ReadFullHeader();
						} else {
							reading = false;
						}
						break;
					case ReadState.Body:
						if (client.Available >= msglen + 4) { // first four bytes: mask
							Console.WriteLine("Reading (masked) frame of length {0} Available: {1}", msglen, client.Available);
							ReadBody();
						} else {
							reading = false;
						}
						break;
				}
			}
		}

		void Close() {
			client.Close();
			Status = ConnectionStatus.Disconnected;
			if (OnDisconnect != null) OnDisconnect(this);
		}
	}
}

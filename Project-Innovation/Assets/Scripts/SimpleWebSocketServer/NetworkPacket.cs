using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebSockets {
	public class NetworkPacket {
		public byte[] Data;
		public NetworkPacket(byte[] data) {
			Data = data;
		}
	}
}
using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace DarkGalaxy_Networking
{
    internal class AEUDPPendingConnection : IDisposable//udělat revizi
    {
        public readonly int ConnectionID;
        public readonly IPEndPoint Host;

        public AEUDPPendingConnection(int connectionID, IPEndPoint host)
        {
            ConnectionID = connectionID;
            Host = host;
        }

        private Packet? _lastReceivedPacket = null;
        public Packet? LastReceivedPacket
        {
            get
            {
                if (!_lastReceivedPacket.HasValue)
                    return null;

                Packet p = _lastReceivedPacket.Value;
                _lastReceivedPacket = null;
                return p;
            }
        }

        public DateTime LastReceive = DateTime.UtcNow;

        public void PacketReceived(ref Packet packet)
        {
            if (_lastReceivedPacket.HasValue)
                PacketBufferPool.ReturnBuffer(_lastReceivedPacket.Value.Buffer);

            _lastReceivedPacket = packet;

            LastReceive = DateTime.UtcNow;
        }

        public Packet? HandshakeRequestPacket = null;
        public PacketAEAD AEAD = null;

        public byte[] ServerVerificationVector = null;

        public void Dispose()//bez AEAD ! - možná
        {
            throw new NotImplementedException();
        }
    }
}

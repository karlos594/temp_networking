using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Reflection;

namespace DarkGalaxy_Networking
{
    public class AEUDPClient : AEUDPConnection//udělat revizi
    {
        internal AEUDPClient(Socket activeSocket, int connectionID, IPEndPoint host, int maxPendingPackets, PacketAEAD aead) : base(activeSocket, connectionID, host, maxPendingPackets, aead)
        {
            //create socket, BeginReceive -> EndReceive -> ...
        }

        //1 sec -> send interval

        //receive timeout -> max
        //attempts count -> max

        public override void Dispose()
        {
            base.Dispose();

            throw new NotImplementedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Security.Cryptography;

namespace DarkGalaxy_Networking
{
    internal class AEUDPPacketHandler : IDisposable
    {
        private readonly Socket _listenSocket;
        private readonly AEUDPDriver _driver;

        public AEUDPPacketHandler(Socket listenSocket, AEUDPDriver driver)
        {
            _listenSocket = listenSocket;
            _driver = driver;
        }

        private bool _canRun = false;
        private Thread _receiveThread = null;

        private Timer _refreshBlockedTimer = null;

        public void Start()
        {
            _canRun = true;

            _receiveThread = new Thread(new ThreadStart(ReceiveLoop));
            _receiveThread.Name = "AEUDPPacketHandler_Thread";
            _receiveThread.IsBackground = true;
            _receiveThread.Priority = ThreadPriority.AboveNormal;
            _receiveThread.Start();

            _refreshBlockedTimer = new Timer(UpdateBlockedAddresses, null, 0, 10 * 60 * 1000);//10 min interval

            Console.WriteLine(this.GetType().Name + " - Running");
        }

        //min 40 000 pps - 2 000 players
        private void ReceiveLoop()
        {
            //byte[] buffer = new byte[65535];//gc moving ? -> spíše použít můj PacketBuffer ? ale vyřešit když přijde větší velikost -> vyvarovat se throw error
            EndPoint socketEndpoint = new IPEndPoint(IPAddress.Any, 0);

            while (_canRun)//osefovat SocketException -> ICMP Error -> fragmentation needed => souvisí s MTU Discovery
            {//udp max size 65535
                PacketBuffer buffer = PacketBufferPool.GetBuffer();
                int receivedLength = _listenSocket.ReceiveFrom(buffer.Buffer, ref socketEndpoint);//pokud přijde větší než je buffer -> SocketException !! (řešení: hodit do try {} a pokud přijde větší zpráva než povolená -> zablokovat IP:Port - NEPOMŮŽE ! stejně musim převzít zprávu než dostanu IP:Port od koho to přišlo)
                //TempAvailable = ActiveSocket.Available;//temp

                IPEndPoint remote = (IPEndPoint)socketEndpoint;
                //int remoteHashCode = remote.GetHashCode();

                if (IsAddressBlocked(remote.Address.GetHashCode()))
                {
                    PacketBufferPool.ReturnBuffer(buffer);
                    continue;
                }

                if (receivedLength < Packet.HEADER_SIZE)
                {
                    PacketBufferPool.ReturnBuffer(buffer);
                    continue;
                }

                var packet = Packet.Parse(buffer);
                if(!packet.HasValue)
                {
                    PacketBufferPool.ReturnBuffer(buffer);
                    continue;
                }

                buffer.DataLength = receivedLength - Packet.HEADER_SIZE;

                _driver.PacketReceived(ref remote, packet.Value);
            }
        }

        private Dictionary<int, DateTime> _blockedAddresses = new Dictionary<int, DateTime>();
        private void UpdateBlockedAddresses(object state)
        {
            if (_blockedAddresses.Count == 0)
                return;

            DateTime nowTime = DateTime.UtcNow;

            lock (_blockedAddresses)
            {
                List<int> clearEndpoints = new List<int>();

                foreach (var item in _blockedAddresses)
                {
                    if ((nowTime - item.Value).TotalHours >= 1)
                        clearEndpoints.Add(item.Key);
                }

                clearEndpoints.ForEach(x => _blockedAddresses.Remove(x));
                clearEndpoints.Clear();
            }
        }
        public void BlockAddress(int addresssHash, DateTime until)
        {
            lock (_blockedAddresses)
                _blockedAddresses[addresssHash] = until;

            //lock (_blockedAddresses)
            //    _blockedAddresses.TryAdd(addresssHash, until);
        }
        public bool IsAddressBlocked(int addresssHash)
        {
            lock (_blockedAddresses)
                return _blockedAddresses.ContainsKey(addresssHash);
        }

        public void Dispose()
        {
            _canRun = false;

            _refreshBlockedTimer.Dispose();
            _blockedAddresses.Clear();
        }
    }
}

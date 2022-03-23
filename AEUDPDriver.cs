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
    public class AEUDPDriver : IDisposable
    {
        private Dictionary<int, AEUDPConnection> _connections = new Dictionary<int, AEUDPConnection>();//spise rozdelit jen na IP a pote array s odlisnymi porty
        public AEUDPConnection[] GetConnections()//gc
        {
            lock (_connections)
            {
                var array = new AEUDPConnection[_connections.Values.Count];//omezit a array znovu použít ! třeba max počet spojení = 5000
                _connections.Values.CopyTo(array, 0);

                return array;
            }
        }

        internal void AddConnection(AEUDPConnection connection)
        {
#if NETSTANDARD2_1
            lock (_connections)
                _connections.TryAdd(connection.ConnectionID, connection);
#else
            lock (_connections)
                if (!_connections.ContainsKey(connection.ConnectionID))
                    _connections.Add(connection.ConnectionID, connection);
#endif
        }
        internal void RemoveConnection(AEUDPConnection connection)
        {
            lock (_connections)
                _connections.Remove(connection.ConnectionID);
        }

        public AEUDPConnection GetConnection(int connectionID)
        {
            lock (_connections)
            {
                AEUDPConnection connection;
                _connections.TryGetValue(connectionID, out connection);
                return connection;
            }
        }

        public AEUDPDriver()
        {

        }

        public AEUDPConnection Connect()//client side
        {
            throw new NotImplementedException();
        }

        public void ProcessPendingPackets()
        {
            var connections = GetConnections();
            int connectionsLength = connections.Length;
            for (int i = 0; i < connectionsLength; i++)
            {
                AEUDPConnection connection = connections[i];
                if (!connection.IsConnected)
                {
                    RemoveConnection(connection);
                    connection.Dispose();
                    continue;
                }

                connection.ProcessPendingPackets();
            }
        }

        //send pending messages

        private const int SIO_UDP_CONNRESET = -1744830452;//ignore connection_reset (klient je nedostupný když se zavolá send_to), + přidat ignore bigger size packets!

        private IPEndPoint _localEndpoint = null;
        private Socket _listenSocket = null;
        //public Socket ActiveSocket => _listenSocket;

        public void Bind(IPEndPoint local)
        {
            _localEndpoint = local;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _listenSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            _listenSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DontFragment, true);
            //_listenSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.NoChecksum, true);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            //_listenSocket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, new byte[] { 0 });
            _listenSocket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
            _listenSocket.ReceiveBufferSize = Packet.MAX_MTU * 2000;
            _listenSocket.SendBufferSize = Packet.MAX_MTU * 2000;
            _listenSocket.Bind(_localEndpoint);
        }

        private AEUDPPacketHandler _packetHandler = null;
        private AEUDPConnectionHandler _connectionHandler = null;

        public void Listen(int backlog = 100)
        {
            _packetHandler = new AEUDPPacketHandler(_listenSocket, this);

            _connectionHandler = new AEUDPConnectionHandler(backlog, _listenSocket, _packetHandler);
            _connectionHandler.Start();

            _packetHandler.Start();

            Console.WriteLine(this.GetType().Name + " - Listening");
        }

        internal void PacketReceived(ref IPEndPoint remote, Packet packet)//ThreadPool.QueueUserWorkItem()
        {
            int connectionID = remote.GetHashCode();
            AEUDPConnection connection = GetConnection(connectionID);

            if(connection != null)
            {
                if(!connection.PacketReceived(ref packet))//spise in param ?
                    PacketBufferPool.ReturnBuffer(packet.Buffer);
            }
            else
            {
                if(_connectionHandler == null || !_connectionHandler.ProcessPacket(connectionID, ref remote, ref packet))
                    PacketBufferPool.ReturnBuffer(packet.Buffer);
            }
        }

        public AEUDPConnection Accept()
        {
            var newConnection = _connectionHandler.GetAcceptedConnection();
            if (newConnection != null)
                AddConnection(newConnection);

            return newConnection;
        }

        public void Dispose()
        {
            _connectionHandler.Dispose();
            _packetHandler.Dispose();

            _listenSocket.Shutdown(SocketShutdown.Both);
            _listenSocket.Close();
            //_listenSocket.Dispose();

            //dispose all connections ?

            throw new NotImplementedException();
        }
    }
}

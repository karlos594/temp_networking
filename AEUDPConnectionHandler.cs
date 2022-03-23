using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Security.Cryptography;
using System.Diagnostics;
using System.IO;

namespace DarkGalaxy_Networking
{
    internal class AEUDPConnectionHandler : IDisposable//udělat revizi
    {
        private const int RSA_KEY_SIZE_IN_BITS = 2048;
        private const int RSA_KEY_SIZE = RSA_KEY_SIZE_IN_BITS / 8;
        private const int RANDOM_VERIFICATION_VECTOR_SIZE = 16;

        private RSACryptoServiceProvider _rsa;
        private RandomNumberGenerator _rng;

        private readonly int _backlog;
        private Dictionary<int, AEUDPPendingConnection> _pendingConnections;

        private readonly Socket _activeSocket;
        private readonly AEUDPPacketHandler _packetHandler;//na zablokování IP

        public AEUDPConnectionHandler(int backlog, Socket activeSocket, AEUDPPacketHandler packetHandler)
        {
            _rsa = new RSACryptoServiceProvider(RSA_KEY_SIZE_IN_BITS);

            if(File.Exists("privatekey.rsa"))
            {
                _rsa.ImportCspBlob(File.ReadAllBytes("privatekey.rsa"));
            }
            else
            {
                byte[] publicKey = _rsa.ExportCspBlob(false);
                byte[] privateKey = _rsa.ExportCspBlob(true);

                File.WriteAllBytes("publickey.rsa", publicKey);
                File.WriteAllBytes("privatekey.rsa", privateKey);
            }

            _rng = RandomNumberGenerator.Create();

            _backlog = backlog;
            _pendingConnections = new Dictionary<int, AEUDPPendingConnection>(_backlog);
            _pendingConnectionsArray = new AEUDPPendingConnection[_backlog];

            _activeSocket = activeSocket;
            _packetHandler = packetHandler;

            _resetEvent = new AutoResetEvent(false);
        }

        private bool _canRun = false;
        private Thread _handlerThread = null;
        private AutoResetEvent _resetEvent;

        internal void Start()
        {
            _canRun = true;

            _handlerThread = new Thread(new ThreadStart(UpdatePendingConnectionsLoop));
            _handlerThread.Name = "AEUDPConnectionsHandler_Thread";
            _handlerThread.IsBackground = true;
            _handlerThread.Priority = ThreadPriority.Normal;
            _handlerThread.Start();

            Console.WriteLine(this.GetType().Name + " - Running");
        }

        internal bool ProcessPacket(int connectionID, ref IPEndPoint remote, ref Packet packet)
        {
            AEUDPPendingConnection pendingConnection;

            lock (_pendingConnections)
            {
                _pendingConnections.TryGetValue(connectionID, out pendingConnection);

                if(pendingConnection == null)
                {
                    if (_pendingConnections.Count >= _backlog)
                        return false;

                    Console.WriteLine("packet type: " + packet.Type.ToString() + " | packet payload length: " + packet.Buffer.DataLength);

                    if (packet.Type != PacketType.ConnectionRequest || packet.Buffer.DataLength != RSA_KEY_SIZE)
                        return false;

                    Console.WriteLine("Creating new AEUDPPendingConnection...");

                    pendingConnection = new AEUDPPendingConnection(connectionID, new IPEndPoint(remote.Address, remote.Port));
#if NETSTANDARD2_1
                    _pendingConnections.TryAdd(pendingConnection.ConnectionID, pendingConnection);
#else
                        if (_pendingConnections.ContainsKey(connectionID))
                            _pendingConnections.Add(connectionID, pendingConnection);
#endif
                }
            }

            lock (pendingConnection)
                pendingConnection.PacketReceived(ref packet);

            _resetEvent.Set();
            return true;
        }

        private int GetPendingConnections()
        {
            lock (_pendingConnections)
            {
                var values = _pendingConnections.Values;
                values.CopyTo(_pendingConnectionsArray, 0);

                return values.Count;
            }
        }
        private AEUDPPendingConnection[] _pendingConnectionsArray;
        private void UpdatePendingConnectionsLoop()
        {
            while(true)
            {
                _resetEvent.WaitOne();
                if (!_canRun)
                    break;

                DateTime nowTime = DateTime.UtcNow;

                int pendingConnectionsCount = GetPendingConnections();
                for (int i = 0; i < pendingConnectionsCount; i++)
                {
                    AEUDPPendingConnection pendingConnection = _pendingConnectionsArray[i];
                    if (pendingConnection == null)
                        continue;

                    _pendingConnectionsArray[i] = null;

                    ProcessPendingConnection(nowTime, pendingConnection);
                }
            }
        }

        private void ProcessPendingConnection(DateTime nowTime, AEUDPPendingConnection pendingConnection)
        {
            lock(pendingConnection)
            {
                if ((nowTime - pendingConnection.LastReceive).TotalSeconds > 3)
                {
                    lock (_pendingConnections)
                        _pendingConnections.Remove(pendingConnection.ConnectionID);//dispose connection & možná zablokovat na 1 min ?

                    return;
                }

                Packet? p = pendingConnection.LastReceivedPacket;
                if (!p.HasValue)
                    return;

                Packet packet = p.Value;
                switch(packet.Type)
                {
                    case PacketType.ConnectionRequest://1x max
                        {
                            //rsa decrypt - spíše přesunout do receive -> před tím než se vytvoří nový objekt (problém že zbrzdí příjem packetů)

                            if(pendingConnection.AEAD == null || !pendingConnection.HandshakeRequestPacket.HasValue)
                            {
                                if(packet.Buffer.DataLength == RSA_KEY_SIZE)
                                {
                                    var rawBuffer = PacketBufferPool.GetBuffer();
#if NETSTANDARD2_1
                                    bool succeed = _rsa.TryDecrypt(new ReadOnlySpan<byte>(packet.Buffer.Buffer, PacketBuffer.DATA_OFFSET, packet.Buffer.DataLength), new Span<byte>(rawBuffer.Buffer, PacketBuffer.DATA_OFFSET, PacketBuffer.MAX_DATA_LENGTH), RSAEncryptionPadding.OaepSHA1, out rawBuffer.DataLength);
#else
                                    bool succeed = false;
                                    //byte[] raw = _rsa.Decrypt();//dodělat ?
#endif

                                    if(succeed && rawBuffer.DataLength == (PacketAEAD.KEY_SIZE + PacketAEAD.IV_SIZE + PacketAEAD.AUTH_KEY_SIZE + RANDOM_VERIFICATION_VECTOR_SIZE))
                                    {
                                        byte[] cryptoKey = new byte[PacketAEAD.KEY_SIZE];
                                        byte[] cryptoIV = new byte[PacketAEAD.IV_SIZE];
                                        byte[] authKey = new byte[PacketAEAD.AUTH_KEY_SIZE];
                                        byte[] clientVerificationVector = new byte[RANDOM_VERIFICATION_VECTOR_SIZE];

                                        rawBuffer.CopyTo(0, PacketAEAD.KEY_SIZE, cryptoKey, 0);
                                        rawBuffer.CopyTo(PacketAEAD.KEY_SIZE, PacketAEAD.IV_SIZE, cryptoIV, 0);
                                        rawBuffer.CopyTo(PacketAEAD.KEY_SIZE + PacketAEAD.IV_SIZE, PacketAEAD.AUTH_KEY_SIZE, authKey, 0);
                                        rawBuffer.CopyTo(PacketAEAD.KEY_SIZE + PacketAEAD.IV_SIZE + PacketAEAD.AUTH_KEY_SIZE, RANDOM_VERIFICATION_VECTOR_SIZE, clientVerificationVector, 0);

                                        pendingConnection.AEAD = new PacketAEAD(cryptoKey, cryptoIV, authKey);

                                        pendingConnection.ServerVerificationVector = new byte[RANDOM_VERIFICATION_VECTOR_SIZE];
                                        _rng.GetBytes(pendingConnection.ServerVerificationVector);

                                        var handshakeRequestBuffer = PacketBufferPool.GetBuffer();
                                        handshakeRequestBuffer.Write(clientVerificationVector, 0, RANDOM_VERIFICATION_VECTOR_SIZE);
                                        handshakeRequestBuffer.Write(pendingConnection.ServerVerificationVector, 0, RANDOM_VERIFICATION_VECTOR_SIZE);

                                        var rawPacket = new Packet(PacketType.ConnectionHandshakeRequest, handshakeRequestBuffer);
                                        var encryptedPacket = pendingConnection.AEAD.Encrypt(ref rawPacket);

                                        pendingConnection.HandshakeRequestPacket = encryptedPacket;
                                    }
                                    else
                                    {
                                        //drop & block
                                    }
                                }
                                else
                                {
                                    //drop & block ?
                                }
                            }

                            if (pendingConnection.HandshakeRequestPacket.HasValue)
                                pendingConnection.HandshakeRequestPacket.Value.Send(_activeSocket, pendingConnection.Host);

                            break;
                        }
                    case PacketType.ConnectionHandshakeResponse:
                        {
                            if(pendingConnection.AEAD != null)
                            {
                                var p1 = pendingConnection.AEAD.Decrypt(ref packet);
                                if(p1.HasValue)
                                {
                                    var rawPacket = p1.Value;
                                    if(rawPacket.Buffer.DataLength == RANDOM_VERIFICATION_VECTOR_SIZE)
                                    {
                                        if(PacketAEAD.SequenceEqual(rawPacket.Buffer.Buffer, PacketBuffer.DATA_OFFSET, RANDOM_VERIFICATION_VECTOR_SIZE, pendingConnection.ServerVerificationVector, 0))
                                        {
                                            var acceptedConnection = new AEUDPConnection(_activeSocket, pendingConnection.ConnectionID, pendingConnection.Host, 5, pendingConnection.AEAD);
                                            _acceptedConnections.Push(acceptedConnection);

                                            lock (_pendingConnections)
                                                _pendingConnections.Remove(pendingConnection.ConnectionID);//dispose pending connection !
                                        }
                                        else
                                        {
                                            //drop & block
                                        }
                                    }
                                    else
                                    {
                                        //drop & block
                                    }
                                }
                            }

                            break;
                        }
                    default:
                        {
                            //drop & block
                            break;
                        }
                }
            }
        }

        private ConcurrentStack<AEUDPConnection> _acceptedConnections = new ConcurrentStack<AEUDPConnection>();//capacity ?
        public AEUDPConnection GetAcceptedConnection()
        {
            AEUDPConnection connection;
            _acceptedConnections.TryPop(out connection);

            return connection;
        }

        public void Dispose()
        {
            _canRun = false;

            _rsa.Dispose();
            _rng.Dispose();

            _resetEvent.Set();
            _resetEvent.Dispose();

            throw new NotImplementedException();
        }
    }
}

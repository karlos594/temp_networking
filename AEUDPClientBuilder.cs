using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace DarkGalaxy_Networking
{
    public class AEUDPClientBuilder//musí se dodělat ! -> safe checks, ..., //udělat revizi
    {
        private const int RSA_KEY_SIZE_IN_BITS = 2048;
        private const int RSA_KEY_SIZE = RSA_KEY_SIZE_IN_BITS / 8;
        private const int RANDOM_VERIFICATION_VECTOR_SIZE = 16;

        private IPEndPoint _host;
        private Socket _socket;
        private PacketBuffer _receiveBuffer;

        private PacketAEAD _aead;

        private readonly byte[] _verificationVector;
        private readonly Packet _connectionRequestPacket;

        private readonly int _timeoutSeconds;

        public AEUDPClientBuilder(in IPEndPoint host, byte[] rsaPublicKey, int timeoutSeconds)
        {
            _host = host;

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DontFragment, true);

            _receiveBuffer = PacketBufferPool.GetBuffer();

            using(var rsa = new RSACryptoServiceProvider(RSA_KEY_SIZE_IN_BITS))
            using(var rng = RandomNumberGenerator.Create())
            {
                rsa.ImportCspBlob(rsaPublicKey);

                var cryptoKey = new byte[PacketAEAD.KEY_SIZE];
                var cryptoIV = new byte[PacketAEAD.IV_SIZE];
                var authKey = new byte[PacketAEAD.AUTH_KEY_SIZE];

                _verificationVector = new byte[RANDOM_VERIFICATION_VECTOR_SIZE];

                rng.GetBytes(cryptoKey);
                rng.GetBytes(cryptoIV);
                rng.GetBytes(authKey);

                rng.GetBytes(_verificationVector);

                byte[] requestPacketRaw = new byte[cryptoKey.Length + cryptoIV.Length + authKey.Length + _verificationVector.Length];

                Buffer.BlockCopy(cryptoKey, 0, requestPacketRaw, 0, cryptoKey.Length);
                Buffer.BlockCopy(cryptoIV, 0, requestPacketRaw, 0 + cryptoKey.Length, cryptoIV.Length);
                Buffer.BlockCopy(authKey, 0, requestPacketRaw, 0 + cryptoKey.Length + cryptoIV.Length, authKey.Length);
                Buffer.BlockCopy(_verificationVector, 0, requestPacketRaw, 0 + cryptoKey.Length + cryptoIV.Length + authKey.Length, _verificationVector.Length);

                byte[] requestPacketEncrypted = rsa.Encrypt(requestPacketRaw, RSAEncryptionPadding.OaepSHA1);

                var packetBuffer = PacketBufferPool.GetBuffer();
                packetBuffer.Write(requestPacketEncrypted, 0, requestPacketEncrypted.Length);

                _connectionRequestPacket = new Packet(PacketType.ConnectionRequest, packetBuffer);

                _aead = new PacketAEAD(cryptoKey, cryptoIV, authKey);

                Array.Clear(cryptoKey, 0, cryptoKey.Length);
                Array.Clear(cryptoIV, 0, cryptoIV.Length);
                Array.Clear(authKey, 0, authKey.Length);
            }

            _timeoutSeconds = timeoutSeconds;
        }

        public delegate void ConnectionEvent(AEUDPClientBuilder sender, ConnectionStatus status, AEUDPClient client);
        public event ConnectionEvent OnConnectionStatusChanged;

        private Timer _updateTimer = null;

        public void ConnectAsync()
        {
            _socket.Connect(_host);

            _canRun = true;

            StartReceive();

            _updateTimer = new Timer(Update, null, 0, 2000);//2 sec

            Console.WriteLine("Connecting...");
        }

        private DateTime _lastReceive = DateTime.UtcNow;

        private void StartReceive()
        {
            if (!_canRun)
                return;

            //_socket.BeginReceiveFrom(_buffer.Buffer, 0, PacketBuffer.MAX_LENGTH, SocketFlags.None, ref _host, EndReceive, null);
            _socket.BeginReceive(_receiveBuffer.Buffer, 0, Packet.MIN_MTU, SocketFlags.None, EndReceive, null);
        }
        private void EndReceive(IAsyncResult result)
        {
            Console.WriteLine("end receive");

            if (_socket == null)
                return;

            //int receivedLength = _socket.EndReceiveFrom(result, ref _host);
            int receivedLength = _socket.EndReceive(result);//throws
            if(receivedLength == 0)
                return;

            _receiveBuffer.DataLength = receivedLength - Packet.HEADER_SIZE;
            if(_receiveBuffer.DataLength < 0)
            {
                StartReceive();
                return;
            }

            Packet? receivedPacket = Packet.Parse(_receiveBuffer);
            if(!receivedPacket.HasValue)
            {
                StartReceive();
                return;
            }

            if(ProcessPacket(receivedPacket.Value))
                StartReceive();
        }

        private Packet? _handshakeResponsePacket = null;

        private object _lockObj = new object();

        private bool ProcessPacket(Packet packet)
        {
            lock(_lockObj)
            {
                if (!_canRun)
                    return false;

                switch (packet.Type)
                {
                    case PacketType.ConnectionHandshakeRequest:
                        {
                            if (packet.Buffer.DataLength < RANDOM_VERIFICATION_VECTOR_SIZE * 2 + PacketAEAD.ENCRYPTED_OVERHEAD_SIZE)
                            {
                                Close(ConnectionStatus.Connecting_Error_Invalid, null);
                                return false;
                            }

                            Packet? p = _aead.Decrypt(ref packet);
                            if (!p.HasValue)
                            {
                                Close(ConnectionStatus.Connecting_Error_Verification, null);
                                return false;
                            }

                            Packet decryptedPacket = p.Value;
                            PacketBuffer decryptedPacketBuffer = decryptedPacket.Buffer;
                            if (decryptedPacketBuffer.DataLength != RANDOM_VERIFICATION_VECTOR_SIZE * 2)
                            {
                                Close(ConnectionStatus.Connecting_Error_Verification, null);
                                return false;
                            }

                            if (!PacketAEAD.SequenceEqual(decryptedPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET, RANDOM_VERIFICATION_VECTOR_SIZE, _verificationVector, 0))
                            {
                                Close(ConnectionStatus.Connecting_Error_Verification, null);
                                return false;
                            }

                            if(_handshakeResponsePacket.HasValue)
                            {
                                PacketBufferPool.ReturnBuffer(_handshakeResponsePacket.Value.Buffer);
                            }

                            var tempBuffer = PacketBufferPool.GetBuffer();
                            decryptedPacketBuffer.CopyTo(RANDOM_VERIFICATION_VECTOR_SIZE, RANDOM_VERIFICATION_VECTOR_SIZE, tempBuffer, 0);

                            var tempPacket = new Packet(PacketType.ConnectionHandshakeResponse, tempBuffer);
                            _handshakeResponsePacket = _aead.Encrypt(ref tempPacket);

                            PacketBufferPool.ReturnBuffer(tempBuffer);

                            _lastReceive = DateTime.UtcNow;

                            return true;
                        }
                    case PacketType.ConnectionAccepted:
                        {
                            Close(ConnectionStatus.Connected, new AEUDPClient(_socket, _host.GetHashCode(), _host, 10, _aead));
                            return false;
                        }
                    case PacketType.ConnectionDenied:
                        {
                            Close(ConnectionStatus.Connecting_Error_Denied, null);
                            return false;
                        }
                }

                Close(ConnectionStatus.Connecting_Error_Verification, null);
                return false;
            }
        }

        private bool _canRun = true;
        private void Update(object state)
        {
            lock(_lockObj)
            {
                if (!_canRun)
                    return;

                if ((DateTime.UtcNow - _lastReceive).TotalSeconds > _timeoutSeconds)
                {
                    Close(ConnectionStatus.Connecting_Error_Timeout, null);
                    return;
                }

                if(_handshakeResponsePacket.HasValue)//attempts -> protože může druhá strana posílat validní packety (resets LastReceive) ale nebude přijímat odpověď takže může posílat pořád HandshakeRequest
                {
                    _handshakeResponsePacket.Value.Send(_socket, _host);
                }
                else
                {
                    _connectionRequestPacket.Send(_socket, _host);
                }
            }
        }

        private void Close(ConnectionStatus status, AEUDPClient client)//lock ?
        {
            _canRun = false;

            _updateTimer.Dispose();

            PacketBufferPool.ReturnBuffer(_receiveBuffer);
            PacketBufferPool.ReturnBuffer(_connectionRequestPacket.Buffer);

            if(_handshakeResponsePacket.HasValue)
                PacketBufferPool.ReturnBuffer(_handshakeResponsePacket.Value.Buffer);

            if(client == null)
            {
                _aead.Dispose();

                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }

            OnConnectionStatusChanged?.Invoke(this, status, client);
        }

        public enum ConnectionStatus : byte
        {
            Connecting,

            Connecting_PerformingAuth,

            Connecting_Error_Socket,
            Connecting_Error_Invalid,
            Connecting_Error_Verification,
            Connecting_Error_Timeout,
            Connecting_Error_Denied,

            Connected
        }
    }
}

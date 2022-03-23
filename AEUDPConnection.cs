using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace DarkGalaxy_Networking
{
    public class AEUDPConnection : IDisposable
    {
        public readonly Socket ActiveSocket;
        public readonly int ConnectionID;
        public readonly IPEndPoint Remote;

        private readonly int _maxPendingPackets;
        private Queue<Packet> _pendingPackets;

        private readonly PacketAEAD _aead;

        internal AEUDPConnection(Socket activeSocket, int connectionID, IPEndPoint remote, int maxPendingPackets, byte[] cryptoKey, byte[] cryptoIV, byte[] authKey) : this(activeSocket, connectionID, remote, maxPendingPackets, new PacketAEAD(cryptoKey, cryptoIV, authKey)) { }
        internal AEUDPConnection(Socket activeSocket, int connectionID, IPEndPoint remote, int maxPendingPackets, in PacketAEAD aead)
        {
            ActiveSocket = activeSocket;
            ConnectionID = connectionID;
            Remote = remote;

            _maxPendingPackets = maxPendingPackets;
            _pendingPackets = new Queue<Packet>(_maxPendingPackets);

            _aead = aead;
        }

        public int PayloadMTU = Packet.MIN_MTU - Packet.HEADER_SIZE - PacketAEAD.ENCRYPTED_OVERHEAD_SIZE;//dynamic? -> MTU discovery function (započítat 20byte IP header, 8byte UDP header), každých 10 minut ?
                                     //posílat od nejmenší velikosti po největší možnou (když se vrátí odpověď), pokud nepřijde odpověď -> poslat max. 3x znovu ale se zvyšujícím timeoutem

        public DateTime LastReceive = DateTime.UtcNow;
        public bool IsConnected => (DateTime.UtcNow - LastReceive).TotalSeconds <= 30;//nějak to udělat aby se volalo jen 1x za frame

        //přidat různé kanály (channels) třeba pro unreliable, reliable a reliable ordered

        //keep alive -> 15 sec ?

        public bool PacketReceived(ref Packet packet)
        {
            lock (_pendingPackets)
            {
                if (_pendingPackets.Count < _maxPendingPackets)
                {
                    _pendingPackets.Enqueue(packet);
                    return true;
                }
                else return false;
            }
        }

        public void ProcessPendingPackets()
        {
            lock (_pendingPackets)//zbrzdí příjem packetů !
            {
                int availablePackets = _pendingPackets.Count;
                for(int i = 0; i < availablePackets; i++)
                {
                    Packet p = _pendingPackets.Dequeue();
                    if (!HandlePacket(ref p))
                        PacketBufferPool.ReturnBuffer(p.Buffer);
                }
            }
        }

        private uint _lastAckSequence = 32;//random pro zvýšení bezpečnosti? nebo přidat session ID
        private uint _lastAckBits = uint.MaxValue;//možný problém v přístupu z více vláken ? (recive -> ConnectionManager + send -> ServerManager)

        private bool HandlePacket(ref Packet encryptedPacket)//decrypt & ověření
        {
            Packet? p = _aead.Decrypt(ref encryptedPacket);
            if (!p.HasValue)
                return false;

            Packet packet = p.Value;

            LastReceive = DateTime.UtcNow;

            if(packet.Type == PacketType.ConnectionHandshakeResponse)
            {
                var tempPacket = new Packet(PacketType.ConnectionAccepted);
                tempPacket.Send(ActiveSocket, Remote);

                PacketBufferPool.ReturnBuffer(tempPacket.Buffer);
                return false;
            }

            uint sequence = packet.Sequence;//nastavit ack bits
            uint ackSequence = packet.AckSequence;
            uint ackBits = packet.AckBits;

            for (int i = 0; i < 32; i++)//check acked bits - performance? nejaky zpusob jak najit pozice jenom 0 ?
            {
                //if(ackBits.GetBit(i))
                //{
                //    //get sequence num & call OnPacketAcked event ?
                //}
                if (!ackBits.GetBit(i))//unacked packet
                {
                    uint unackedSequence = ackSequence - (uint)(i + 1);
                    Packet? p1 = GetUnackedPacket(unackedSequence);
                    if (p1.HasValue)
                    {
                        var unackedPacket = p1.Value;
                        unackedPacket = new Packet(unackedPacket.Type, unackedPacket.Sequence, _lastAckSequence, _lastAckBits, unackedPacket.Buffer);//gc ? musí se smazat starý, už nepoužívaný Packet struct

                        unackedPacket.Send(ActiveSocket, Remote);
                    }
                }
            }

            //lock _lastAckSequence + _lastAckBits ?

            if (sequence == _lastAckSequence)//prijde uz znamy packet
                return false;

            if (sequence > _lastAckSequence)//new seq
            {
                uint difference = sequence - _lastAckSequence;
                if (difference > 32)
                {
                    _lastAckBits = uint.MaxValue;//nastavit všechny na 1 protože neznáme přesné sequence numbers
                    //poslat info o tom že nedorazilo víc jak 32 packetů ? nebo rovnou ukončit spojení a nastavit nové ?
                }
                else
                {
                    if (difference < 32)
                    {
                        _lastAckBits.ShiftLeft(difference);
                    }
                    else
                    {
                        _lastAckBits = uint.MinValue;
                    }

                    _lastAckBits.SetBit(difference - 1, true);
                }

                _lastAckSequence = sequence;//nebezpečí že dosáhne vysokých hodnot a nebo nějaký blbeček nastaví max hodnotu -> reset na 0 což zkurví celou logiku

                _receivedData.Enqueue(packet.Buffer);
                return true;
            }
            else//old seq -> update ack bits -> nedělat nic pokud je novější packet důležitější (player movement destination)
            {
                uint difference = _lastAckSequence - sequence;
                if (difference > 32)
                    return false;//neznámo přesné sequence number -> když přidám zprávu může tam být 2x

                if (!_lastAckBits.GetBit(difference - 1))//unacked packet arrived
                {
                    _lastAckBits.SetBit(difference - 1, true);

                    _receivedData.Enqueue(packet.Buffer);
                    return true;
                }
            }

            return false;
        }

        //list partial data
        private ConcurrentQueue<PacketBuffer> _receivedData = new ConcurrentQueue<PacketBuffer>();//nebo klidně list ?
        public PacketBuffer ReadPacketData()//handle partial messages
        {
            PacketBuffer buffer;
            _receivedData.TryDequeue(out buffer);

            return buffer;//nebo vrátit NetworkMessageReader ?
        }

        public NetworkMessageWriter BeginWrite(short messageID)
        {
            return new NetworkMessageWriter(messageID, PayloadMTU);
        }
        public void EndWrite(ref NetworkMessageWriter writer)
        {
            _pendingMessages.Add(writer.GetBuffer());
        }

        private List<PacketBuffer> _pendingMessages = new List<PacketBuffer>();
        public void SendPendingMessages()
        {
            //lock _sendQueue ?
            //set ack sequence + ack bits

            int queueLength = _pendingMessages.Count;
            if (queueLength == 0)
            {
                //send empty packet -> spíše nějaký ping
                SendPacket(null);
                return;
            }

            _pendingMessages.Sort((x, y) => x.DataLength.CompareTo(y.DataLength));

            //send partial messages -> v příjmu je pozdržet dokud nepřijdou všechny -> poté spojit a přidat do message queue

            var buffer = PacketBufferPool.GetBuffer();

            while (_pendingMessages.Count > 0)
            {
                PacketBuffer message = _pendingMessages[0];

                if (buffer.DataLength + message.DataLength > PayloadMTU)
                    throw new Exception();

                if (buffer.DataLength + message.DataLength <= PayloadMTU)
                {
                    message.CopyTo(0, message.DataLength, buffer, buffer.DataLength);

                    _pendingMessages.RemoveAt(0);
                    PacketBufferPool.ReturnBuffer(message);
                }
                else
                {
                    SendPacket(buffer);
                    buffer.DataLength = 0;
                }
            }

            if (buffer.DataLength > 0)
                SendPacket(buffer);

            PacketBufferPool.ReturnBuffer(buffer);
        }

        private uint _lastSentSequence = 32;//random kvůli bezpečnosti? nebo přidat session ID

        private const int SENT_PACKETS_BUFFER_SIZE = 64;//mozna zvysit?
        private Packet[] _sentPackets = new Packet[SENT_PACKETS_BUFFER_SIZE];

        private void SendPacket(PacketBuffer buffer)
        {
            var rawPacket = new Packet(PacketType.Message, ++_lastSentSequence, _lastAckSequence, _lastAckBits, buffer);//lock _lastSentSequence, _lastAckSequence....

            int sentPacketsBufferIndex = (int)(rawPacket.Sequence % SENT_PACKETS_BUFFER_SIZE);
            ref Packet oldPacket = ref _sentPackets[sentPacketsBufferIndex];
            PacketBufferPool.ReturnBuffer(oldPacket.Buffer);

            Packet? p = _aead.Encrypt(ref rawPacket);
            if(p.HasValue)
            {
                var encryptedPacket = p.Value;

                _sentPackets[sentPacketsBufferIndex] = encryptedPacket;
                encryptedPacket.Send(ActiveSocket, Remote);
            }
        }

        private Packet? GetUnackedPacket(uint sequence)
        {
            int index = (int)(sequence % SENT_PACKETS_BUFFER_SIZE);
            ref Packet packet = ref _sentPackets[index];
            if (packet.Sequence == sequence)
                return _sentPackets[index];

            return null;
        }

        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}

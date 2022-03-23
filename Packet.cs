using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using DarkGalaxy_Networking.Helpers;

namespace DarkGalaxy_Networking
{
    public readonly struct Packet//možná udělat class -> při receive se zbytečně kopíruje (do queue a z queue)!
    {
        public const int MAX_RAW_MTU = 1500;
        public const int MAX_MTU = MAX_RAW_MTU - 20 - 8;//20 bytes IP header, 8 bytes UDP header
        public const int MIN_MTU = 576 - 20 - 8;//20 bytes IP header, 8 bytes UDP header

        public const int HEADER_SIZE = 2 + 1 + 4 + 4 + 4;

        private const ushort PROTOCOL_ID = 46187;//možná spíše Session ID ?
        public readonly PacketType Type;
        public readonly uint Sequence;//mozna ushort
        public readonly uint AckSequence;//ushort
        public readonly uint AckBits;

        public readonly PacketBuffer Buffer;

        //header elements -> get; set; -> nastavit přímo do bufferu

        //public Action AckAction;//když dorazí zpátky Ack pro tento packet -> delta encoding | nějak vyřešit více akcí -> delta encoding pro více dat
        //nemůžu použít protože vyruší Struct generation on stack ?

        public Packet(PacketType type) : this(type, 0, 0, 0, PacketBufferPool.GetBuffer()) { }//ten buffer je potom k ničemu (když neni už v ctr tak neni potřeba)
        public Packet(PacketType type, PacketBuffer buffer) : this(type, 0, 0, 0, buffer) { }
        public Packet(PacketType type, uint sequence, uint ackSequence, uint ackBits) : this(type, sequence, ackSequence, ackBits, PacketBufferPool.GetBuffer()) { }
        public Packet(PacketType type, uint sequence, uint ackSequence, uint ackBits, PacketBuffer buffer)
        {
            Type = type;
            Sequence = sequence;
            AckSequence = ackSequence;
            AckBits = ackBits;

            Buffer = buffer;
        }

        public void CopyHeader(PacketBuffer packetBuffer)//lock ?
        {
            byte[] buffer = packetBuffer.Buffer;
            int offset = 0;

            BinaryHelper.Write(PROTOCOL_ID, buffer, ref offset);

            BinaryHelper.Write((byte)Type, buffer, ref offset);

            BinaryHelper.Write(Sequence, buffer, ref offset);
            BinaryHelper.Write(AckSequence, buffer, ref offset);
            BinaryHelper.Write(AckBits, buffer, ref offset);
        }

        public void Send(Socket socket, EndPoint endpoint)//byte order ?
        {
            CopyHeader(Buffer);

            socket.SendTo(Buffer.Buffer, 0, HEADER_SIZE + Buffer.DataLength, SocketFlags.None, endpoint);//lock ?, async ?

            Console.WriteLine("send packet length: " + HEADER_SIZE + Buffer.DataLength);
        }

        public static Packet? Parse(PacketBuffer packetBuffer)//byte order
        {
            byte[] buffer = packetBuffer.Buffer;
            int offset = 0;

            ushort protocolID = BinaryHelper.ReadUShort(buffer, ref offset);
            if (protocolID != PROTOCOL_ID)
                return null;

            PacketType type = (PacketType)BinaryHelper.ReadByte(buffer, ref offset);
            if (type == PacketType.Unknown)
                return null;

            uint sequence = BinaryHelper.ReadUInt(buffer, ref offset);
            uint ackSequence = BinaryHelper.ReadUInt(buffer, ref offset);
            uint ackBits = BinaryHelper.ReadUInt(buffer, ref offset);

            return new Packet(type, sequence, ackSequence, ackBits, packetBuffer);
        }
    }

    public enum PacketType : byte
    {
        Unknown,

        //klient požádá o připojení -> obsahuje crypto key, crypto IV, auth key + verification code (náhodná sequence encryptovaná veřejným klíčem)
        //server žádá o ověření -> obsahuje encryptované data pomocí předchozích info (verifikační sekvence dat co poslal klient encryptovaný veřejným klíčem, a také obsahuje další náhodnou sekvenci dat pro ověření úspěšného nastavení na klientovi straně) => max počet pokusů
        //klient odpoví o srozumění akceptovaného spojení -> obsahuje encryptované data pomocí crypto info (verifikační sekvence co poslal server) => max počet pokusů
        //server odpoví o spojení nastaveno a akceptováno => může se ztratit !

        //a nebo server spojení zamítne

        ConnectionRequest,

        ConnectionHandshakeRequest,
        ConnectionHandshakeResponse,

        ConnectionAccepted,
        ConnectionDenied,

        Disconnect,

        Ping,
        Pong,

        Message,
        PartialMessage
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace DarkGalaxy_Networking
{
    public class PacketBuffer
    {
        public const int MAX_DATA_LENGTH = Packet.MAX_MTU - Packet.HEADER_SIZE;

        public const int DATA_OFFSET = Packet.HEADER_SIZE;

        public readonly byte[] Buffer;//nebo span<byte> ?
        public int DataLength;

        public PacketBuffer()
        {
            Buffer = new byte[MAX_DATA_LENGTH + Packet.HEADER_SIZE];
            DataLength = 0;
        }

        /// <summary>
        /// Přidá určený počet bytes do tohoto bufferu a zvedne DataLength
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool Write(byte[] srcBuffer, int srcOffset, int length)
        {
            if (DataLength + length > MAX_DATA_LENGTH)
                return false;

            System.Buffer.BlockCopy(srcBuffer, srcOffset, Buffer, DATA_OFFSET + DataLength, length);
            DataLength += length;
            return true;
        }

        public bool CopyTo(int offset, int length, PacketBuffer dstBuffer, int dstOffset)
        {
            if (offset + length > DataLength || dstOffset + length > MAX_DATA_LENGTH)
                return false;

            System.Buffer.BlockCopy(Buffer, DATA_OFFSET + offset, dstBuffer.Buffer, DATA_OFFSET + dstOffset, length);
            dstBuffer.DataLength = dstOffset + length;
            return true;
        }
        public bool CopyTo(int offset, int length, byte[] dstBuffer, int dstOffset)
        {
            if (offset + length > DataLength)
                return false;

            System.Buffer.BlockCopy(Buffer, DATA_OFFSET + offset, dstBuffer, dstOffset, length);
            return true;
        }

        //public bool SequenceEqual(byte[] buffer,...);
        public bool SequenceEqual(int offset, int length, PacketBuffer buffer)//dodělat
        {
            return false;
        }
        public bool IsEqual(int offset, int length, PacketBuffer buffer)//dodělat
        {
            return false;
        }

        //public byte this[int index]
        //{
        //    get
        //    {

        //    }
        //    set
        //    {

        //    }
        //}
    }
}

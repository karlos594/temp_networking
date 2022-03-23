using System;
using System.Collections.Generic;
using System.Text;
using DarkGalaxy_Networking.Helpers;

namespace DarkGalaxy_Networking
{
    public ref struct NetworkMessageReader//full packet buffer ! né jen jedna zpráva
    {
        private readonly PacketBuffer _buffer;

        public NetworkMessageReader(PacketBuffer buffer)
        {
            _buffer = buffer;

            offset = 0;
        }

        private int offset;

        public void Read(byte[] dstBuffer, int dstOffset, int length)
        {
            if (offset + length > _buffer.DataLength)
                throw new Exception();//return default value ?

            Buffer.BlockCopy(_buffer.Buffer, offset, dstBuffer, dstOffset, length);
            offset += length;
        }
        public byte[] Read(int length)
        {
            if (offset + length > _buffer.DataLength)
                throw new Exception();//return default value ?

            var data = new byte[length];
            Buffer.BlockCopy(_buffer.Buffer, offset, data, 0, length);
            offset += length;

            return data;
        }
        public void Read(out bool value)
        {
            if (offset + 1 > _buffer.DataLength)
                throw new Exception();//return default value ?

            value = BinaryHelper.ReadBool(_buffer.Buffer, ref offset);
        }
        public void Read(out byte value)
        {
            if (offset + 1 > _buffer.DataLength)
                throw new Exception();

            value = BinaryHelper.ReadByte(_buffer.Buffer, ref offset);
        }
        public void Read(out short value)
        {
            if (offset + 2 > _buffer.DataLength)
                throw new Exception();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            value = BinaryHelper.ReadShort(_buffer.Buffer, ref offset);
        }
        public void Read(out ushort value)
        {
            if (offset + 2 > _buffer.DataLength)
                throw new Exception();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            value = BinaryHelper.ReadUShort(_buffer.Buffer, ref offset);
        }
        public void Read(out int value)
        {
            if (offset + 4 > _buffer.DataLength)
                throw new Exception();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            value = BinaryHelper.ReadInt(_buffer.Buffer, ref offset);
        }
        public void Read(out uint value)
        {
            if (offset + 4 > _buffer.DataLength)
                throw new Exception();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            value = BinaryHelper.ReadUInt(_buffer.Buffer, ref offset);
        }
        public void Read(out float value)
        {
            if (offset + 4 > _buffer.DataLength)
                throw new Exception();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            value = BinaryHelper.ReadFloat(_buffer.Buffer, ref offset);
        }
        public void Read(out long value)
        {
            throw new NotImplementedException();
        }
        public void Read(out ulong value)
        {
            throw new NotImplementedException();
        }
        public void Read(out double value)
        {
            throw new NotImplementedException();
        }
    }
}

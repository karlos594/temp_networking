using System;
using System.Collections.Generic;
using System.Text;
using DarkGalaxy_Networking.Helpers;

namespace DarkGalaxy_Networking
{
    public ref struct NetworkMessageWriter
    {
        public readonly short MessageID;

        private readonly int _maxLength;

        private PacketBuffer _buffer;

        public NetworkMessageWriter(short messageID, int maxLength)
        {
            MessageID = messageID;

            _maxLength = maxLength - 2;//message ID + partial message struct length ?

            _buffer = PacketBufferPool.GetBuffer();

            Write(MessageID);
        }

        public void Write(byte[] srcBuffer, int srcOffset, int length)
        {
            if (_buffer.DataLength + length > _maxLength)
                throw new NotImplementedException();

            _buffer.Write(srcBuffer, srcOffset, length);
        }
        public void Write(in bool value)
        {
            if(_buffer.DataLength + 1 > _maxLength)
                throw new NotImplementedException();

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in byte value)
        {
            if (_buffer.DataLength + 1 > _maxLength)
                throw new NotImplementedException();

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in short value)
        {
            if (_buffer.DataLength + 2 > _maxLength)
                throw new NotImplementedException();

            if(!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in ushort value)
        {
            if (_buffer.DataLength + 2 > _maxLength)
                throw new NotImplementedException();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in int value)
        {
            if (_buffer.DataLength + 4 > _maxLength)
                throw new NotImplementedException();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in uint value)
        {
            if (_buffer.DataLength + 4 > _maxLength)
                throw new NotImplementedException();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in float value)
        {
            if (_buffer.DataLength + 4 > _maxLength)
                throw new NotImplementedException();

            if (!BinaryHelper.IsLittleEndian)
                throw new NotImplementedException();//just reverse array ?

            BinaryHelper.Write(value, _buffer.Buffer, ref _buffer.DataLength);
        }
        public void Write(in long value)
        {
            throw new NotImplementedException();
        }
        public void Write(in ulong value)
        {
            throw new NotImplementedException();
        }
        public void Write(in double value)
        {
            throw new NotImplementedException();
        }

        internal PacketBuffer GetBuffer()
        {
            return _buffer;
        }
    }
}

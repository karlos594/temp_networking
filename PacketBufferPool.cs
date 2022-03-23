using System;
using System.Collections.Generic;
using System.Text;

namespace DarkGalaxy_Networking
{
    public static class PacketBufferPool
    {
#if NETSTANDARD2_1
        //public class PacketBufferPool : ArrayPool<byte>
#else
        //
#endif

        //private static ConcurrentStack<PooledBuffer> _bufferStack = new ConcurrentStack<PooledBuffer>();
        private static Stack<PacketBuffer> _bufferStack = new Stack<PacketBuffer>();

        public static void Init(int initialSize = 0)
        {
            _bufferStack.Clear();

            for (int i = 0; i < initialSize; i++)
            {
                _bufferStack.Push(new PacketBuffer());
            }
        }

        private static void AddBuffer()
        {

        }

        public static PacketBuffer GetBuffer()
        {
            lock (_bufferStack)
            {
                if (_bufferStack.Count > 0)//heavy ? možná použít TryPop ?
                {
                    var buffer = _bufferStack.Pop();
                    buffer.DataLength = 0;
                    return buffer;
                }
                else
                    return new PacketBuffer();
            }
        }
        public static void ReturnBuffer(PacketBuffer buffer)
        {
            if (buffer == null)
                return;

            lock (_bufferStack)
            {
                if (!_bufferStack.Contains(buffer))//heavy ? možná použít ConcurrentBag ?
                {
                    _bufferStack.Push(buffer);
                }
            }
        }
    }
}

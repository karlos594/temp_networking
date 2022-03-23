using System;
using System.IO;
using System.Security.Cryptography;

namespace DarkGalaxy_Networking
{
    internal class PacketAEAD : IDisposable
    {
        public const int ENCRYPTED_OVERHEAD_SIZE = BLOCK_SIZE + AUTH_TAG_SIZE;//BLOCK_SIZE = Padding.PKCS7 (max = 1 block)

        private const int BLOCK_SIZE_IN_BITS = 128;
        private const int BLOCK_SIZE = BLOCK_SIZE_IN_BITS / 8;

        private const int KEY_SIZE_IN_BITS = 256;
        public const int KEY_SIZE = KEY_SIZE_IN_BITS / 8;

        public const int IV_SIZE = BLOCK_SIZE;

        private const int AUTH_TAG_SIZE = 16;//klidně zmenšit na 80 bitů, nebo dokonce na 32 bitů
        public const int AUTH_KEY_SIZE = 32;

        private ICryptoTransform _encryptor;
        private ICryptoTransform _decryptor;

        //private HMACSHA256 _hmac;
        private HMAC _hmac;//spíše použít AES ? (CBC-MAC) => single block, no IV, CBC mode (ECB -> previous block XOR current block), encryption only
        //private byte[] _hmacBuffer = new byte[32];

        public bool IsDisposed => _encryptor == null && _decryptor == null && _hmac == null;

        public PacketAEAD(byte[] key, byte[] iv, byte[] authKey)//init -> poslat jen jeden klíč a poté Sha256 => rozdělit na 2 poloviny (1 pro crypto key, 1 pro auth key)
        {
            //var aes = new AesCryptoServiceProvider();
            //var aes = Aes.Create();
            //var aes = new AesManaged();//protoze mono
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;//nebo ECB ? -> nebude potřeba IV -> může se nastavit na zeroes
            //aes.FeedbackSize = 128;

            aes.Key = key;//automaticky nastaví key-size
            aes.IV = iv;

            aes.Padding = PaddingMode.PKCS7;
            _encryptor = aes.CreateEncryptor();

            aes.Padding = PaddingMode.None;
            _decryptor = aes.CreateDecryptor();

            //_hmac = new HMACSHA256(authKey);
            //_hmac = new HMACMD5(authKey);
            _hmac = new HMACSHA1(authKey);
            //_hmacBuffer

            aes.Dispose();
            aes.Clear();
        }

        public Packet? Encrypt(ref Packet packet)
        {
            if (IsDisposed)
                return null;

            //safe check -> vypočítat potřebnou velikost output (Cipher + AuthTag)

            var rawPacketBuffer = packet.Buffer;
            if(rawPacketBuffer.DataLength > PacketBuffer.MAX_DATA_LENGTH)
            {
                //throw error ?
                return null;
            }

            var encryptedPacketBuffer = PacketBufferPool.GetBuffer();

            int encryptedLength = EncryptTransform(rawPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET, rawPacketBuffer.DataLength, encryptedPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET);
            encryptedPacketBuffer.DataLength = encryptedLength;

            packet.CopyHeader(encryptedPacketBuffer);

            byte[] authTag;
            lock (_hmac)
                authTag = _hmac.ComputeHash(encryptedPacketBuffer.Buffer, 0, PacketBuffer.DATA_OFFSET + encryptedPacketBuffer.DataLength);

            Buffer.BlockCopy(authTag, 0, encryptedPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET + encryptedPacketBuffer.DataLength, AUTH_TAG_SIZE);//tag.Length => Sha256 = 32 bytes -> polovina 16 bytes
            encryptedPacketBuffer.DataLength += AUTH_TAG_SIZE;

            return new Packet(packet.Type, packet.Sequence, packet.AckSequence, packet.AckBits, encryptedPacketBuffer);
        }
        public Packet? Decrypt(ref Packet packet)
        {
            if (IsDisposed)
                return null;

            //safe check -> potřeba Header + Cipher + AuthTag
            //if < PacketHeader.SIZE + AUTH_TAG_SIZE
            //return null;

            var encryptedPacketBuffer = packet.Buffer;

            byte[] calculatedAuthTag;
            lock (_hmac)
                calculatedAuthTag = _hmac.ComputeHash(encryptedPacketBuffer.Buffer, 0, PacketBuffer.DATA_OFFSET + encryptedPacketBuffer.DataLength - AUTH_TAG_SIZE);//TryComputeHash ?

            int authTagOffset = PacketBuffer.DATA_OFFSET + (encryptedPacketBuffer.DataLength - AUTH_TAG_SIZE);
            if (!SequenceEqual(encryptedPacketBuffer.Buffer, authTagOffset, AUTH_TAG_SIZE, calculatedAuthTag, 0))
                return null;

            var decryptedPacketBuffer = PacketBufferPool.GetBuffer();

            int decryptedLength = DecryptTransform(encryptedPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET, encryptedPacketBuffer.DataLength - AUTH_TAG_SIZE, decryptedPacketBuffer.Buffer, PacketBuffer.DATA_OFFSET);
            decryptedPacketBuffer.DataLength = decryptedLength;

            return new Packet(packet.Type, packet.Sequence, packet.AckSequence, packet.AckBits, decryptedPacketBuffer);
        }

        private int EncryptTransform(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset)
        {
            lock (_encryptor)
            {
                int blockCount = inputLength / BLOCK_SIZE;

                int transformed = 0;
                if (_encryptor.CanTransformMultipleBlocks)
                {
                    if (blockCount > 0)
                    {
                        transformed += _encryptor.TransformBlock(input, inputOffset, blockCount * BLOCK_SIZE, output, outputOffset);
                    }
                }
                else
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        transformed += _encryptor.TransformBlock(input, inputOffset + (i * BLOCK_SIZE), BLOCK_SIZE, output, outputOffset + transformed);
                    }
                }

                byte[] remained = _encryptor.TransformFinalBlock(input, inputOffset + transformed, inputLength - transformed);
                if (remained != null)
                {
                    int remainedLength = remained.Length;
                    if (remainedLength > 0)
                    {
                        Buffer.BlockCopy(remained, 0, output, outputOffset + transformed, remainedLength);
                        transformed += remainedLength;
                    }
                }

                return transformed;
            }
        }
        private int DecryptTransform(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset)
        {
            if (inputLength % BLOCK_SIZE != 0)//při decrypt je vždy velikost dělitelná 16 (encrypt padding == PKCS)
                return 0;

            lock (_decryptor)
            {
                int blockCount = inputLength / BLOCK_SIZE;

                int transformed = 0;
                if (_decryptor.CanTransformMultipleBlocks)
                {
                    if (blockCount > 0)
                    {
                        transformed += _decryptor.TransformBlock(input, inputOffset, blockCount * BLOCK_SIZE, output, outputOffset);
                    }
                }
                else
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        transformed += _decryptor.TransformBlock(input, inputOffset + (i * BLOCK_SIZE), BLOCK_SIZE, output, outputOffset + transformed);
                    }
                }

                _decryptor.TransformFinalBlock(input, 0, 0);//reset previous blocks (CBC mode) -> možná dát před decrypt transform ?

                if (transformed != inputLength)//decrypt -> transform all block (padding == none)
                    return 0;

                var paddingLength = output[transformed - 1];//poslední byte == padding length
                if (paddingLength == 0 || paddingLength > 16)//padding length vždy > 0 && max padding <= 16 ?
                    return 0;

                return transformed - paddingLength;
            }
        }

        public static bool SequenceEqual(byte[] a1, int a1Offset, int a1Length, byte[] a2, int a2Offset)
        {
            for (int i = 0; i < a1Length; i++)
            {
                if (a1[a1Offset + i] != a2[a2Offset + i])
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            if (_encryptor != null)
            {
                lock (_encryptor)
                    _encryptor.Dispose();

                _encryptor = null;
            }
            if (_decryptor != null)
            {
                lock (_decryptor)
                    _decryptor.Dispose();

                _decryptor = null;
            }
            if (_hmac != null)
            {
                lock (_hmac)
                    _hmac.Dispose();

                _hmac = null;
                //_hmacBuffer = null;
            }
        }
    }
}

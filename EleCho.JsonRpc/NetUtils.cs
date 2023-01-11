using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EleCho.JsonRpc
{
    static class NetUtils
    {
        private static readonly JsonSerializerOptions RpcJsonSerializerOptions =
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

#if NET6_0_OR_GREATER
        public static void ReadBlock(Stream stream, Span<byte> block)
        {
            while (!block.IsEmpty)
            {
                int read = stream.Read(block);
                if (read == 0)
                    throw new EndOfStreamException();

                block = block[read..];
            }
        }
#elif NET462_OR_GREATER
        public static void ReadBlock(Stream stream, byte[] block)
        {
            int offset = 0;
            while (offset < block.Length)
                offset += stream.Read(block, offset, block.Length - offset);
        }
#endif

        public static unsafe void WriteMessage(this Stream stream, byte[] buffer, int offset, int count)
        {
#if NET6_0_OR_GREATER
            int countCopy = count;
            Span<byte> head = new Span<byte>((byte*)&countCopy, sizeof(int));
            if (BitConverter.IsLittleEndian)
                head.Reverse();                                             // 这样?

            stream.Write(head);
#elif NET462_OR_GREATER
            byte[] head = BitConverter.GetBytes(count);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(head);

            stream.Write(head, 0, 4);
#else
            throw new NotImplementedException();
#endif
            stream.Write(buffer, offset, count);
        }

        public static byte[] ReadMessage(this Stream stream)
        {
#if NET6_0_OR_GREATER
            Span<byte> head = stackalloc byte[sizeof(int)];
            ReadBlock(stream, head);

            int bodyLen = Unsafe.As<byte, int>(ref MemoryMarshal.GetReference(head));
#elif NET462_OR_GREATER
            byte[] head = new byte[sizeof(int)];
            ReadBlock(stream, head);

            int bodyLen = BitConverter.ToInt32(head, 0);
#else
            throw new NotImplementedException();
#endif
            if (BitConverter.IsLittleEndian)
                bodyLen = IPAddress.NetworkToHostOrder(bodyLen);

            byte[] body = new byte[bodyLen];
            ReadBlock(stream, body);

            return body;
        }

        public static void WriteJsonMessage<T>(this Stream stream, T obj)
        {
            MemoryStream ms = new MemoryStream();
            JsonSerializer.Serialize(ms, obj, RpcJsonSerializerOptions);

            byte[] body = ms.GetBuffer();
            stream.WriteMessage(body, 0, (int)ms.Length);
        }

        public static T? ReadJsonMessage<T>(this Stream stream)
        {
            byte[] body = stream.ReadMessage();
            return JsonSerializer.Deserialize<T>(body, RpcJsonSerializerOptions);
        }
    }
}
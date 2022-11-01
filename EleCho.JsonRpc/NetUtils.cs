using System.Text.Json;
using System.Text.Json.Nodes;

namespace EleCho.JsonRpc
{
    static class NetUtils
    {
        public static void ReadBlock(Stream stream, byte[] block)
        {
            int offset = 0;
            while (offset < block.Length)
                offset += stream.Read(block, offset, block.Length - offset);
        }

        public static void WriteMessage(this Stream stream, byte[] buffer, int offset, int count)
        {
            byte[] head = BitConverter.GetBytes(count);
            stream.Write(head, 0, 4);
            stream.Write(buffer, offset, count);
        }

        public static byte[] ReadMessage(this Stream stream)
        {
            byte[] head = new byte[4];
            ReadBlock(stream, head);

            int bodyLen = BitConverter.ToInt32(head);
            byte[] body = new byte[bodyLen];
            ReadBlock(stream, body);

            return body;
        }

        public static void WriteJsonMessage<T>(this Stream stream, T obj)
        {
            MemoryStream ms = new MemoryStream();
            JsonSerializer.Serialize(ms, obj);

            byte[] body = ms.GetBuffer();
            stream.WriteMessage(body, 0, (int)ms.Length);
        }

        public static T? ReadJsonMessage<T>(this Stream stream)
        {
            byte[] body = stream.ReadMessage();
            return JsonSerializer.Deserialize<T>(body);
        }
    }
}
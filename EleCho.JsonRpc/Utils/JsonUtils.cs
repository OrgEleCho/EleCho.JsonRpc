using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EleCho.JsonRpc.Utils
{
    internal static class JsonUtils
    {
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            Converters =
            {
                RpcPackageConverter.Instance,
                RpcPackageIdConverter.Instance,
                new JsonStringEnumConverter(),
            }
        };

        public class RpcPackageConverter : JsonConverter<RpcPackage>
        {
            public RpcPackageConverter()
            { }

            public static RpcPackageConverter Instance { get; } = new RpcPackageConverter();

            public override RpcPackage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                JsonDocument doc =
                    JsonDocument.ParseValue(ref reader);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                if (doc.RootElement.TryGetProperty("method", out _))
                    return JsonSerializer.Deserialize<RpcRequest>(doc, options);
                else if (doc.RootElement.TryGetProperty("error", out _))
                    return JsonSerializer.Deserialize<RpcErrorResponse>(doc, options);
                else
                    return JsonSerializer.Deserialize<RpcResponse>(doc, options);
            }

            public override void Write(Utf8JsonWriter writer, RpcPackage value, JsonSerializerOptions options)
            {
                if (value is not null)
                    JsonSerializer.Serialize(writer, value, value.GetType(), options);
                else
                    writer.WriteNullValue();
            }
        }

        public class RpcPackageIdConverter : JsonConverter<RpcPackageId>
        {
            public static RpcPackageIdConverter Instance { get; } = new RpcPackageIdConverter();

            public override RpcPackageId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                    return RpcPackageId.Create(reader.GetString()!);
                else if (reader.TokenType == JsonTokenType.Number)
                    return RpcPackageId.Create(reader.GetInt32());
                else
                    throw new JsonException();
            }

            public override void Write(Utf8JsonWriter writer, RpcPackageId value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value.Value, value.Value.GetType(), options);
            }
        }
    }
}

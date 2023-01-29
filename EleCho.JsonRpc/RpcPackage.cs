using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EleCho.JsonRpc
{
    public enum RpcPackageKind
    {
        Req = 1, Resp = 2
    }
    
    public abstract class RpcPackage
    {
        public abstract RpcPackageKind Kind { get; }
    }

    public class RpcRequest : RpcPackage
    {
        [JsonConstructor]
        public RpcRequest(string method, object?[]? arg)
        {
            Method = method;
            Arg = arg;
        }

        public string Method { get; }
        public object?[]? Arg { get; }

        [JsonInclude]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public override RpcPackageKind Kind => RpcPackageKind.Req;
    }

    public class RpcResponse : RpcPackage
    {
        [JsonConstructor]
        public RpcResponse(object? ret, object?[]? refRet, string? err)
        {
            Ret = ret;
            RefRet = refRet;
            Err = err;
        }

        public object? Ret { get; }
        public object?[]? RefRet { get; }
        public string? Err { get; }

        [JsonInclude]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public override RpcPackageKind Kind => RpcPackageKind.Resp;
    }

    public class RpcPackageConverter : JsonConverter<RpcPackage>
    {
        public override RpcPackage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            JsonDocument doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Cannot deserialize from {doc.RootElement.ValueKind}");
            if (!doc.RootElement.TryGetProperty(nameof(RpcPackage.Kind), out JsonElement eleKind))
                throw new JsonException("There isn't an element named 'Kind' in the Object");
            
            RpcPackageKind kind = 
                eleKind.Deserialize<RpcPackageKind>(options);
            return kind switch
            {
                RpcPackageKind.Req => JsonSerializer.Deserialize<RpcRequest>(doc, options),
                RpcPackageKind.Resp => JsonSerializer.Deserialize<RpcResponse>(doc, options),

                _ => throw new JsonException("Not a valid RpcPackage")
            };
        }

        public override void Write(Utf8JsonWriter writer, RpcPackage value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                JsonSerializer.Serialize(value, value.GetType(), options);
        }
    }
}
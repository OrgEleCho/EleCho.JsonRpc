using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using EleCho.JsonRpc.Utils;

namespace EleCho.JsonRpc
{
    internal abstract record class RpcPackage
    {
        [JsonInclude]
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc => "2.0";
    }

    internal record struct RpcPackageId
    {
        private RpcPackageId(object value)
        {
            Value = value;
        }

        public object Value { get; }

        public static RpcPackageId? CreateOrNull(object? id)
        {
            if (id == null)
                return null;

            return Create(id);
        }

        public static RpcPackageId Create(object id)
        {
            if (id is string strId)
                return Create(strId);
            else if (id is int intId)
                return Create(intId);
            else
                throw new ArgumentException("Invalid type of id", nameof(id));
        }

        public static RpcPackageId Create(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Empty value", nameof(id));

            return new RpcPackageId(id);
        }

        public static RpcPackageId Create(int id)
        {
            return new RpcPackageId(id);
        }
    }

    internal record class RpcRequest : RpcPackage
    {
        [JsonConstructor]
        public RpcRequest(string method, object?[]? args, string? signature, RpcPackageId? id)
        {
            Method = method;
            Args = args;
            Signature = signature;
            Id = id;
        }

        [JsonPropertyName("method")]
        public string Method { get; }

        [JsonPropertyName("params")]
        public object?[]? Args { get; }

        [JsonPropertyName("signature")]
        public string? Signature { get; }

        [JsonPropertyName("id")]
        public RpcPackageId? Id { get; }
    }

    internal record class RpcResponse : RpcPackage
    {
        [JsonConstructor]
        public RpcResponse(object? result, object?[]? refResults, RpcPackageId id)
        {
            Result = result;
            RefResults = refResults;
            Id = id;
        }

        [JsonPropertyName("result")]
        public object? Result { get; }

        [JsonPropertyName("ref_results")]
        public object?[]? RefResults { get; }


        [JsonPropertyName("id")]
        public RpcPackageId Id { get; }
    }

    internal record class RpcErrorResponse : RpcPackage
    {
        [JsonConstructor]
        public RpcErrorResponse(RpcError error, RpcPackageId id)
        {
            Error = error;
            Id = id;
        }

        [JsonPropertyName("error")]
        public RpcError Error { get; }


        [JsonPropertyName("id")]
        public RpcPackageId Id { get; }
    }

    internal struct RpcError
    {
        [JsonConstructor]
        public RpcError(int code, string message, object? data)
        {
            Code = code;
            Message = message;

            Data = data;
        }

        public RpcError(RpcErrorCode code, string message, object? data) :
            this((int)code, message, data)
        { }


        [JsonPropertyName("code")]
        public int Code { get; }

        [JsonPropertyName("message")]
        public string Message { get; }

        [JsonPropertyName("data")]
        public object? Data { get; }


        [JsonIgnore]
        public bool IsParseError =>
            Code == (int)RpcErrorCode.ParseError;

        [JsonIgnore]
        public bool IsInvalidRequest =>
            Code == (int)RpcErrorCode.InvalidRequest;

        [JsonIgnore]
        public bool IsMethodNotFound =>
            Code == (int)RpcErrorCode.MethodNotFound;

        [JsonIgnore]
        public bool IsInvalidParams =>
            Code == (int)RpcErrorCode.InvalidParams;

        [JsonIgnore]
        public bool IsInternalError =>
            Code == (int)RpcErrorCode.InternalError;

        [JsonIgnore]
        public bool IsServerError =>
            Code <= (int)RpcErrorCode.ServerErrorUpBound &&
            Code >= (int)RpcErrorCode.ServerErrorDownBound;
    }

    internal enum RpcErrorCode
    {
        ParseError           = -32700,
        InvalidRequest       = -32600,
        MethodNotFound       = -32601,
        InvalidParams        = -32602,
        InternalError        = -32603,
        ServerErrorUpBound   = -32000,
        ServerErrorDownBound = -32099
    }
}
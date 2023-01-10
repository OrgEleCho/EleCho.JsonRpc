namespace EleCho.JsonRpc
{
    public class RpcRequest
    {
        public RpcRequest(string method, object?[]? arg)
        {
            Method = method;
            Arg = arg;
        }

        public string Method { get; }
        public object?[]? Arg { get; }
    }

    public class RpcResponse
    {
        public RpcResponse(object? ret, object?[]? refRet, string? err)
        {
            Ret = ret;
            RefRet = refRet;
            Err = err;
        }

        public object? Ret { get; }
        public object?[]? RefRet { get; }
        public string? Err { get; }
    }
}
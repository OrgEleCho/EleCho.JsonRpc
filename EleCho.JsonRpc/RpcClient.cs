using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

#if NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
using EleCho.JsonRpc.Utils;
#endif

namespace EleCho.JsonRpc
{

    public interface IRpcClient<T> where T : class
    {
        internal object? ProcessInvocation(MethodInfo? targetMethod, object?[]? args);
    }


    public partial class RpcClient<T> : IRpcClient<T> where T : class
    {
        private readonly Stream send, recv;
        private readonly object invocationLock = new object();
        public T Remote { get; }

        public Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache = new();

        public RpcClient(Stream server) : this(server, server) { }
        public RpcClient(Stream send, Stream recv)
        {
            if (!typeof(T).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this.send = send;
            this.recv = recv;

            Remote = RpcUtils.CreateDynamicProxy(this);
        }

        RpcResponse? ReceiveResponse() =>
            recv.ReadJsonMessage<RpcResponse>();

        object? IRpcClient<T>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args) =>
            RpcUtils.ClientProcessInvocation(targetMethod, args, methodsCache, send, ReceiveResponse, invocationLock);
    }
}
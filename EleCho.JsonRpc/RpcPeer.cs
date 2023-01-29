using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

#if NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
using EleCho.JsonRpc.Utils;
#endif

namespace EleCho.JsonRpc
{
    public class RpcPeer<TAction, TImpl> : IRpcServer<TImpl>, IRpcClient<TAction>, IDisposable
        where TAction : class
        where TImpl : class
    {
        private readonly Stream send, recv;
        private readonly TImpl implInstance;
        private readonly object invocationLock = new object();

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> clientMethodsCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> serverMethodsCache = new();
        private readonly DataValueQueue<RpcResponse> rpcResponseQueue = new();

        private bool loop = true;

        public TAction Remote { get; }

        public RpcPeer(Stream anotherClient, TImpl implInstance) : this(anotherClient, anotherClient, implInstance) { }
        public RpcPeer(Stream send, Stream rect, TImpl implInstance)
        {
            if (!typeof(TAction).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this.send = send;
            this.recv = rect;
            this.implInstance = implInstance;

            Remote = RpcUtils.CreateDynamicProxy(this);
            Task.Run(MainLoop);
        }

        private void MainLoop()
        {
            while (loop)
            {
                try
                {
                    RpcPackage? pkg = recv.ReadJsonMessage<RpcPackage>();

                    if (pkg is RpcRequest req)
                    {
                        RpcResponse resp = RpcUtils.ServerProcessRequest(req, serverMethodsCache, implInstance);
                        send.WriteJsonMessage(resp);
                        send.Flush();
                    }
                    else if (pkg is RpcResponse resp)
                    {
                        rpcResponseQueue.Enqueue(resp);
                    }
                }
                catch
                {

                }
            }
        }

        RpcResponse? ReceiveResponse() => 
            rpcResponseQueue.Dequeue();

        object? IRpcClient<TAction>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args) =>
            RpcUtils.ClientProcessInvocation(targetMethod, args, clientMethodsCache, send, ReceiveResponse, invocationLock);

        RpcResponse IRpcServer<TImpl>.ProcessInvocation(RpcRequest request) =>
            RpcUtils.ServerProcessRequest(request, serverMethodsCache, implInstance);

        public void Dispose() => loop = false;
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

namespace EleCho.JsonRpc
{
    public interface IRpcServer<T> where T : class
    {
        internal RpcResponse ProcessInvocation(RpcRequest request);
    }

    public class RpcServer<T> : IRpcServer<T>, IDisposable where T : class
    {
        private bool loop = true;
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsCache =
            new Dictionary<string, (MethodInfo, ParameterInfo[])>();

        public readonly Stream send;
        public readonly Stream recv;
        public T Instance { get; }

        public void Dispose() => loop = false;

        public RpcServer(Stream client, T instance) : this(client, client, instance) { }
        public RpcServer(Stream send, Stream recv, T instance)
        {
            this.send = send;
            this.recv = recv;
            Instance = instance;

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
                        RpcResponse resp = RpcUtils.ServerProcessRequest(req, methodsCache, Instance);
                        send.WriteJsonMessage(resp);
                        send.Flush();
                    }
                }
                catch
                {
                    
                }
            }
        }

        RpcResponse IRpcServer<T>.ProcessInvocation(RpcRequest request) =>
            RpcUtils.ServerProcessRequest(request, methodsCache, Instance);
    }
}
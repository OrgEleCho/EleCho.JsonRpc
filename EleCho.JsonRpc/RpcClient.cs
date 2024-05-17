using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

#if NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif

namespace EleCho.JsonRpc
{

    public interface IRpcClient<T> where T : class
    {
        public T Remote { get; }
        internal object? ProcessInvocation(MethodInfo? targetMethod, object?[]? args);
        internal Task<object?> ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args);
    }


    public partial class RpcClient<T> : IRpcClient<T> where T : class
    {
        private readonly Stream send, recv;
        private readonly StreamWriter sendWriter;
        private readonly StreamReader recvReader;
        private readonly object writeLock = new object();
        private readonly object readLock = new object();

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache = new();
        private readonly Dictionary<object, RpcPackage> rpcResponseDict = new();
        private bool disposed = false;
        public T Remote { get; }
        public bool DisposeBaseStream { get; set; } = false;

        public RpcClient(Stream server) : this(server, server) { }
        public RpcClient(Stream send, Stream recv)
        {
            if (!typeof(T).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this.send = send;
            this.recv = recv;

            sendWriter = new StreamWriter(send) { AutoFlush = true };
            recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
            Task.Run(MainLoop);
        }

        private void MainLoop()
        {
            while (!disposed)
            {
                try
                {
                    if (!recvReader.ReadPackage(readLock, out RpcPackage? pkg))
                    {
                        Dispose();
                        return;
                    }
                    
                    if (pkg is RpcResponse resp)
                    {
                        rpcResponseDict[resp.Id] = resp;
                    }
                    else if (pkg is RpcErrorResponse errResp)
                    {
                        rpcResponseDict[errResp.Id] = errResp;
                    }
                }
                catch (IOException)
                {
                    Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        RpcPackage? ReceiveResponse(object id)
        {
            while (true)
            {
                if (rpcResponseDict.TryGetValue(id, out RpcPackage? r_pak))
                {
                    rpcResponseDict.Remove(id);
                    return r_pak;
                }
            }
        }

        async Task<RpcPackage?> ReceiveResponseAsync(object id)
        {
            while (true)
            {
                if (rpcResponseDict.TryGetValue(id, out RpcPackage? r_pak))
                {
                    rpcResponseDict.Remove(id);
                    return r_pak;
                }

                await Task.Delay(1);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (DisposeBaseStream)
            {
                send.Dispose();
                recv.Dispose();
                sendWriter.Dispose();
                recvReader.Dispose();
            }

            Disposed?.Invoke(this, EventArgs.Empty);
        }

        void EnsureNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("The RpcClient was disposed.");
        }

        object? IRpcClient<T>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocation(targetMethod, args, methodsCache, sendWriter, ReceiveResponse, writeLock);
        }

        Task<object?> IRpcClient<T>.ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocationAsync(targetMethod, args, methodsCache, sendWriter, ReceiveResponseAsync, writeLock);
        }

        public event EventHandler? Disposed;
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;
using System.Text.Json;

#if NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif

namespace EleCho.JsonRpc
{
    public class RpcPeer<TAction, TImpl> : IRpcServer<TImpl>, IRpcClient<TAction>, IDisposable
        where TAction : class
        where TImpl : class
    {
        private readonly Stream send, recv;
        private readonly StreamWriter sendWriter;
        private readonly StreamReader recvReader;

        private readonly TImpl implInstance;
        private readonly object writeLock = new object();
        private readonly object readLock = new object();

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> clientMethodsCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> serverMethodsNameCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> serverMethodsSignatureCache = new();
        private readonly Dictionary<object, RpcPackage> rpcResponseDict = new();

        private bool disposed = false;

        public TAction Remote { get; }
        public bool DisposeBaseStream { get; set; } = false;

        public RpcPeer(Stream anotherClient, TImpl implInstance) : this(anotherClient, anotherClient, implInstance) { }
        public RpcPeer(Stream send, Stream recv, TImpl implInstance)
        {
            if (!typeof(TAction).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this.send = send;
            this.recv = recv;
            this.implInstance = implInstance;

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

                    if (pkg is RpcRequest req)
                    {
                        RpcPackage? r_pak = RpcUtils.ServerProcessRequest(req, serverMethodsNameCache, serverMethodsSignatureCache, implInstance);

                        if (r_pak == null)
                            continue;

                        string r_json =
                            JsonSerializer.Serialize(r_pak, JsonUtils.Options);

                        sendWriter.WriteLine(r_json);
                    }
                    else if (pkg is RpcResponse resp)
                    {
                        rpcResponseDict[resp.Id] = resp;
                    }
                    else if (pkg is RpcErrorResponse errResp)
                    {
                        rpcResponseDict[errResp.Id] = errResp;
                    }
                }
                catch (JsonException ex)
                {
                    string r_json =
                        JsonSerializer.Serialize(
                            new RpcErrorResponse(
                                new RpcError(RpcErrorCode.ParseError, ex.Message, ex.Data),
                                SharedRandom.NextId()),
                            JsonUtils.Options);

                    sendWriter.WriteLine(r_json);
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
                if (rpcResponseDict.TryGetValue(id, out RpcPackage? resp))
                {
                    rpcResponseDict.Remove(id);
                    return resp;
                }
            }
        }

        async Task<RpcPackage?> ReceiveResponseAsync(object id)
        {
            while (true)
            {
                if (rpcResponseDict.TryGetValue(id, out RpcPackage? resp))
                {
                    rpcResponseDict.Remove(id);
                    return resp;
                }

                await Task.Delay(1);
            }
        }

        object? IRpcClient<TAction>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return 
                RpcUtils.ClientProcessInvocation(targetMethod, args, clientMethodsCache, sendWriter, ReceiveResponse, writeLock);
        }

        Task<object?> IRpcClient<TAction>.ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocationAsync(targetMethod, args, clientMethodsCache, sendWriter, ReceiveResponseAsync, writeLock);
        }

        RpcPackage? IRpcServer<TImpl>.ProcessInvocation(RpcRequest request)
        {
            EnsureNotDisposed();
            return 
                RpcUtils.ServerProcessRequest(request, serverMethodsNameCache, serverMethodsSignatureCache, implInstance);
        }

        Task<RpcPackage?> IRpcServer<TImpl>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, serverMethodsNameCache, serverMethodsSignatureCache, implInstance);
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
                throw new ObjectDisposedException("The RpcPeer was disposed.");
        }

        public event EventHandler? Disposed;
    }
}
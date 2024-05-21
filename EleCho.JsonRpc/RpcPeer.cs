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

        private readonly SemaphoreSlim writeLock = new(1, 1);
        private readonly SemaphoreSlim readLock = new(1, 1);

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> clientMethodsCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> serverMethodsNameCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> serverMethodsSignatureCache = new();
        private readonly Dictionary<object, RpcPackage> rpcResponseDict = new();

        private bool disposed = false;

        public TAction Remote { get; }
        public TImpl Implementation { get; }
        public bool DisposeBaseStream { get; set; } = false;

        public RpcPeer(Stream anotherClient, TImpl implInstance) : this(anotherClient, anotherClient, implInstance) { }
        public RpcPeer(Stream send, Stream recv, TImpl implInstance)
        {
            if (!typeof(TAction).IsInterface)
                throw new ArgumentException("Type must be an interface");
            if (implInstance is null)
                throw new ArgumentNullException(nameof(implInstance));

            this.send = send;
            this.recv = recv;

            sendWriter = new StreamWriter(send) { AutoFlush = true };
            recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
            Implementation = implInstance;
            Task.Run(MainLoop);
        }

        private async Task MainLoop()
        {
            while (!disposed)
            {
                try
                {
                    if ((await recvReader.ReadPackageAsync(readLock)) is not RpcPackage pkg)
                    {
                        Dispose();
                        return;
                    }

                    if (pkg is RpcRequest req)
                    {
                        RpcPackage? responsePackage = await RpcUtils.ServerProcessRequestAsync(req, serverMethodsNameCache, serverMethodsSignatureCache, Implementation);

                        if (responsePackage == null)
                            continue;

                        await sendWriter.WritePackageAsync(writeLock, responsePackage);
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
                    var errorPackage = new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ParseError, ex.Message, ex.Data),
                        SharedRandom.NextId());

                    await sendWriter.WritePackageAsync(writeLock, errorPackage);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    Dispose();
                }
                catch
                {

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
                RpcUtils.ServerProcessRequest(request, serverMethodsNameCache, serverMethodsSignatureCache, Implementation);
        }

        Task<RpcPackage?> IRpcServer<TImpl>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, serverMethodsNameCache, serverMethodsSignatureCache, Implementation);
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
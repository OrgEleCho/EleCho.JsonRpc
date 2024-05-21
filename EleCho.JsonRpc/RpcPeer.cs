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
        private readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> _clientMethodsCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _serverMethodsNameCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _serverMethodsSignatureCache = new();
        private readonly Dictionary<object, RpcPackage> _rpcResponseDict = new();


        private bool _disposed = false;


        public TAction Remote { get; }
        public TImpl Implementation { get; }

        public bool AllowConcurrentInvoking { get; set; } = true;
        public bool AllowParallelInvoking { get; set; } = false;
        public bool DisposeBaseStream { get; set; } = false;

        public RpcPeer(Stream anotherClient, TImpl implInstance) : this(anotherClient, anotherClient, implInstance) { }
        public RpcPeer(Stream send, Stream recv, TImpl implInstance)
        {
            if (!typeof(TAction).IsInterface)
                throw new ArgumentException("Type must be an interface");
            if (implInstance is null)
                throw new ArgumentNullException(nameof(implInstance));

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
            Implementation = implInstance;
            Task.Run(MainLoop);
        }

        private async Task MainLoop()
        {
            while (!_disposed)
            {
                try
                {
                    if ((await _recvReader.ReadPackageAsync(_readLock, _cancellationTokenSource.Token)) is not RpcPackage pkg)
                    {
                        Dispose();
                        return;
                    }

                    if (pkg is RpcRequest req)
                    {
                        var processAndRespondTask = AllowParallelInvoking ?
                            Task.Run(() => ProcessRequestAndRespondAsync(req)):
                            ProcessRequestAndRespondAsync(req);

                        if (!AllowConcurrentInvoking)
                            await processAndRespondTask;
                    }
                    else if (pkg is RpcResponse resp)
                    {
                        _rpcResponseDict[resp.Id] = resp;
                    }
                    else if (pkg is RpcErrorResponse errResp)
                    {
                        _rpcResponseDict[errResp.Id] = errResp;
                    }
                }
                catch (JsonException ex)
                {
                    var errorPackage = new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ParseError, ex.Message, ex.Data),
                        SharedRandom.NextId());

                    await _sendWriter.WritePackageAsync(_writeLock, errorPackage, _cancellationTokenSource.Token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    Dispose();
                }
                catch (IOException)
                {
                    Dispose();
                }
                catch
                {

                }
            }

            async Task ProcessRequestAndRespondAsync(RpcRequest requestPackage)
            {
                RpcPackage? r_pkg = await RpcUtils.ServerProcessRequestAsync(requestPackage, _serverMethodsNameCache, _serverMethodsSignatureCache, Implementation, _cancellationTokenSource.Token);

                if (r_pkg == null)
                    return;

                await _sendWriter.WritePackageAsync(_writeLock, r_pkg, _cancellationTokenSource.Token);
            }
        }

        RpcPackage? ReceiveResponse(object id)
        {
            while (true)
            {
                if (_rpcResponseDict.TryGetValue(id, out RpcPackage? resp))
                {
                    _rpcResponseDict.Remove(id);
                    return resp;
                }
            }
        }

        async Task<RpcPackage?> ReceiveResponseAsync(object id)
        {
            while (true)
            {
                if (_rpcResponseDict.TryGetValue(id, out RpcPackage? resp))
                {
                    _rpcResponseDict.Remove(id);
                    return resp;
                }

                await Task.Delay(1);
            }
        }

        object? IRpcClient<TAction>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return 
                RpcUtils.ClientProcessInvocation(targetMethod, args, _clientMethodsCache, _sendWriter, ReceiveResponse, _writeLock);
        }

        Task<object?> IRpcClient<TAction>.ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocationAsync(targetMethod, args, _clientMethodsCache, _sendWriter, ReceiveResponseAsync, _writeLock);
        }

        RpcPackage? IRpcServer<TImpl>.ProcessInvocation(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequest(request, _serverMethodsNameCache, _serverMethodsSignatureCache, Implementation);
        }

        Task<RpcPackage?> IRpcServer<TImpl>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, _serverMethodsNameCache, _serverMethodsSignatureCache, Implementation);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellationTokenSource.Cancel();

            if (DisposeBaseStream)
            {
                _send.Dispose();
                _recv.Dispose();
                _sendWriter.Dispose();
                _recvReader.Dispose();
            }

            Disposed?.Invoke(this, EventArgs.Empty);
        }

        void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("The RpcPeer was disposed.");
        }

        public event EventHandler? Disposed;
    }
}
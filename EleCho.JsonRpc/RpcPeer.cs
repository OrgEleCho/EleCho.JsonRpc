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
    /// <summary>
    /// JSON RPC Peer
    /// </summary>
    /// <typeparam name="TAction"></typeparam>
    /// <typeparam name="TImpl"></typeparam>
    public class RpcPeer<TAction, TImpl> : IRpcServer<TImpl>, IRpcClient<TAction>, IDisposable
        where TAction : class
        where TImpl : class, TAction
    {
        private readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly ConcurrentDictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> _clientMethodsCache = new();
        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _serverMethodsNameCache = new();
        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _serverMethodsSignatureCache = new();
        private readonly ConcurrentDictionary<object, RpcPackage> _rpcResponseDict = new();


        private bool _disposed = false;


        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public TAction Remote { get; }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public TImpl Implementation { get; }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public bool AllowConcurrentInvoking { get; set; } = true;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public bool AllowParallelInvoking { get; set; } = false;

        /// <summary>
        /// Whether dispose base streams while disposing current object
        /// </summary>
        public bool DisposeBaseStream { get; set; } = false;

        /// <summary>
        /// Create a new instance of JSON RPC Peer
        /// </summary>
        /// <param name="otherPeer">Network stream for communication with the other peer</param>
        /// <param name="implementation">Server side method implementations instance</param>
        public RpcPeer(Stream otherPeer, TImpl implementation) : this(otherPeer, otherPeer, implementation) { }

        /// <summary>
        /// Create a new instance of JSON RPC Peer
        /// </summary>
        /// <param name="send"></param>
        /// <param name="recv"></param>
        /// <param name="implementation"></param>
        /// <exception cref="ArgumentException">The <typeparamref name="TAction"/> is not interface</exception>
        /// <exception cref="ArgumentNullException">Some parameter is null</exception>
        public RpcPeer(Stream send, Stream recv, TImpl implementation)
        {
            if (!typeof(TAction).IsInterface)
                throw new ArgumentException("Type must be an interface");
            if (send is null)
                throw new ArgumentNullException(nameof(send));
            if (recv is null)
                throw new ArgumentNullException(nameof(recv));
            if (implementation is null)
                throw new ArgumentNullException(nameof(implementation));

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
            Implementation = implementation;
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
                    // ignore
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
                    _rpcResponseDict.TryRemove(id, out _);
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
                    _rpcResponseDict.TryRemove(id, out _);
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
                RpcUtils.ClientProcessInvocationAsync(targetMethod, args, _clientMethodsCache, _sendWriter, ReceiveResponseAsync, _writeLock, _cancellationTokenSource.Token);
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
                RpcUtils.ServerProcessRequestAsync(request, _serverMethodsNameCache, _serverMethodsSignatureCache, Implementation, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
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


        /// <summary>
        /// Occurs when the current object was disposed
        /// </summary>
        public event EventHandler? Disposed;
    }
}
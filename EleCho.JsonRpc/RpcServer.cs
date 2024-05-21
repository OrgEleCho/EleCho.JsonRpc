using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

namespace EleCho.JsonRpc
{
    /// <summary>
    /// JSON RPC Server abstraction
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRpcServer<T> where T : class
    {
        /// <summary>
        /// Server side method implementations instance
        /// </summary>
        public T Implementation { get; }

        /// <summary>
        /// Allow concurrent invoking. When concurrent calls are allowed, methods that return a Task will not be waited
        /// </summary>
        public bool AllowConcurrentInvoking { get; set; }

        /// <summary>
        /// Allow parallel invoking. When concurrent calls are allowed, each method call is executed in a new thread
        /// </summary>
        public bool AllowParallelInvoking { get; set; }

        internal RpcPackage? ProcessInvocation(RpcRequest request);
        internal Task<RpcPackage?> ProcessInvocationAsync(RpcRequest request);
    }


    /// <summary>
    /// JSON RPC Server
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RpcServer<T> : IRpcServer<T>, IDisposable 
        where T : class
    {
        private readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsNameCache = new();
        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsSignatureCache = new();


        private bool _disposed = false;


        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public T Implementation { get; }

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

        /// <summary>
        /// Create a new instance of JSON RPC Server
        /// </summary>
        /// <param name="client">Network stream for communication</param>
        /// <param name="implementation">Server side method implementations instance</param>
        public RpcServer(Stream client, T implementation) : this(client, client, implementation) { }

        /// <summary>
        /// Create a new instance of JSON RPC Server
        /// </summary>
        /// <param name="send">Network stream for sending data</param>
        /// <param name="recv">Network stream for receiving data</param>
        /// <param name="implementation">Server side method implementations instance</param>
        /// <exception cref="ArgumentNullException">Some parameter is null</exception>
        public RpcServer(Stream send, Stream recv, T implementation)
        {
            if (implementation is null)
                throw new ArgumentNullException(nameof(implementation));
            if (send is null)
                throw new ArgumentNullException(nameof(send));
            if (recv is null)
                throw new ArgumentNullException(nameof(recv));

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);
            Implementation = implementation;

            Task.Run(MainLoop);
        }

        private async Task MainLoop()
        {
            while (!_disposed)
            {
                try
                {
                    if ((await _recvReader.ReadPackageAsync(_readLock, _cancellationTokenSource.Token)) is not RpcPackage package)
                    {
                        Dispose();
                        return;
                    }

#if DEBUG
                    Debug.WriteLine($"Server received package: {package}");
#endif

                    if (package is RpcRequest requestPackage)
                    {
                        var processAndRespondTask = AllowParallelInvoking ?
                            Task.Run(() => ProcessRequestAndRespondAsync(requestPackage)):
                            ProcessRequestAndRespondAsync(requestPackage);

                        if (!AllowConcurrentInvoking)
                            await processAndRespondTask;
                    }
                }
                catch (JsonException ex)
                {
                    var errorPackage = new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ParseError, ex.Message, ex.Data),
                        SharedRandom.NextId());

                    await _sendWriter.WritePackageAsync(_writeLock, errorPackage, _cancellationTokenSource.Token);
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
                RpcPackage? r_pkg = await RpcUtils.ServerProcessRequestAsync(requestPackage, _methodsNameCache, _methodsSignatureCache, Implementation, _cancellationTokenSource.Token);

                if (r_pkg == null)
                    return;

                await _sendWriter.WritePackageAsync(_writeLock, r_pkg, _cancellationTokenSource.Token);
            }
        }

        RpcPackage? IRpcServer<T>.ProcessInvocation(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequest(request, _methodsNameCache, _methodsSignatureCache, Implementation);
        }

        Task<RpcPackage?> IRpcServer<T>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, _methodsNameCache, _methodsSignatureCache, Implementation, _cancellationTokenSource.Token);
        }

        void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException($"The RpcServer was disposed.");
        }


        /// <summary>
        /// Occurs when the current object was disposed
        /// </summary>
        public event EventHandler? Disposed;
    }
}
using System;
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
    public interface IRpcServer<T> where T : class
    {
        public T Implementation { get; }

        public bool AllowConcurrentInvoking { get; set; }
        public bool AllowParallelInvoking { get; set; }

        internal RpcPackage? ProcessInvocation(RpcRequest request);
        internal Task<RpcPackage?> ProcessInvocationAsync(RpcRequest request);
    }


    public class RpcServer<T> : IRpcServer<T>, IDisposable where T : class
    {
        public readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsNameCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsSignatureCache = new();


        private bool _disposed = false;


        public T Implementation { get; }

        public bool AllowConcurrentInvoking { get; set; } = true;
        public bool AllowParallelInvoking { get; set; } = false;
        public bool DisposeBaseStream { get; set; } = false;

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

        public RpcServer(Stream client, T implementation) : this(client, client, implementation) { }
        public RpcServer(Stream send, Stream recv, T instance)
        {
            if (instance is null)
                throw new ArgumentNullException(nameof(instance));

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);
            Implementation = instance;

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

        public event EventHandler? Disposed;
    }
}
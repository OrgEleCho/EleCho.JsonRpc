using EleCho.JsonRpc.Utils;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        /// Whether main loop is running
        /// </summary>
        public bool IsRunning { get; }

        /// <summary>
        /// Start main loop in background
        /// </summary>
        public void Start();

        /// <summary>
        /// Run main loop asynchronously
        /// </summary>
        public Task RunAsync();

        /// <summary>
        /// Run main loop and wait for it to complete
        /// </summary>
        public void Run();

        /// <summary>
        /// Stop the background main loop started by <see cref="Start"/>
        /// </summary>
        public void Stop();

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
        private readonly object _mainLoopLock = new();

        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsNameCache = new();
        private readonly ConcurrentDictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> _methodsSignatureCache = new();


        private bool _disposed = false;
        private Task? _mainLoopTask;
        private CancellationTokenSource? _mainLoopCancellationTokenSource;


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
        /// Whether main loop is running
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_mainLoopLock)
                {
                    return _mainLoopTask != null && !_mainLoopTask.IsCompleted;
                }
            }
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
        }

        /// <summary>
        /// Start main loop in background
        /// </summary>
        public void Start()
        {
            CancellationTokenSource mainLoopCancellationTokenSource = PrepareMainLoop();
            Task mainLoopTask = Task.Run(() => MainLoop(mainLoopCancellationTokenSource.Token));

            lock (_mainLoopLock)
            {
                _mainLoopTask = mainLoopTask;
            }

            _ = mainLoopTask.ContinueWith(
                _ => FinishMainLoop(mainLoopCancellationTokenSource),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Run main loop asynchronously
        /// </summary>
        public async Task RunAsync()
        {
            CancellationTokenSource mainLoopCancellationTokenSource = PrepareMainLoop();
            Task mainLoopTask = MainLoop(mainLoopCancellationTokenSource.Token);

            lock (_mainLoopLock)
            {
                _mainLoopTask = mainLoopTask;
            }

            try
            {
                await mainLoopTask;
            }
            finally
            {
                FinishMainLoop(mainLoopCancellationTokenSource);
            }
        }

        /// <summary>
        /// Run main loop and wait for it to complete
        /// </summary>
        public void Run()
        {
            RunAsync().Wait();
        }

        /// <summary>
        /// Stop the background main loop started by <see cref="Start"/>
        /// </summary>
        public void Stop()
        {
            CancellationTokenSource? mainLoopCancellationTokenSource;
            lock (_mainLoopLock)
            {
                mainLoopCancellationTokenSource = _mainLoopCancellationTokenSource;
            }

            mainLoopCancellationTokenSource?.Cancel();
        }

        private CancellationTokenSource PrepareMainLoop()
        {
            lock (_mainLoopLock)
            {
                EnsureNotDisposed();

                if (_mainLoopTask != null && !_mainLoopTask.IsCompleted)
                    throw new InvalidOperationException("The RpcServer is already running.");

                _mainLoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
                _mainLoopTask = new TaskCompletionSource<object?>().Task;
                return _mainLoopCancellationTokenSource;
            }
        }

        private void FinishMainLoop(CancellationTokenSource mainLoopCancellationTokenSource)
        {
            lock (_mainLoopLock)
            {
                if (ReferenceEquals(_mainLoopCancellationTokenSource, mainLoopCancellationTokenSource))
                {
                    _mainLoopTask = null;
                    _mainLoopCancellationTokenSource = null;
                }
            }

            mainLoopCancellationTokenSource.Dispose();
        }

        private async Task MainLoop(CancellationToken cancellationToken)
        {
            while (!_disposed)
            {
                try
                {
                    if ((await _recvReader.ReadPackageAsync(_readLock, cancellationToken)) is not RpcPackage package)
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

                    await _sendWriter.WritePackageAsync(_writeLock, errorPackage, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
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
                RpcPackage? r_pkg = await RpcUtils.ServerProcessRequestAsync(requestPackage, _methodsNameCache, _methodsSignatureCache, Implementation, cancellationToken);

                if (r_pkg == null)
                    return;

                await _sendWriter.WritePackageAsync(_writeLock, r_pkg, cancellationToken);
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

using EleCho.JsonRpc.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;


#if NET6_0_OR_GREATER
using System.Threading.Tasks.Dataflow;
#endif

namespace EleCho.JsonRpc
{
    /// <summary>
    /// JSON RPC Client abstraction
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRpcClient<T> where T : class
    {
        /// <summary>
        /// Instance for invoking remote methods
        /// </summary>
        public T Remote { get; }

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

        internal object? ProcessInvocation(MethodInfo? targetMethod, object?[]? args);
        internal Task<object?> ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args);
    }


    /// <summary>
    /// JSON RPC Client
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RpcClient<T> : IRpcClient<T>, IDisposable
        where T : class
    {
        private readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly object _mainLoopLock = new();

        private readonly ConcurrentDictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> _methodsCache = new();
        private readonly ConcurrentDictionary<object, RpcPackage> _rpcResponseDict = new();


        private bool _disposed = false;
        private Task? _mainLoopTask;
        private CancellationTokenSource? _mainLoopCancellationTokenSource;


        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public T Remote { get; }

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
        /// Construct a new JSON RPC Client
        /// </summary>
        /// <param name="server">Network stream for communication</param>
        public RpcClient(Stream server) : this(server, server) { }

        /// <summary>
        /// Construct a new JSON RPC Client
        /// </summary>
        /// <param name="send">Network stream for sending data</param>
        /// <param name="recv">Network stream for receiving data</param>
        /// <exception cref="ArgumentException">The <typeparamref name="T"/> is not interface</exception>
        /// <exception cref="ArgumentNullException">Some parameter is null</exception>
        public RpcClient(Stream send, Stream recv)
        {
            if (!typeof(T).IsInterface)
                throw new ArgumentException("Type must be an interface");
            if (send is null)
                throw new ArgumentNullException(nameof(send));
            if (recv is null)
                throw new ArgumentNullException(nameof(recv));

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
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
                    throw new InvalidOperationException("The RpcClient is already running.");

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
                    if ((await _recvReader.ReadPackageAsync(_readLock, cancellationToken)) is not RpcPackage pkg)
                    {
                        Dispose();
                        return;
                    }

                    if (pkg is RpcResponse resp)
                    {
                        _rpcResponseDict[resp.Id] = resp;
                    }
                    else if (pkg is RpcErrorResponse errResp)
                    {
                        _rpcResponseDict[errResp.Id] = errResp;
                    }
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
        }

        RpcPackage? ReceiveResponse(object id)
        {
            while (true)
            {
                if (_rpcResponseDict.TryGetValue(id, out RpcPackage? r_pak))
                {
                    _rpcResponseDict.TryRemove(id, out _);
                    return r_pak;
                }
            }
        }

        async Task<RpcPackage?> ReceiveResponseAsync(object id)
        {
            while (true)
            {
                if (_rpcResponseDict.TryGetValue(id, out RpcPackage? r_pak))
                {
                    _rpcResponseDict.TryRemove(id, out _);
                    return r_pak;
                }

                await Task.Delay(1);
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

        void EnsureNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("The RpcClient was disposed.");
        }

        object? IRpcClient<T>.ProcessInvocation(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocation(targetMethod, args, _methodsCache, _sendWriter, ReceiveResponse, _writeLock);
        }

        Task<object?> IRpcClient<T>.ProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ClientProcessInvocationAsync(targetMethod, args, _methodsCache, _sendWriter, ReceiveResponseAsync, _writeLock, _cancellationTokenSource.Token);
        }


        /// <summary>
        /// Occurs when the current object was disposed
        /// </summary>
        public event EventHandler? Disposed;
    }
}

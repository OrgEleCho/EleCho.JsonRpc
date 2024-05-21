using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;
using System.Threading;


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
        private readonly Stream _send, _recv;
        private readonly StreamWriter _sendWriter;
        private readonly StreamReader _recvReader;

        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _readLock = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> _methodsCache = new();
        private readonly Dictionary<object, RpcPackage> _rpcResponseDict = new();


        private bool _disposed = false;


        public T Remote { get; }
        public bool DisposeBaseStream { get; set; } = false;

        public RpcClient(Stream server) : this(server, server) { }
        public RpcClient(Stream send, Stream recv)
        {
            if (!typeof(T).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this._send = send;
            this._recv = recv;

            _sendWriter = new StreamWriter(send) { AutoFlush = true };
            _recvReader = new StreamReader(recv);

            Remote = RpcUtils.CreateDynamicProxy(this);
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
        }

        RpcPackage? ReceiveResponse(object id)
        {
            while (true)
            {
                if (_rpcResponseDict.TryGetValue(id, out RpcPackage? r_pak))
                {
                    _rpcResponseDict.Remove(id);
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
                    _rpcResponseDict.Remove(id);
                    return r_pak;
                }

                await Task.Delay(1);
            }
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

        public event EventHandler? Disposed;
    }
}
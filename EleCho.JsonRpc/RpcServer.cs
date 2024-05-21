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

        internal RpcPackage? ProcessInvocation(RpcRequest request);
        internal Task<RpcPackage?> ProcessInvocationAsync(RpcRequest request);
    }

    public class RpcServer<T> : IRpcServer<T>, IDisposable where T : class
    {
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsNameCache = new();
        private readonly Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsSignatureCache = new();

        public readonly Stream send, recv;
        private readonly StreamWriter sendWriter;
        private readonly StreamReader recvReader;
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private readonly SemaphoreSlim readLock = new(1, 1);
        private bool disposed = false;

        public T Implementation { get; }
        public bool AllowParallelInvoking { get; set; } = false;
        public bool DisposeBaseStream { get; set; } = false;

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

        public RpcServer(Stream client, T implementation) : this(client, client, implementation) { }
        public RpcServer(Stream send, Stream recv, T instance)
        {
            if (instance is null)
                throw new ArgumentNullException(nameof(instance));

            this.send = send;
            this.recv = recv;

            sendWriter = new StreamWriter(send) { AutoFlush = true };
            recvReader = new StreamReader(recv);
            Implementation = instance;

            Task.Run(MainLoop);
        }

        private async Task MainLoop()
        {
            while (!disposed)
            {
                try
                {
                    RpcPackage? package = await recvReader.ReadPackageAsync(readLock);

#if DEBUG
                    Debug.WriteLine($"Server received package: {package}");
#endif

                    if (package is null)
                    {
                        Dispose();
                        break;
                    }

                    if (package is RpcRequest requestPackage)
                    {
                        RpcPackage? r_pkg = await RpcUtils.ServerProcessRequestAsync(requestPackage, methodsNameCache, methodsSignatureCache, Implementation);

                        if (r_pkg == null)
                            continue;

                        await sendWriter.WritePackageAsync(writeLock, r_pkg);
                    }
                }
                catch (JsonException ex)
                {
                    var errorPackage = new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ParseError, ex.Message, ex.Data),
                        SharedRandom.NextId());

                    await sendWriter.WritePackageAsync(writeLock, errorPackage);
                }
                catch (IOException)
                {
                    Dispose();
                }
            }
        }

        RpcPackage? IRpcServer<T>.ProcessInvocation(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequest(request, methodsNameCache, methodsSignatureCache, Implementation);
        }

        Task<RpcPackage?> IRpcServer<T>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, methodsNameCache, methodsSignatureCache, Implementation);
        }

        void EnsureNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException($"The RpcServer was disposed.");
        }

        public event EventHandler? Disposed;
    }
}
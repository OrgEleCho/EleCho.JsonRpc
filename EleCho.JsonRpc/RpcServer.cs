using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using EleCho.JsonRpc.Utils;

namespace EleCho.JsonRpc
{
    public interface IRpcServer<T> where T : class
    {
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
        public T Instance { get; }

        public bool DisposeBaseStream { get; set; } = false;

        bool disposed = false;
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

        public RpcServer(Stream client, T instance) : this(client, client, instance) { }
        public RpcServer(Stream send, Stream recv, T instance)
        {
            this.send = send;
            this.recv = recv;
            Instance = instance;

            sendWriter = new StreamWriter(send) { AutoFlush = true };
            recvReader = new StreamReader(recv);

            Task.Run(MainLoop);
        }

        private void MainLoop()
        {
            while (!disposed)
            {
                try
                {
                    string? line = recvReader.ReadLine();

#if DEBUG
                    Debug.WriteLine($"Server received package: {line}");
#endif

                    if (line == null)
                    {
                        Dispose();
                        break;
                    }

                    RpcPackage? pkg =
                        JsonSerializer.Deserialize<RpcPackage>(line, JsonUtils.Options);

                    if (pkg is RpcRequest req)
                    {
                        RpcPackage? r_pkg = RpcUtils.ServerProcessRequest(req, methodsNameCache, methodsSignatureCache, Instance);

                        if (r_pkg == null)
                            continue;

                        string r_json =
                            JsonSerializer.Serialize(r_pkg, JsonUtils.Options);

                        sendWriter.WriteLine(r_json);
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
            }
        }

        RpcPackage? IRpcServer<T>.ProcessInvocation(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequest(request, methodsNameCache, methodsSignatureCache, Instance);
        }

        Task<RpcPackage?> IRpcServer<T>.ProcessInvocationAsync(RpcRequest request)
        {
            EnsureNotDisposed();
            return
                RpcUtils.ServerProcessRequestAsync(request, methodsNameCache, methodsSignatureCache, Instance);
        }

        void EnsureNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException($"The RpcServer was disposed.");
        }

        public event EventHandler? Disposed;
    }
}
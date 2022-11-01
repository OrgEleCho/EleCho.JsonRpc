using System.Reflection;
using System.Text;

namespace EleCho.JsonRpc
{

    public class RpcClient<T>
    {
        class RpcInvoker : DispatchProxy
        {
            public Stream? Send { get; set; } = null;
            public Stream? Recv { get; set; } = null;

            private readonly object calllock = new object();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (Send == null || Recv == null)
                    throw new InvalidCastException("Instance not initalized");

                lock (calllock)
                {
                    if (targetMethod == null)
                        return null;

                    Send.WriteJsonMessage(new RpcRequest(targetMethod.Name, args));
                    RpcResponse? resp = Recv.ReadJsonMessage<RpcResponse>();

                    if (resp == null)
                        throw new InvalidOperationException("Invalid protocol between server and client");

                    if (resp.Err != null)
                        throw new TargetInvocationException(resp.Err, null);

                    return resp.Ret;
                }
            }
        }

        public T Remote { get; }

        public RpcClient(Stream server) : this(server, server) { }
        public RpcClient(Stream send, Stream recv)
        {
            try
            {
                T rpc = DispatchProxy.Create<T, RpcInvoker>()!;

                if (rpc is RpcInvoker _rpc)
                {
                    _rpc.Send = send;
                    _rpc.Recv = recv;
                }
                else
                {
                    throw new InvalidOperationException();
                }

                Remote = rpc;
            }
            catch (ArgumentException are)
            {
                throw new ArgumentException("T is not interface", are);
            }
        }
    }
}
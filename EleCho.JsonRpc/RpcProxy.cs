using System.Reflection;

#region .NET462 Usings
#if NET462_OR_GREATER
using Castle.DynamicProxy;
#endif
#endregion

namespace EleCho.JsonRpc
{
    public partial class RpcClient<T>
    {
#if NET6_0_OR_GREATER
        class RpcInvoker : DispatchProxy
        {
            public RpcClient<T> Client = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return Client.ProcessInvocation(targetMethod, args);
            }
        }
#elif NET462_OR_GREATER
        class RpcProxyIntercepter : IInterceptor
        {
            public readonly RpcClient<T> Client;

            public RpcProxyIntercepter(RpcClient<T> client)
            {
                Client = client;
            }

            public void Intercept(IInvocation invocation)
            {
                MethodInfo targetMethod = invocation.Method;
                object?[]? args = invocation.Arguments;

                invocation.ReturnValue = Client.ProcessInvocation(targetMethod, args);
            }
        }
#endif
    }
}

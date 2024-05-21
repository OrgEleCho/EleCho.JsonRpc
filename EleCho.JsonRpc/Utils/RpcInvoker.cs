using System;
using System.Reflection;
using System.Threading.Tasks;

#region .NET462 Usings
#if NET462_OR_GREATER
using Castle.DynamicProxy;
using EleCho.JsonRpc.Utils;
#endif
#endregion

namespace EleCho.JsonRpc.Utils
{
    /// <summary>
    /// Proxy for RPC client
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RpcInvoker<T> : DispatchProxy where T : class
    {
        static readonly Type TaskType = typeof(Task);

        /// <summary>
        /// RPC Client instance
        /// </summary>
        public IRpcClient<T> Client = null!;


        /// <summary>
        /// Method invoking implementation
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
                return null;

            if (TaskType.IsAssignableFrom(targetMethod.ReturnType))
                return Client.ProcessInvocationAsync(targetMethod, args);
            else
                return Client.ProcessInvocation(targetMethod, args);
        }
    }
}

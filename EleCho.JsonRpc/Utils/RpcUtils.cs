using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#region .NET462 Usings
#if NET462_OR_GREATER
using Castle.DynamicProxy;
using EleCho.JsonRpc.Utils;
#endif
#endregion

namespace EleCho.JsonRpc.Utils
{
    internal class RpcUtils
    {
        public static string GetMethodSignature(MethodInfo method, ParameterInfo[] paramInfos)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(method.Name);

            if (paramInfos.Length > 0)
            {
                sb.Append(':');
                sb.Append(paramInfos[0].ParameterType.FullName);
                for (int i = 1; i < paramInfos.Length; i++)
                {
                    sb.Append(',');
                    sb.Append(paramInfos[i].ParameterType.FullName);
                }
            }

            return sb.ToString();
        }

        public static void GetMethodNameAndParameterTypesFromSignature(string signature, out string methodName, out Type[] parameterTypes)
        {
            int colonIndex = signature.IndexOf(':');
            if (colonIndex == -1)
            {
                methodName = signature;
                parameterTypes = new Type[0];
                return;
            }

            methodName = signature.Substring(0, colonIndex);
            string[] paramTypes = signature.Substring(colonIndex + 1).Split(',');
            parameterTypes = new Type[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
                parameterTypes[i] = Type.GetType(paramTypes[i]) ?? throw new Exception("Invalid parameter type");
        }


        public static T CreateDynamicProxy<T>(IRpcClient<T> client) where T : class
        {
#if NET6_0_OR_GREATER
            T proxy = DispatchProxy.Create<T, RpcInvoker<T>>();
            if (proxy is RpcInvoker<T> _rpc)
                _rpc.Client = client;
            else
                throw new InvalidOperationException("This would never happen");
            return proxy;
#elif NET462_OR_GREATER
            ProxyGenerator proxyGenerator = new ProxyGenerator();
            T proxy = proxyGenerator.CreateInterfaceProxyWithoutTarget<T>(new RpcProxyIntercepter<T>(client));

            return proxy;
#else
            throw new NotImplementedException();
#endif
        }

        public static object? ClientProcessInvocation(MethodInfo? targetMethod, object?[]? args,
            Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache, Stream send, Func<RpcResponse?> recv, object invocationLock)
        {
            if (send == null || recv == null)
                throw new InvalidCastException("Instance not initalized");

            lock (invocationLock)
            {
                if (targetMethod == null)
                    return null;

                if (!methodsCache.TryGetValue(targetMethod, out (string Signature, ParameterInfo[] ParamInfos) methodStorage))
                {
                    methodStorage = methodsCache[targetMethod] =
                        (GetMethodSignature(targetMethod, targetMethod.GetParameters()), targetMethod.GetParameters());
                }

                ParameterInfo[] paramInfos = methodStorage.ParamInfos;
                send.WriteJsonMessage(new RpcRequest(methodStorage.Signature, args));
                send.Flush();

                RpcResponse? resp = recv.Invoke();

                if (resp == null)
                    throw new InvalidOperationException("Invalid protocol between server and client");
                if (resp.Err != null)
                    throw new TargetInvocationException(resp.Err, null);

                object? ret =
                            resp.Ret is JsonElement jret ? jret.Deserialize(targetMethod.ReturnType) : null;

                if (resp.RefRet is object?[] refRet)
                {
                    int i = 0;
                    foreach (ParameterInfo paramInfo in paramInfos)
                        if (paramInfo.ParameterType.IsByRef)
                        {
                            Type paramType = paramInfo.ParameterType;
                            if (paramType.IsByRef)
                                paramType = paramType.GetElementType()!;

                            object? arg = refRet[i];
                            if (arg is JsonElement jarg)
                                arg = jarg.Deserialize(paramType);

                            args![i] = arg;
                        }
                }

                return ret;
            }
        }

        public static RpcResponse ServerProcessRequest<T>(RpcRequest request,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsCache, T instance) where T : class
        {
            if (request == null)
                return new RpcResponse(null, null, "Invalid invocation");

            if (!methodsCache.TryGetValue(request.Method, out (MethodInfo Method, ParameterInfo[] ParamInfos) methodStorage))
            {
                try
                {
                    GetMethodNameAndParameterTypesFromSignature(request.Method, out string methodName, out Type[] parameterTypes);
                    MethodInfo? foundMethod = typeof(T).GetMethod(methodName, parameterTypes);

                    if (foundMethod == null)
                        return new RpcResponse(null, null, "Method not found");

                    methodStorage = methodsCache[request.Method] =
                        (foundMethod, foundMethod.GetParameters());
                }
                catch
                {
                    return new RpcResponse(null, null, "Method not found");
                }
            }

            int refArgCount = 0;
            object?[]? parameters = request.Arg;

            if (parameters != null)
            {
                try
                {
                    ParameterInfo[] parameterInfos = methodStorage.ParamInfos;

                    object?[] convertedArg = new object[parameters.Length];
                    for (int i = 0; i < convertedArg.Length; i++)
                        if (parameters[i] is JsonElement ele)
                        {
                            Type paramType = parameterInfos[i].ParameterType;
                            if (paramType.IsByRef)
                            {
                                paramType = paramType.GetElementType()!;
                                refArgCount++;
                            }

                            convertedArg[i] = ele.Deserialize(paramType);
                        }

                    parameters = convertedArg;
                }
                catch
                {
                    return new RpcResponse(null, null, "Invalid parameters");
                }
            }

            try
            {
                object? ret = methodStorage.Method.Invoke(instance, parameters);
                object?[]? refArgs = null;
                if (refArgCount > 0)
                {
                    refArgs = new object[refArgCount];

                    int i = 0;
                    ParameterInfo[] parameterInfos = methodStorage.ParamInfos;
                    foreach (var paramInfo in parameterInfos)
                        if (paramInfo.ParameterType.IsByRef)
                            refArgs[i] = parameters![i++];
                }

                return new RpcResponse(ret, refArgs, null);
            }
            catch (TargetInvocationException ex)
            {
                return new RpcResponse(null, null, ex.Message);
            }
        }

#if NET6_0_OR_GREATER
        class RpcInvoker<T> : DispatchProxy where T : class
        {
            public IRpcClient<T> Client = null!;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                return Client.ProcessInvocation(targetMethod, args);
            }
        }
#elif NET462_OR_GREATER
        class RpcProxyIntercepter<T> : IInterceptor where T : class
        {
            public readonly IRpcClient<T> Client;

            public RpcProxyIntercepter(IRpcClient<T> client)
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

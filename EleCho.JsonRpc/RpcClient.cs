using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

#region .NET462 Usings
#if NET462_OR_GREATER
using Castle.DynamicProxy;
#endif
#endregion

namespace EleCho.JsonRpc
{

    public partial class RpcClient<T> where T : class
    {
        private readonly Stream send, recv;
        private readonly object invokationLock = new object();
        public T Remote { get; }

        public Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache =
                new Dictionary<MethodInfo, (string, ParameterInfo[])>();

        public RpcClient(Stream server) : this(server, server) { }
        public RpcClient(Stream send, Stream recv)
        {
            if (!typeof(T).IsInterface)
                throw new ArgumentException("Type must be an interface");

            this.send = send;
            this.recv = recv;
            
            Remote = CreateDynamicProxy();
        }
        
        private T CreateDynamicProxy()
        {
#if NET6_0_OR_GREATER
            T proxy = DispatchProxy.Create<T, RpcInvoker>();
            if (proxy is RpcInvoker _rpc)
                _rpc.Client = this;
            else
                throw new InvalidOperationException("This would never happen");
            return proxy;
#elif NET462_OR_GREATER
            ProxyGenerator proxyGenerator = new ProxyGenerator();
            T proxy = proxyGenerator.CreateInterfaceProxyWithoutTarget<T>(new RpcProxyIntercepter(this));

            return proxy;
#else
            throw new NotImplementedException();
#endif
        }
        
        private object? ProcessInvocation(MethodInfo? targetMethod, object?[]? args)
        {
            if (send == null || recv == null)
                throw new InvalidCastException("Instance not initalized");

            lock (invokationLock)
            {
                if (targetMethod == null)
                    return null;

                if (!methodsCache.TryGetValue(targetMethod, out (string Signature, ParameterInfo[] ParamInfos) methodStorage))
                {
                    methodStorage = methodsCache[targetMethod] =
                        (RpcUtils.GetMethodSignature(targetMethod, targetMethod.GetParameters()), targetMethod.GetParameters());
                }

                ParameterInfo[] paramInfos = methodStorage.ParamInfos;
                send.WriteJsonMessage(new RpcRequest(methodStorage.Signature, args));
                send.Flush();

                RpcResponse? resp = recv.ReadJsonMessage<RpcResponse>();

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
    }
}
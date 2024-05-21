using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#region .NET462 Usings
#if NET462_OR_GREATER
using Castle.DynamicProxy;
using EleCho.JsonRpc.Utils;
#endif
#endregion

namespace EleCho.JsonRpc.Utils
{
    internal static class RpcUtils
    {
        /// <summary>
        /// 生成唯一的方法签名字符串
        /// </summary>
        /// <param name="method"></param>
        /// <param name="paramInfos"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 为签名获取方法名和参数类型
        /// </summary>
        /// <param name="signature"></param>
        /// <param name="methodName"></param>
        /// <param name="parameterTypes"></param>
        /// <exception cref="Exception"></exception>
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

        /// <summary>
        /// 创建动态代理
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <returns></returns>
        public static T CreateDynamicProxy<T>(IRpcClient<T> client) where T : class
        {
            T proxy = DispatchProxy.Create<T, RpcInvoker<T>>();
            if (proxy is RpcInvoker<T> _rpc)
                _rpc.Client = client;
            else
                throw new InvalidOperationException("This would never happen");
            return proxy;
        }

        public static void ClientThrowExceptionFromErrorResponse(RpcErrorResponse r_err_resp)
        {
            if (r_err_resp.Error.IsParseError)
                throw new InvalidOperationException(r_err_resp.Error.Message) { Data = { { "Error", r_err_resp.Error } } };
            else if (r_err_resp.Error.IsInvalidRequest)
                throw new InvalidCastException(r_err_resp.Error.Message) { Data = { { "Error", r_err_resp.Error } } };
            else if (r_err_resp.Error.IsMethodNotFound)
                throw new InvalidOperationException(r_err_resp.Error.Message) { Data = { { "Error", r_err_resp.Error } } };
            else if (r_err_resp.Error.IsInvalidParams)
                throw new ArgumentException(r_err_resp.Error.Message) { Data = { { "Error", r_err_resp.Error } } };
            else if (r_err_resp.Error.IsInternalError)
                throw new NotSupportedException(r_err_resp.Error.Message) { Data = { { "Error", r_err_resp.Error } } };
            else
                throw new TargetInvocationException(r_err_resp.Error.Message, null) { Data = { { "Error", r_err_resp.Error } } };
        }

        public static bool ClientConvertMethodResult(
            MethodInfo targetMethod, ParameterInfo[] targetMethodParams,
            object? retOrigin, object?[]? refRetOrigin, ref object?[]? args, out object? ret)
        {
            ret =
                retOrigin is JsonElement jret ? jret.Deserialize(targetMethod.ReturnType) : retOrigin;

            if (refRetOrigin != null && args != null)
            {
                for (int i = 0; i < targetMethodParams.Length; i++)
                {
                    ParameterInfo paramInfo = targetMethodParams[i];
                    if (paramInfo.ParameterType.IsByRef)
                    {
                        Type paramType = paramInfo.ParameterType;
                        if (paramType.IsByRef)
                            paramType = paramType.GetElementType()!;

                        object? arg = refRetOrigin[i];
                        if (arg is JsonElement jarg)
                            arg = jarg.Deserialize(paramType);

                        args[i] = arg;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 客户端处理调用
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <param name="args"></param>
        /// <param name="methodsCache"></param>
        /// <param name="sendWriter"></param>
        /// <param name="recv"></param>
        /// <param name="writeLock"></param>
        /// <returns></returns>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="TargetInvocationException"></exception>
        public static object? ClientProcessInvocation(MethodInfo? targetMethod, object?[]? args,
            Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache, StreamWriter sendWriter, Func<object, RpcPackage?> recv, SemaphoreSlim writeLock)
        {
            if (sendWriter == null || recv == null)
                throw new InvalidCastException("Instance not initalized");

            if (targetMethod == null)
                return null;

            if (!methodsCache.TryGetValue(targetMethod, out (string Signature, ParameterInfo[] ParamInfos) methodStorage))
            {
                methodStorage = methodsCache[targetMethod] =
                    (GetMethodSignature(targetMethod, targetMethod.GetParameters()), targetMethod.GetParameters());
            }

            RpcPackageId id = SharedRandom.NextId();

            ParameterInfo[] paramInfos = methodStorage.ParamInfos;

            // send the request
            sendWriter.WritePackage(writeLock,
                new RpcRequest(targetMethod.Name, args, methodStorage.Signature, id));

            // recv the response
            RpcPackage? r_pak = recv.Invoke(id);

            // Error Response
            if (r_pak is RpcErrorResponse r_err_resp)
                ClientThrowExceptionFromErrorResponse(r_err_resp);

            // Normal response
            if (r_pak is not RpcResponse resp)
                throw new InvalidOperationException("Invalid protocol between server and client");

            ClientConvertMethodResult(targetMethod, paramInfos, resp.Result, resp.RefResults, ref args, out object? ret);

            return ret;
        }

        public static async Task<object?> ClientProcessInvocationAsync(MethodInfo? targetMethod, object?[]? args,
            Dictionary<MethodInfo, (string Signature, ParameterInfo[] ParamInfos)> methodsCache, StreamWriter sendWriter, Func<object, Task<RpcPackage?>> recv, SemaphoreSlim writeLock)
        {
            if (sendWriter == null || recv == null)
                throw new InvalidCastException("Instance not initalized");

            if (targetMethod == null)
                return null;

            if (!methodsCache.TryGetValue(targetMethod, out (string Signature, ParameterInfo[] ParamInfos) methodStorage))
            {
                methodStorage = methodsCache[targetMethod] =
                    (GetMethodSignature(targetMethod, targetMethod.GetParameters()), targetMethod.GetParameters());
            }

            RpcPackageId id = SharedRandom.NextId();

            ParameterInfo[] paramInfos = methodStorage.ParamInfos;

            // send the request
            await sendWriter.WritePackageAsync(writeLock,
                new RpcRequest(targetMethod.Name, args, methodStorage.Signature, id));

            // recv the response
            RpcPackage? r_pak = await recv.Invoke(id);

            // Error Response
            if (r_pak is RpcErrorResponse r_err_resp)
                ClientThrowExceptionFromErrorResponse(r_err_resp);

            // Normal response
            if (r_pak is not RpcResponse resp)
                throw new InvalidOperationException("Invalid protocol between server and client");

            ClientConvertMethodResult(targetMethod, paramInfos, resp.Result, resp.RefResults, ref args, out object? ret);

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request"></param>
        /// <param name="methodsNameCache"></param>
        /// <param name="methodsSignatureCache"></param>
        /// <param name="targetMethod"></param>
        /// <param name="targetMethodParams"></param>
        /// <returns></returns>
        public static bool ServerFindMethod<T>(RpcRequest request,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsNameCache,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsSignatureCache,
#if NET6_0_OR_GREATER
            [NotNullWhen(true)] out MethodInfo? targetMethod, [NotNullWhen(true)] out ParameterInfo[]? targetMethodParams
#else
            out MethodInfo? targetMethod, out ParameterInfo[]? targetMethodParams
#endif
            ) where T : class
        {
            targetMethod = null;
            targetMethodParams = null;
            (MethodInfo, ParameterInfo[]) _methodStorage;

            // 第一步, 签名缓存中查找方法
            if (request.Signature != null &&
                methodsSignatureCache.TryGetValue(request.Signature, out _methodStorage))
            {
                (targetMethod, targetMethodParams) = _methodStorage;
            }

            // 第二部, 通过签名组装类型信息, 查找方法
            if ((targetMethod == null ||
                targetMethodParams == null) &&
                request.Signature != null)
            {
                try
                {
                    // 则通过签名找到类型, 并且尝试找到对应方法重载

                    GetMethodNameAndParameterTypesFromSignature(request.Signature, out string methodName, out Type[] parameterTypes);
                    MethodInfo? foundMethod = typeof(T).GetMethod(methodName, parameterTypes);

                    // 如果找到了
                    if (foundMethod != null)
                    {
                        targetMethod = foundMethod;
                        targetMethodParams = foundMethod.GetParameters();

                        // 存缓存
                        methodsNameCache[request.Method] = methodsSignatureCache[request.Signature] = (targetMethod, targetMethodParams);
                    }
                }
                catch { }
            }

            // 第三步, 在名称缓存中查找
            if ((targetMethod == null ||
                targetMethodParams == null) &&
                methodsNameCache.TryGetValue(request.Method, out _methodStorage))
            {
                (targetMethod, targetMethodParams) = _methodStorage;

                // 生成一个签名
                string signature =
                    GetMethodSignature(targetMethod, targetMethodParams);

                // 存缓存
                methodsNameCache[request.Method] = methodsSignatureCache[signature] = (targetMethod, targetMethodParams);
            }

            // 第四步, 直接通过名称查找
            if (targetMethod == null ||
                targetMethodParams == null)
            {
                // 直接通过方法名找到一个方法
                MethodInfo? foundMethod = typeof(T).GetMethod(request.Method);

                // 如果还是没找到, 就只能 return 错误响应了
                if (foundMethod == null)
                {
                    return false;
                }

                targetMethod = foundMethod;
                targetMethodParams = foundMethod.GetParameters();

                string signature =
                    GetMethodSignature(targetMethod, targetMethodParams);

                methodsNameCache[request.Method] = methodsSignatureCache[signature] = (targetMethod, targetMethodParams);
            }

            return true;
        }

        public static bool ServerConvertMethodParameters(object?[]? source, ParameterInfo[] targetMethodParams,
#if NET6_0_OR_GREATER
            out int refArgCount, out object?[]? parameters, [NotNullWhen(false)] out Exception? exception
#else
            out int refArgCount, out object?[]? parameters, out Exception? exception
#endif
            )
        {
            refArgCount = 0;
            parameters = null;
            exception = null;

            if (source == null)
                return true;

            try
            {
                ParameterInfo[] parameterInfos = targetMethodParams;

                object?[] convertedArg = new object[source.Length];
                for (int i = 0; i < convertedArg.Length; i++)
                    if (source[i] is JsonElement ele)
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

                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        public static void ServerConvertMethodRefResult(ParameterInfo[] targetMethodParams,
            int refArgCount, object?[] parameters, object? ret, out object?[]? refArgs)
        {
            if (refArgCount == 0)
            {
                refArgs = null;
                return;
            }

            refArgs = new object[refArgCount];

            ParameterInfo[] parameterInfos = targetMethodParams;
            for (int i = 0; i < parameterInfos.Length; i++)
            {
                ParameterInfo? paramInfo = parameterInfos[i];
                if (paramInfo.ParameterType.IsByRef)
                    refArgs[i] = parameters![i++];
            }

            return;
        }

        public static RpcPackage? ServerProcessRequest<T>(RpcRequest request,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsNameCache,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsSignatureCache, T instance) where T : class
        {
            if (request == null)
                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ParseError, "Invalid invocation", null),
                    SharedRandom.NextId());

            MethodInfo? targetMethod = null;
            ParameterInfo[]? targetMethodParams = null;

            if (!ServerFindMethod<T>(request, methodsNameCache, methodsSignatureCache, out targetMethod, out targetMethodParams))
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.MethodNotFound, "Method not found.", null),
                    request.Id.Value);
            }

            if (!ServerConvertMethodParameters(request.Args, targetMethodParams, out int refArgCount, out object?[]? parameters, out Exception? exception))
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.InvalidParams, exception.Message, exception.Data),
                    request.Id.Value);
            }

            try
            {
                // 实际执行方法
                object? ret = targetMethod.Invoke(instance, parameters);

                // 如果客户端不关心结果, 则不返回结果
                if (request.Id == null)
                    return null;

                // 组装返回值
                ServerConvertMethodRefResult(targetMethodParams, refArgCount, parameters!, ret, out object?[]? refArgs);

                // 检查异步返回值
                ret = GetOrWaitForResult(ret);

                // 返回
                return new RpcResponse(ret, refArgs, request.Id.Value);
            }
            catch (TargetInvocationException ex)
            {
                if (request.Id == null)
                    return null;

                Exception? realException = ex.InnerException;

                if (realException == null)
                    return new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ServerErrorUpBound, ex.Message, ex.Data),
                        request.Id.Value);

                if (realException is ArgumentException arg_ex)
                    return new RpcErrorResponse(
                        new RpcError(RpcErrorCode.InvalidParams, arg_ex.Message, arg_ex.Data),
                        request.Id.Value);

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ServerErrorUpBound, realException.Message, realException.Data),
                    request.Id.Value);
            }
            catch (Exception ex)
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ServerErrorUpBound, ex.Message, ex.Data),
                    request.Id.Value);
            }
        }

        public static async Task<RpcPackage?> ServerProcessRequestAsync<T>(RpcRequest request,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsNameCache,
            Dictionary<string, (MethodInfo Method, ParameterInfo[] ParamInfos)> methodsSignatureCache, T instance,
            CancellationToken cancellationToken = default) where T : class
        {
            if (request == null)
                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ParseError, "Invalid invocation", null),
                    SharedRandom.NextId());

            MethodInfo? targetMethod = null;
            ParameterInfo[]? targetMethodParams = null;

            if (!ServerFindMethod<T>(request, methodsNameCache, methodsSignatureCache, out targetMethod, out targetMethodParams))
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.MethodNotFound, "Method not found.", null),
                    request.Id.Value);
            }

            if (!ServerConvertMethodParameters(request.Args, targetMethodParams, out int refArgCount, out object?[]? parameters, out Exception? exception))
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.InvalidParams, exception.Message, exception.Data),
                    request.Id.Value);
            }

            try
            {
                // 实际执行方法
                object? ret = targetMethod.Invoke(instance, parameters);

                // 如果客户端不关心结果, 则不返回结果
                if (request.Id == null)
                    return null;

                // 组装返回值
                ServerConvertMethodRefResult(targetMethodParams, refArgCount, parameters!, ret, out object?[]? refArgs);

                // 检查异步返回值
                ret = await GetOrWaitForResultAsync(ret);

                // 返回
                return new RpcResponse(ret, refArgs, request.Id.Value);
            }
            catch (TargetParameterCountException ex)
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.InvalidParams, ex.Message, ex.Data),
                    request.Id.Value);
            }
            catch (TargetInvocationException ex)
            {
                if (request.Id == null)
                    return null;

                Exception? realException = ex.InnerException;

                if (realException == null)
                    return new RpcErrorResponse(
                        new RpcError(RpcErrorCode.ServerErrorUpBound, ex.Message, ex.Data),
                        request.Id.Value);

                if (realException is ArgumentException arg_ex)
                    return new RpcErrorResponse(
                        new RpcError(RpcErrorCode.InvalidParams, arg_ex.Message, arg_ex.Data),
                        request.Id.Value);

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ServerErrorUpBound, realException.Message, realException.Data),
                    request.Id.Value);
            }
            catch (Exception ex)
            {
                if (request.Id == null)
                    return null;

                return new RpcErrorResponse(
                    new RpcError(RpcErrorCode.ServerErrorUpBound, ex.Message, ex.Data),
                    request.Id.Value);
            }
        }


        static readonly Type taskType = typeof(Task<>);


        public static object? GetOrWaitForResult(object? origin)
        {
            if (origin is not Task task)
                return origin;

            task.Wait();

            Type valueType = origin.GetType();

            if (valueType.IsGenericType &&
                valueType.GetGenericTypeDefinition() == typeof(Task<>))
                return valueType.GetProperty("Result")?.GetValue(origin);

            return null;
        }

        public static async Task<object?> GetOrWaitForResultAsync(object? origin)
        {
            if (origin is not Task task)
                return origin;

            await task;

            Type valueType = origin.GetType();

            if (valueType.IsGenericType &&
                valueType.GetGenericTypeDefinition() == typeof(Task<>))
                return valueType.GetProperty("Result")?.GetValue(origin);

            return null;
        }

        public static bool WritePackage(this StreamWriter writer, SemaphoreSlim writeLock,
            RpcPackage package)
        {
            try
            {
                writeLock.Wait();
                string json = JsonSerializer.Serialize(package, JsonUtils.Options);

                writer.WriteLine(json);

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                writeLock.Release();
            }
        }

        public static async Task<bool> WritePackageAsync(
            this StreamWriter writer,
            SemaphoreSlim writeLock,
            RpcPackage package,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await writeLock.WaitAsync();

                string json = JsonSerializer.Serialize(package, JsonUtils.Options);

#if NET6_0_OR_GREATER
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
#else
                await writer.WriteLineAsync(json);
#endif

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                writeLock.Release();
            }
        }

        public static async Task<RpcPackage?> ReadPackageAsync(
            this StreamReader reader,
            SemaphoreSlim readLock,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await readLock.WaitAsync();

#if NET8_0_OR_GREATER
                string? json = await reader.ReadLineAsync(cancellationToken);
#elif NET6_0_OR_GREATER
                string? json = await reader.ReadLineAsync().WaitAsync(cancellationToken);
#else
                string? json = await reader.ReadLineAsync();
#endif

                if (json == null)
                    return null;

                return JsonSerializer.Deserialize<RpcPackage>(json, JsonUtils.Options);
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                readLock.Release();
            }
        }
    }
}

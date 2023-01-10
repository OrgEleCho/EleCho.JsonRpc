using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EleCho.JsonRpc
{
    public class RpcServer<T> : IDisposable
    {
        private static readonly Type t = typeof(T);

        private bool loop = true;
        private readonly Dictionary<string, (MethodInfo, ParameterInfo[])> methods =
            new Dictionary<string, (MethodInfo, ParameterInfo[])>();

        public Stream Send { get; }
        public Stream Recv { get; }
        public T Instance { get; }


        private void MainLoop()
        {
            while (loop)
            {
                RpcRequest? req = Recv.ReadJsonMessage<RpcRequest>();

                if (req == null)
                {
                    Send.WriteJsonMessage(new RpcResponse(null, null, "Invalid invocation"));
                    Send.Flush();
                    continue;
                }

                if (!methods.TryGetValue(req.Method, out (MethodInfo Method, ParameterInfo[] ParamInfos) methodStorage))
                {
                    MethodInfo? foundMethod = t.GetMethod(req.Method);

                    if (foundMethod == null)
                    {
                        Send.WriteJsonMessage(new RpcResponse(null, null, "Method not found"));
                        Send.Flush();
                        continue;
                    }

                    methodStorage = methods[req.Method] =
                        (foundMethod, foundMethod.GetParameters());
                }

                int refArgCount = 0;
                object?[]? parameters = req.Arg;

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
                        Send.WriteJsonMessage(new RpcResponse(null, null, "Invalid parameters"));
                        Send.Flush();
                        continue;
                    }
                }

                try
                {
                    object? ret = methodStorage.Method.Invoke(Instance, parameters);
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

                    Send.WriteJsonMessage(new RpcResponse(ret, refArgs, null));
                    Send.Flush();
                }
                catch (TargetInvocationException ex)
                {
                    Send.WriteJsonMessage(new RpcResponse(null, null, ex.Message));
                    Send.Flush();
                }
            }
        }

        public void Dispose() => loop = false;

        public RpcServer(Stream client, T instance) : this(client, client, instance) { }
        public RpcServer(Stream send, Stream recv, T instance)
        {
            Send = send;
            Recv = recv;
            Instance = instance;

            Task.Run(MainLoop);
        }
    }
}
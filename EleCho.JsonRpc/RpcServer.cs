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
                    Send.WriteJsonMessage(new RpcResponse(null, "Invalid invocation"));
                    continue;
                }

                if (!methods.TryGetValue(req.Method, out (MethodInfo, ParameterInfo[]) methodinfo))
                {
                    MethodInfo? foundMethod = t.GetMethod(req.Method);

                    if (foundMethod == null)
                    {
                        Send.WriteJsonMessage(new RpcResponse(null, "Method not found"));
                        continue;
                    }

                    methodinfo = methods[req.Method] =
                        (foundMethod, foundMethod.GetParameters());
                }

                object?[]? parameters = req.Arg;

                if (req.Arg != null)
                {
                    ParameterInfo[] parameterInfos = methodinfo.Item2;

                    object?[] convertedArg = new object[req.Arg.Length];
                    for (int i = 0; i < convertedArg.Length; i++)
                        if (req.Arg[i] is JsonElement ele)
                            convertedArg[i] = ele.Deserialize(parameterInfos[i].ParameterType);
                    parameters = convertedArg;
                }

                try
                {
                    object? ret = methodinfo.Item1.Invoke(Instance, parameters);
                    Send.WriteJsonMessage(new RpcResponse(ret, null));
                }
                catch (TargetInvocationException ex)
                {
                    Send.WriteJsonMessage(new RpcResponse(null, ex.Message));
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
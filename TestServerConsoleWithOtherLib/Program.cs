using System.Net.Sockets;
using System.Net;
using StreamJsonRpc;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例
List<JsonRpc> rpcs = new List<JsonRpc>();                 // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端
    Stream stream = client.GetStream();
    CommandsImpl impl = new CommandsImpl();
    JsonRpc rpc = new JsonRpc(new StreamJsonRpc.NewLineDelimitedMessageHandler(stream, stream, new StreamJsonRpc.JsonMessageFormatter()));
    rpc.AddLocalRpcMethod("WriteLine", impl.WriteLine);
    rpc.StartListening();

    rpcs.Add(rpc);
}

internal class CommandsImpl
{
    public DateTime DateTimeNow => DateTime.Now;

    public int Add(int a, int b) => a + b;
    public int Add114514(ref int num) => num += 114514;
    public void WriteLine(string message) => Console.WriteLine("Server print: " + message);
}
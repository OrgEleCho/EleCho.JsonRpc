using System.Net;
using System.Net.Sockets;
using TestCommon;
using EleCho.JsonRpc;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));             // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                        // 创建公用的指令调用实例
List<RpcServer<Commands>> rpcs = new List<RpcServer<Commands>>();                        // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                            // 接受一个客户端
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));               // 创建并保存 RPC 实例
}

internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
    public void WriteLine(string message) => Console.WriteLine("Server print: " + message);
}
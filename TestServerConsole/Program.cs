using EleCho.JsonRpc;
using System.Net;
using System.Net.Sockets;
using TestCommon;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();
List<RpcServer<Commands>> rpcs = new List<RpcServer<Commands>>();

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));
}

internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
    public void WriteLine(string message) => Console.WriteLine("Server print:" + message);
}
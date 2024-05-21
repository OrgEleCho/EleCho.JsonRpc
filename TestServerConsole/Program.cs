using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using EleCho.JsonRpc;
using TestCommon;
using System.Threading.Tasks;
using System.Threading;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例
List<RpcServer<ICommands>> rpcs = new List<RpcServer<ICommands>>();               // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端

    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
    var rpcServer = new RpcServer<ICommands>(client.GetStream(), serverCommands); // 创建新的 RPC 服务端实例
    rpcServer.AllowParallelInvoking = true;                                       // 允许并行调用

    rpcs.Add(rpcServer);
}

internal class CommandsImpl : ICommands
{
    public DateTime DateTimeNow => DateTime.Now;

    public int Add(int a, int b)
    {
        return a + b;
    }
    public int Add114514(ref int num) => num += 114514;
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(3000);
        return a + b;
    }

    public void WriteLine(string message)
    {
        Console.WriteLine("Server print: " + message);
    }
}
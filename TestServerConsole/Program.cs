using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using EleCho.JsonRpc;
using TestCommon;
using System.Threading.Tasks;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例
List<RpcServer<ICommands>> rpcs = new List<RpcServer<ICommands>>();                 // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端

    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
    rpcs.Add(new RpcServer<ICommands>(client.GetStream(), serverCommands));        // 创建并保存 RPC 实例
}

internal class CommandsImpl : ICommands
{
    public DateTime DateTimeNow => DateTime.Now;

    public int Add(int a, int b) => a + b;
    public int Add114514(ref int num) => num += 114514;
    public Task WriteLine(string message)
    {
        Console.WriteLine("Server print: " + message);
        return Task.CompletedTask;
    }
}
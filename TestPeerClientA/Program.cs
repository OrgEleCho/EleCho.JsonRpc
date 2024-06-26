﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using EleCho.JsonRpc;
using TestCommon;

int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例
List<RpcPeer<ICommands, CommandsImpl>> rpcs = new List<RpcPeer<ICommands, CommandsImpl>>();                 // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

_ = Task.Run(async () =>
{
    while (true)
    {
        TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端
        rpcs.Add(new RpcPeer<ICommands, CommandsImpl>(client.GetStream(), serverCommands));        // 创建并保存 RPC 实例
    }
});

while (true)
{
    while (true)
    {
        var input = Console.ReadLine();
        if (input == null)
            break;

        foreach(var rpc in rpcs)
            rpc.Remote.WriteLine(input);                        // 调用服务端 WriteLine 方法
    }
}

internal class CommandsImpl : ICommands
{
    public DateTime DateTimeNow => DateTime.Now;

    public int Add(int a, int b) => a + b;
    public int Add114514(ref int num) => num += 114514;
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(3000);
        return a + b;
    }

    public void ThrowException() => throw new NotImplementedException();

    public void WriteLine(string message)
    {
        Console.WriteLine("Server print: " + message);
    }
}
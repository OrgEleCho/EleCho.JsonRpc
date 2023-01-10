# EleCho.JsonRpc [![](https://img.shields.io/badge/-中文-green)](README.md) [![](https://img.shields.io/badge/-English-green)](README.en.md)

基于 JSON 的简单 RPC 库. \
Simple JSON based RPC library.

> 通过阅读此项目的代码, 你可以学到: 动态代理. 项目主要逻辑代码不超过 300 行. \
> By reading the code of this project, you can learn: Dynamic proxy. The main logic code of the project does not exceed 300 lines.

## 传输 / Transmission

```txt
--> 包头(四字节整数) + {"Method":"方法名","Arg":["参数"]}
<-- 包头(四字节整数) + {"Ret":"返回值","RefRet":["引用返回值"],"Err":"错误信息"}
```
```txt
--> header(four-byte integer) + {"Method":"method name","Arg":["arguments"]}
<-- header(four-byte integer) + {"Ret":"return value","RefRet":["reference returns"],"Err":"error message"}
```

> 注: 当方法正确响应返回值时, Err 字段应该为 null \
> Note: The Err field should be null when the method responds correctly with the return value

## 使用 / Usage

该库可以在 `System.IO.Stream` 上使用 \
This library can be used on `System.IO.Stream`

定义公共的接口(Define the public interface):

```csharp
public interface Commands
{
    public void WriteLine(string message);
    public int Add(int a, int b);
    public int Add114514(ref int num);
}
```

服务端对接口的实现(Server implementation of the interface):

```csharp
internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
    public int Add114514(ref int num) => num += 114514;
    public void WriteLine(string message) => Console.WriteLine("Server print: " + message);
}
```

服务端监听 TCP (Server listening on TCP):

```csharp
int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口 / listen on specified port
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例 / Create a common command call instance
List<RpcServer<Commands>> rpcs = new List<RpcServer<Commands>>();                 // 保存所有客户端 RPC 引用 / Save all client RPC references

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端 / Accept a client
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));        // 创建并保存 RPC 实例 / Create and save an RPC instance
}
```

客户端连接并调用远程函数(The client connects and calls the remote function):

```csharp
Console.Write("Addr: ");
var addr = Console.ReadLine()!;                         // 用户输入地址 / User enters the address

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));                 // 连接到服务器 / Connect to server

RpcClient<Commands> rpc =
    new RpcClient<Commands>(client.GetStream());        // 创建 RPC 客户端实例 / Create an RPC client instance

int num = 10;
rpc.Remote.Add114514(ref num);

if (num == 114524)
    Console.WriteLine("带 ref 参数的 RPC 调用成功");

while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    rpc.Remote.WriteLine(input);                        // 调用服务端 WriteLine 方法 / Call the server WriteLine method
}
```

> 客户端控制台(Client console): \
> Addr: 127.0.0.1:11451 \
> 带 ref 参数的 RPC 调用成功\
> hello \
> this message is from client

> 服务端控制台: \
> Listening 11451 \
> Server print: hello \
> Server print: this message is from client
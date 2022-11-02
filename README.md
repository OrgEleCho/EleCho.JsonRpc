# EleCho.JsonRpc

基于 JSON 的简单 RPC 库.

> 通过阅读此项目的代码, 你可以学到: 动态代理. 项目主要逻辑代码不超过 250 行.

## 传输格式

```txt
--> 包头(四字节整数) + {"Method":"方法名","Arg":["参数"]}
<-- 包头(四字节整数) + {"Ret":"返回值","Err":"错误信息"}
```

> 注: 当方法正确响应返回值时, Err 字段应该为 null

## 使用

该库可以在 `System.IO.Stream` 上使用

定义公共的接口:

```csharp
public interface Commands
{
    public void WriteLine(string message);
    public int Add(int a, int b);
}
```

服务端对接口的实现:

```csharp
internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
    public void WriteLine(string message) => Console.WriteLine("Server print: " + message);
}
```

服务端监听 TCP:

```csharp
int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // 监听指定端口
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // 创建公用的指令调用实例
List<RpcServer<Commands>> rpcs = new List<RpcServer<Commands>>();                 // 保存所有客户端 RPC 引用

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // 接受一个客户端
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));        // 创建并保存 RPC 实例
}
```

客户端连接并调用远程函数:

```csharp
Console.Write("Addr: ");
var addr = Console.ReadLine()!;                         // 用户输入地址

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));                 // 连接到服务器

RpcClient<Commands> rpc =
    new RpcClient<Commands>(client.GetStream());        // 创建 RPC 客户端实例

while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    rpc.Remote.WriteLine(input);                        // 调用服务端 WriteLine 方法
}
```

> 客户端控制台: \
> Addr: 127.0.0.1:11451 \
> hello \
> this message is from client

> 服务端控制台: \
> Listening 11451 \
> Server print: hello \
> Server print: this message is from client
# EleCho.JsonRpc [![](https://img.shields.io/badge/-ÖÐÎÄ-green)](README.md) [![](https://img.shields.io/badge/-English-green)](README.en.md)

Simple JSON based RPC library.

> By reading the code of this project, you can learn: Dynamic proxy. The main logic code of the project does not exceed 400 lines.

## Transmission

```txt
--> header(four-byte integer) + {"Method":"method name","Arg":["arguments"]}
<-- header(four-byte integer) + {"Ret":"return value","RefRet":["reference returns"],"Err":"error message"}
```

> Note: The Err field should be null when the method responds correctly with the return value

## Usage

This library can be used on `System.IO.Stream`

Define the public interface:

```csharp
public interface Commands
{
    public void WriteLine(string message);
    public int Add(int a, int b);
    public int Add114514(ref int num);
}
```

Server implementation of the interface:

```csharp
internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
    public int Add114514(ref int num) => num += 114514;
    public void WriteLine(string message) => Console.WriteLine("Server print: " + message);
}
```

Server listening on TCP:

```csharp
int port = 11451;

TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));      // listen on specified port
listener.Start();

CommandsImpl serverCommands = new CommandsImpl();                                 // Create a common command call instance
List<RpcServer<Commands>> rpcs = new List<RpcServer<Commands>>();                 // Save all client RPC references

Console.WriteLine($"Listening {port}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();                     // Accept a client
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));        // Create and save an RPC instance
}
```

The client connects and calls the remote function:

```csharp
Console.Write("Addr: ");
var addr = Console.ReadLine()!;                         // User enters the address

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));                 // Connect to server

RpcClient<Commands> rpc =
    new RpcClient<Commands>(client.GetStream());        // Create an RPC client instance

int num = 10;
rpc.Remote.Add114514(ref num);

if (num == 114524)
    Console.WriteLine("RPC call with 'ref' succeed");

while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    rpc.Remote.WriteLine(input);                        // Call the server WriteLine method
}
```

> Client console: \
> Addr: 127.0.0.1:11451 \
> RPC call with 'ref' succeed\
> hello \
> this message is from client

> Server console: \
> Listening 11451 \
> Server print: hello \
> Server print: this message is from client
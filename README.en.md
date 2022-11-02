# EleCho.JsonRpc [![](https://img.shields.io/badge/-ÖÐÎÄ-green)](README.md) [![](https://img.shields.io/badge/-English-green)](README.en.md)

Simple JSON based RPC library.

> By reading the code of this project, you can learn: Dynamic proxy. The main logic code of the project does not exceed 250 lines.

## Transmission

```txt
--> header(four-byte integer) + {"Method":"method name","Arg":["arguments"]}
<-- header(four-byte integer) + {"Ret":"return value","Err":"error message"}
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
}
```

Server implementation of the interface:

```csharp
internal class CommandsImpl : Commands
{
    public int Add(int a, int b) => a + b;
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
    TcpClient client = await listener.AcceptTcpClientAsync();                     // accept a client
    rpcs.Add(new RpcServer<Commands>(client.GetStream(), serverCommands));        // Create and save an RPC instance
}
```

The client connects and calls the remote function:

```csharp
Console.Write("Addr: ");
var addr = Console.ReadLine()!;                         // User enters address

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));                 // connect to server

RpcClient<Commands> rpc =
    new RpcClient<Commands>(client.GetStream());        // Create an RPC client instance

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
> hello \
> this message is from client

> Server console: \
> Listening 11451 \
> Server print: hello \
> Server print: this message is from client

using System.IO;
using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;

Console.Write("Addr: ");
var addr = Console.ReadLine();                          // 用户输入地址

if (string.IsNullOrWhiteSpace(addr))
    addr = "127.0.0.1:11451";

TcpClient client = new TcpClient();
client.Connect(ParseIPEndPoint(addr));                 // 连接到服务器
var stream = client.GetStream();

JsonRpc rpc = new JsonRpc(new StreamJsonRpc.NewLineDelimitedMessageHandler(stream, stream, new StreamJsonRpc.JsonMessageFormatter()));
rpc.StartListening();


while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    await rpc.InvokeAsync("WriteLine", input);
}

IPEndPoint ParseIPEndPoint(string addr)
{
    var parts = addr.Split(':');
    if (parts.Length != 2)
        throw new ArgumentException("Invalid address format");

    return new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
}


public interface ICommands
{
    public void WriteLine(string message);
}
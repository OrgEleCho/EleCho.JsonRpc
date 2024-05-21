using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using EleCho.JsonRpc;
using TestCommon;

Console.Write("Addr: ");
var addr = Console.ReadLine();                           // 用户输入地址

if (string.IsNullOrWhiteSpace(addr))
    addr = "127.0.0.1:11451";

TcpClient client = new TcpClient();
client.Connect(ParseIPEndPoint(addr));                   // 连接到服务器

RpcClient<ICommands> rpc =
    new RpcClient<ICommands>(client.GetStream());        // 创建 RPC 客户端实例

int num = 10;
rpc.Remote.Add114514(ref num);

if (num == 114524)
    Console.WriteLine("带 ref 参数的 RPC 调用成功");

Console.WriteLine("当前服务器时间: " + rpc.Remote.DateTimeNow);

while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    rpc.Remote.WriteLine(input);                        // 调用服务端 WriteLine 方法
}

IPEndPoint ParseIPEndPoint(string addr)
{
    var parts = addr.Split(':');
    if (parts.Length != 2)
        throw new ArgumentException("Invalid address format");

    return new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
}
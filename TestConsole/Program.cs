using EleCho.JsonRpc;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TestCommon;

Console.Write("Addr: ");
var addr = Console.ReadLine();                          // 用户输入地址

if (string.IsNullOrWhiteSpace(addr))
    addr = "127.0.0.1:11451";

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));                 // 连接到服务器

RpcClient<Commands> rpc =
    new RpcClient<Commands>(client.GetStream());        // 创建 RPC 客户端实例

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
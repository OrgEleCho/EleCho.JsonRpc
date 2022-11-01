using EleCho.JsonRpc;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TestCommon;

Console.Write("Addr: ");
var addr = Console.ReadLine()!;

TcpClient client = new TcpClient();
client.Connect(IPEndPoint.Parse(addr));

var stream = client.GetStream();
RpcClient<Commands> rpc = new RpcClient<Commands>(stream);

while (true)
{
    var input = Console.ReadLine();
    if (input == null)
        break;

    rpc.Remote.WriteLine(input);
}

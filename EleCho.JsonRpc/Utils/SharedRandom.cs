using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EleCho.JsonRpc.Utils
{
    internal static class SharedRandom
    {
        public static RpcPackageId NextId()
        {
            return RpcPackageId.Create(Guid.NewGuid().ToString());
        }
    }
}

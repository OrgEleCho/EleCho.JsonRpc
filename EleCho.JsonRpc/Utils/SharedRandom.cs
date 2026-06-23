using System;

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

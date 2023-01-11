using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EleCho.JsonRpc
{
    internal class RpcUtils
    {
        public static string GetMethodSignature(MethodInfo method, ParameterInfo[] paramInfos)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(method.Name);

            if (paramInfos.Length > 0)
            {
                sb.Append(':');
                sb.Append(paramInfos[0].ParameterType.FullName);
                for (int i = 1; i < paramInfos.Length; i++)
                {
                    sb.Append(',');
                    sb.Append(paramInfos[i].ParameterType.FullName);
                }
            }
            
            return sb.ToString();
        }

        public static void GetMethodNameAndParameterTypesFromSignature(string signature, out string methodName, out Type[] parameterTypes)
        {
            int colonIndex = signature.IndexOf(':');
            if (colonIndex == -1)
            {
                methodName = signature;
                parameterTypes = new Type[0];
                return;
            }

            methodName = signature.Substring(0, colonIndex);
            string[] paramTypes = signature.Substring(colonIndex + 1).Split(',');
            parameterTypes = new Type[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
                parameterTypes[i] = Type.GetType(paramTypes[i]) ?? throw new Exception("Invalid parameter type");
        }
    }
}

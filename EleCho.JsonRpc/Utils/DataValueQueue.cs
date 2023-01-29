using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EleCho.JsonRpc.Utils
{
    internal class DataValueQueue<T>
    {
        private readonly Queue<T> queue = new Queue<T>();
        private readonly object sync = new object();

        public void Enqueue(T value)
        {
            lock (sync)
            {
                queue.Enqueue(value);
                Monitor.Pulse(sync);
            }
        }

        public T Dequeue()
        {
            lock (sync)
            {
                while (queue.Count == 0)
                    Monitor.Wait(sync);
                return queue.Dequeue();
            }
        }
    }
}

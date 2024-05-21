using System;
using System.Threading.Tasks;

namespace TestCommon
{
    public interface ICommands
    {
        public DateTime DateTimeNow { get; }

        public void WriteLine(string message);
        public int Add(int a, int b);
        public Task<int> AddAsync(int a, int b);
        public int Add114514(ref int num);
        public void ThrowException();
    }
}
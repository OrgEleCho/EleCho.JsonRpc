﻿using System;

namespace TestCommon
{
    public interface ICommands
    {
        public DateTime DateTimeNow { get; }

        public void WriteLine(string message);
        public int Add(int a, int b);
        public int Add114514(ref int num);
    }
}
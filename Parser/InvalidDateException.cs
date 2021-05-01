using System;

namespace subbuzz.Parser
{
    public class InvalidDateException : ApplicationException
    {
        public InvalidDateException(string message, params object[] args) : base(string.Format(message, args))
        {
        }

        public InvalidDateException(string message) : base(message)
        {
        }
    }
}

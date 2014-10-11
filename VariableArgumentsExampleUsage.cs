using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(AmazingSPrintf("I am %s, %d years old, %f meters tall!",
                "Chris", 
                24,
                1.94));
        }

        static string AmazingSPrintf(string format, params VariableArgument[] args)
        {
            if (!args.Any()) 
                return format;

            using (var combinedVariables = new CombinedVariables(args))
            {
                var bufferCapacity = _vscprintf(format, combinedVariables.GetPtr());
                var stringBuilder = new StringBuilder(bufferCapacity + 1);

                vsprintf(stringBuilder, format, combinedVariables.GetPtr());

                return stringBuilder.ToString();
            }
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int vsprintf(
            StringBuilder buffer,
            string format,
            IntPtr ptr);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int _vscprintf(
            string format,
            IntPtr ptr);

    }
}

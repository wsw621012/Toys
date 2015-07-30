using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Security.Cryptography;

namespace ProtecteDataTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string pass = "Tr3ndM!cr0";

            string entry = "File Reputation Service";

            var encData = ProtectedData.Protect(GetBytes(pass), GetBytes(entry), DataProtectionScope.LocalMachine);

            var encString = Convert.ToBase64String(encData);

            Console.WriteLine("encrypted string: {0}", encString);

            var decData = Convert.FromBase64String(encString);

            var inverse = ProtectedData.Unprotect(decData, GetBytes(entry), DataProtectionScope.LocalMachine);

            Console.WriteLine("inverse: {0}", GetString(inverse));

            Console.ReadKey();
        }

        static byte[] GetBytes(string str)
        {
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }

        static string GetString(byte[] bytes)
        {
            char[] chars = new char[bytes.Length / sizeof(char)];
            System.Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }
    }
}

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Diagnostics;

namespace PolyStream.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            using (var testStream = new ModifiableStream())
            {
                var buffer = new byte[] { 0xAA, 0xBB, 0xCC };
                testStream.Write(buffer, 0, buffer.Length);
                buffer = new byte[] { 0xDD, 0xEE, 0xFF };
                testStream.Write(buffer, 0, buffer.Length);
                buffer = new byte[] { 0x12, 0x34, 0x56 };
                testStream.Position = 3;
                testStream.Insert(buffer, 0, buffer.Length);
                PrintStream(testStream);
                testStream.RemoveAt(1, 2);
                PrintStream(testStream);
                testStream.Flush();
                PrintStream(testStream);
            }
        }


        static void PrintStream(Stream stream, long maxLen = 0)
        {
            var origPos = stream.Position;
            stream.Position = 0;
            long totalRead = 0;
            byte[] buffer = new byte[32];
            int byteRead = 0;

            do
            {
                Trace.Write(totalRead.ToString().PadRight(24));
                Trace.Write((totalRead + 8).ToString().PadRight(24));
                Trace.Write((totalRead + 16).ToString().PadRight(24));
                Trace.Write((totalRead + 24).ToString().PadRight(24) + Environment.NewLine);
                byteRead = stream.Read(buffer, 0, buffer.Length);

                for (int i = 0; i < byteRead; i++)
                    Trace.Write(buffer[i].ToString("X2").PadRight(3));
                Trace.Write(Environment.NewLine);

                totalRead += byteRead;
                if (maxLen > 0 && totalRead > maxLen)
                    break;
            }
            while (byteRead >= buffer.Length);
            stream.Position = origPos;
        }
    }
}

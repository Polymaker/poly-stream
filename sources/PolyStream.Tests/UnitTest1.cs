using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace PolyStream.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var beforeSave = new MemoryStream();
            var afterSave = new MemoryStream();

            using (var testStream = new ModifiableStream(CachingMethod.InMemory))
            {
                var buffer = new byte[] { 0xAA, 0xBB, 0xCC };
                testStream.Write(buffer, 0, buffer.Length);
                PrintStream(testStream);

                buffer = new byte[] { 0xDD, 0xEE, 0xFF };
                testStream.Write(buffer, 0, buffer.Length);
                PrintStream(testStream);

                buffer = new byte[] { 0x12, 0x34, 0x56 };
                testStream.InsertAt(3, buffer, 0, buffer.Length);
                PrintStream(testStream);

                testStream.RemoveAt(testStream.Length - 1, 1);
                PrintStream(testStream);

                //testStream.RemoveAt(6, 3);
                //PrintStream(testStream);

                buffer = new byte[] { 0x42, 0x42, 0x42 };
                testStream.Append(buffer, 0, buffer.Length);
                PrintStream(testStream);

                testStream.WriteAt(5, buffer, 0, buffer.Length);
                PrintStream(testStream);

                //testStream.RemoveAt(0, 2, false);
                //PrintStream(testStream);

                //testStream.RemoveAt(0, 2, true);
                //PrintStream(testStream);


                testStream.Position = 0;
                testStream.CopyTo(beforeSave);

                testStream.Flush();

                testStream.Position = 0;
                testStream.CopyTo(afterSave);
            }

            AssertStreamEquals(beforeSave, afterSave);
        }

        private static void AssertStreamEquals(Stream stream1, Stream stream2)
        {
            if (stream1.Length != stream2.Length)
                Assert.Fail("Streams have not the same length");

            var buff1 = new byte[256];
            var buff2 = new byte[256];

            int byteRead = 0;

            do
            {
                int br1 = stream1.Read(buff1, 0, 256);
                int br2 = stream2.Read(buff2, 0, 256);
                if (br1 != br2)
                    Assert.Fail("Read amount not equal");

                if (!buff1.SequenceEqual(buff2))
                    Assert.Fail("Read data not equal");
                byteRead = br1;

            }
            while (byteRead > 0);
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
                if (origPos > totalRead && origPos - totalRead < 32)
                    Trace.WriteLine(string.Empty.PadRight((int)(origPos - totalRead) * 3) + "^");
                totalRead += byteRead;
                if (maxLen > 0 && totalRead > maxLen)
                    break;
            }
            while (byteRead >= buffer.Length);
            stream.Position = origPos;
        }
    }
}

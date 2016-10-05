using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolyStream
{
    public class TempFileCache : StreamCache
    {
        private FileStream cacheStream;

        public const long DefaultMaxBuffer = 1024 * 1024 * 1024;

        public override long Length
        {
            get { return cacheStream.Length; }
        }

        public TempFileCache()
        {
            cacheStream = File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite);
        }

        public override int Read(long position, byte[] buffer, int bufferOffset, int count)
        {
            cacheStream.Seek(position, SeekOrigin.Begin);
            return cacheStream.Read(buffer, bufferOffset, count);
        }

        internal override void Append(byte[] buffer, int bufferOffset, int count)
        {
            cacheStream.Seek(0, SeekOrigin.End);
            cacheStream.Write(buffer, bufferOffset, count);
            cacheStream.Flush();
        }

        internal override void Write(long position, byte[] buffer, int bufferOffset, int count)
        {
            cacheStream.Seek(position, SeekOrigin.Begin);
            cacheStream.Write(buffer, bufferOffset, count);
            cacheStream.Flush();
        }

        ~TempFileCache()
        {
            Dispose();
        }

        public override void Dispose()
        {
            if (cacheStream != null)
            {
                cacheStream.Dispose();
                File.Delete(cacheStream.Name);
                cacheStream = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolyStream
{
    public class MemoryCache : StreamCache
    {
        private MemoryStream cacheStream;

        public override long Length
        {
            get { return cacheStream.Length; }
        }

        public MemoryCache()
        {
            cacheStream = new MemoryStream();
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
        }

        internal override void Write(long position, byte[] buffer, int bufferOffset, int count)
        {
            cacheStream.Seek(position, SeekOrigin.Begin);
            cacheStream.Write(buffer, bufferOffset, count);
        }

        ~MemoryCache()
        {
            Dispose();
        }

        public override void Dispose()
        {
            if (cacheStream != null)
            {
                cacheStream.Dispose();
                cacheStream = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}

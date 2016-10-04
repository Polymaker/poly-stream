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

        public override long Length
        {
            get { return cacheStream.Length; }
        }

        public TempFileCache()
        {
            cacheStream = File.OpenWrite(Path.GetTempFileName());
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

        public override void Dispose()
        {
            if (cacheStream != null)
            {
                cacheStream.Dispose();
                File.Delete(cacheStream.Name);
                cacheStream = null;
            }
        }
    }
}

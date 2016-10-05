using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolyStream
{
    public abstract class StreamCache : IDisposable
    {
        internal int Index;

        public abstract long Length { get; }

        public abstract int Read(long position, byte[] buffer, int bufferOffset, int count);

        internal abstract void Append(byte[] buffer, int bufferOffset, int count);

        internal abstract void Write(long position, byte[] buffer, int bufferOffset, int count);

        public abstract void Dispose();
    }
}

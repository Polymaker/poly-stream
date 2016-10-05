using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolyStream
{
    public class ModifiableStream : Stream
    {

        private readonly CachingMethod _CachingMethod;
        private LinkedList<Segment> Segments;
        private Stream Source;
        private List<StreamCache> CachedData;
        private long _Position;
        private long _Length;
        private bool isSimpleStream;

        #region Properties...

        public override bool CanRead
        {
            get { return Source.CanRead; }
        }

        public override bool CanSeek
        {
            get { return Source.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return Source.CanWrite; }
        }

        public override long Length
        {
            get { return _Length; }
        }

        public override long Position
        {
            get { return _Position; }
            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }
        
        public CachingMethod CachingMethod
        {
            get { return _CachingMethod; }
        }

        #endregion

        #region Ctor & initialization

        public ModifiableStream() : this(new MemoryStream(), CachingMethod.InMemory) { }

        public ModifiableStream(CachingMethod cachingMethod) : this(new MemoryStream(), cachingMethod) { }

        public ModifiableStream(Stream source) : this(source, CachingMethod.InMemory) { }

        public ModifiableStream(Stream source, CachingMethod cachingMethod)
        {
            ValidateStream(source);
            Source = source;
            _CachingMethod = cachingMethod;
            _Length = source.Length;
            _Position = source.Position;
            Segments = new LinkedList<Segment>();
            CachedData = new List<StreamCache>();
            InitFirstSegment();
        }

        private static void ValidateStream(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("Source stream mustnot be null.");

            if (stream is ModifiableStream)
                throw new NotSupportedException("Don't do that. Just don't.");

            if (!(stream.CanRead && stream.CanSeek && stream.CanWrite))

                throw new InvalidDataException("Source stream must be readable, seekable and writable.");
            try
            {
                stream.SetLength(stream.Length + 1);
                stream.SetLength(stream.Length - 1);
            }
            catch (NotImplementedException)
            {
                throw new InvalidDataException("Source stream must implement SetLength.");
            }
            catch (NotSupportedException)
            {
                throw new InvalidDataException("Source stream must support SetLength.");
            }
            catch
            {
                throw new InvalidDataException("Source stream must allow resizing.");
            }
        }

        private void InitFirstSegment()
        {
            Segments.Clear();
            isSimpleStream = true;
            if (Source.Length > 0)
                Segments.AddFirst(new Segment(Source.Length));
        }

        #endregion      

        public override void Flush()
        {
            if (Segments.Count > 0)
            {
                if (Length > Source.Length)
                    Source.SetLength(Length);//expand before

                var buffer = new byte[1024];

                //FIRST PASS: start by writing segments from source stream in reverse order
                
                var currentNode = Segments.Last;
                long currentPos = Length;

                do
                {
                    currentPos -= currentNode.Value.Length;
                    if (currentNode.Value.CacheIndex < 0)
                        WriteSegment(currentPos, currentNode.Value, 0);
                    
                }
                while ((currentNode = currentNode.Previous) != null);

                //SECOND PASS: write remaining segments

                currentNode = Segments.First;
                currentPos = 0;
                do
                {
                    if (currentNode.Value.CacheIndex >= 0)
                        WriteSegment(currentPos, currentNode.Value, 0);
                    currentPos += currentNode.Value.Length;
                }
                while ((currentNode = currentNode.Next) != null);

                if (Length < Source.Length)
                    Source.SetLength(Length);//shrink after
            }
            ClearCache();
            Source.Flush();
            _Length = Source.Length;
            InitFirstSegment();
        }

        private void WriteSegment(long sourcePosition, Segment segment, long segmentOffset)
        {
            var buffer = new byte[1024];
            int byteRead = 0;
            Source.Seek(sourcePosition, SeekOrigin.Begin);
            do
            {
                byteRead = ReadSegment(segment, buffer, 0, 1024, segmentOffset);
                segmentOffset += byteRead;
                Source.Write(buffer, 0, byteRead);
            }
            while (byteRead >= buffer.Length);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current)
                offset = Position + offset;
            else if (origin == SeekOrigin.End)
                offset = Length /*- 1*/ - offset;

            if (offset < 0)
                offset = 0;

            if (offset >/*=*/ Length)
                offset = Length/* - 1*/;

            _Position = offset;

            return _Position;
        }

        public override void SetLength(long value)
        {
            if (value > Length)
            {
                //append
            }
            else if (value < Length)
            {
                //remove
                //_Position = Math.Min(_Position, Length);
            }
        }

        #region Segment manipulation

        private SegmentLocation FindSegment(long position)
        {

            if (position > Length || Segments.Count == 0)
                return null;

            var currentNode = Segments.First;
            long currentPos = 0;
            do
            {
                if (position >= currentPos && position < currentPos + currentNode.Value.Length)
                {
                    return new SegmentLocation(currentNode, currentPos, position - currentPos);
                }
                currentPos += currentNode.Value.Length;
            }
            while ((currentNode = currentNode.Next) != null);

            return null;
        }

        private bool GetSegmentsFromTo(long startPos, long endPos, out SegmentLocation first, out SegmentLocation last)
        {
            first = FindSegment(startPos);
            last = null;

            if (first == null)
                return false;

            if (endPos < Length)
            {
                long currentPos = first.StreamPosition;
                var currentNode = first.Node;

                while (currentPos < endPos && currentNode != null)
                {
                    if (endPos <= currentPos + currentNode.Value.Length)
                    {
                        last = new SegmentLocation(currentNode, currentPos, endPos - currentPos);
                        break;
                    }
                    currentPos += currentNode.Value.Length;
                    currentNode = currentNode.Next;
                }

                if (last.Node == null)
                    first = null;
            }
            else
            {
                last = new SegmentLocation(Segments.Last, Length - Segments.Last.Value.Length, Segments.Last.Value.Length - 1);
            }

            return first != null && last != null;
        }

        private static void SplitSegment(Segment segment, long splitPos, out Segment left, out Segment right)
        {
            left = new Segment(splitPos, segment.CacheIndex, segment.CacheOffset);
            right = new Segment(segment.Length - splitPos, segment.CacheIndex, segment.CacheOffset + splitPos);
        }

        private void InsertSegment(long position, Segment newSegment)
        {
            var segmentAtPosition = FindSegment(position);
            if (segmentAtPosition == null)
            {
                //if (position != Length)
                //    throw new Exception();
                Segments.AddLast(newSegment);
            }
            else if (segmentAtPosition.LocalPosition == 0)
            {
                Segments.AddBefore(segmentAtPosition.Node, newSegment);
            }
            else
            {
                Segment left, right;
                var splitPos = position - segmentAtPosition.StreamPosition;
                SplitSegment(segmentAtPosition.Segment, splitPos, out left, out right);
                Segments.AddBefore(segmentAtPosition.Node, left);
                Segments.AddBefore(segmentAtPosition.Node, newSegment);
                segmentAtPosition.Node.Value = right;
            }

            TryKeepSimple();

            _Length += newSegment.Length;
        }

        private void RemoveSegment(long position, long length)
        {
            SegmentLocation start, end;
            if (!GetSegmentsFromTo(position, position + length, out start, out end))
                throw new Exception();

            RemoveSegment(start, end);
            TryKeepSimple();
        }

        private void RemoveSegment(SegmentLocation start, SegmentLocation end)
        {
            Segment left = Segment.Invaild, right = Segment.Invaild, dummy;

            if (start.LocalPosition > 0/* && start.LocalPosition < start.Segment.Length - 1*/)
                SplitSegment(start.Segment, start.LocalPosition, out left, out dummy);

            if (end.LocalPosition > 0 && end.LocalPosition < end.Segment.Length)
                SplitSegment(end.Segment, end.LocalPosition, out dummy, out right);

            var nodesToRemove = Segments.GetNodesInBetween(start.Node, end.Node);
            _Length -= nodesToRemove.Sum(n => n.Value.Length);

            if (left != Segment.Invaild)
            {
                Segments.AddBefore(start.Node, left);
                _Length += left.Length;
            }

            if (right != Segment.Invaild)
            {
                Segments.AddAfter(end.Node, right);
                _Length += right.Length;
            }

            Segments.RemoveRange(nodesToRemove);
        }

        private void OverwriteSegment(long position, long length, Segment newSegment)
        {
            if ((Length == 0 && position == 0) || position == Length)
            {
                InsertSegment(position, newSegment);
                return;
            }
            SegmentLocation start, end;
            if (!GetSegmentsFromTo(position, position + length, out start, out end))
                throw new Exception();

            RemoveSegment(start, end);
            InsertSegment(position, newSegment);
        }

        private void TryKeepSimple()
        {
            if (isSimpleStream)
            {
                if (!Segments.All(s => s.CacheIndex == -1))
                {
                    isSimpleStream = false;
                    return;
                }

                if (Segments.Count <= 1)
                    return;

                var currentNode = Segments.First;
                
                while (true)
                {
                    if (currentNode.Next == null)
                        break;
                    if (currentNode.Next.Value.CacheOffset != currentNode.Value.CacheOffset + currentNode.Value.Length)
                    {
                        isSimpleStream = false;
                        break;
                    }
                    currentNode = currentNode.Next;
                }

                if (isSimpleStream)
                {
                    Segments.Clear();
                    Segments.AddFirst(new Segment(Source.Length));
                }
                
            }
        }

        #endregion

        #region Read

        public override int Read(byte[] buffer, int offset, int count)
        {
            var firstSegment = FindSegment(Position);
            if (firstSegment == null)
                return 0;

            int byteRead = ReadSegment(firstSegment.Segment, buffer, offset, count, firstSegment.LocalPosition);

            var currentNode = firstSegment.Node;

            while (byteRead < count && (currentNode = currentNode.Next) != null)
            {
                int byteRemaining = count - byteRead;
                byteRead += ReadSegment(currentNode.Value, buffer, offset + byteRead, byteRemaining, 0);
            }

            _Position += byteRead;
            return byteRead;
        }

        private int ReadSegment(Segment segment, byte[] buffer, int bufferOffset, int count, long segmentOffset)
        {
            count = (int)Math.Min(count, segment.Length - segmentOffset);

            if (segment.CacheIndex < 0)
            {
                long currentPos = Source.Position;
                try
                {
                    Source.Seek(segment.CacheOffset + segmentOffset, SeekOrigin.Begin);
                    return Source.Read(buffer, bufferOffset, count);
                }
                finally
                {
                    Source.Position = currentPos;
                }
            }
            else
            {
                return CachedData[segment.CacheIndex].Read(segment.CacheOffset + segmentOffset, buffer, bufferOffset, count);
            }
        }

        #endregion

        #region Modification operations (Write/Overwrite, Insert, Remove & Append


        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAt(Position, buffer, offset, count);
            _Position += count;
        }

        public void WriteAt(long position, byte[] buffer, int offset, int count)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");

            Segment newSegment = default(Segment);

            if (isSimpleStream)
            {
                Source.Seek(position, SeekOrigin.Begin);
                Source.Write(buffer, offset, count);
                newSegment = new Segment(count, -1, position);
            }
            else
                newSegment = WriteToCache(buffer, offset, count);

            OverwriteSegment(position, newSegment.Length, newSegment);
        }

        public void Insert(byte[] buffer, int offset, int count)
        {
            InsertAt(Position, buffer, offset, count);
            _Position += count;
        }

        public void InsertAt(long position, byte[] buffer, int offset, int count)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");

            if (position == Length || (position == 0 && Length == 0))
            {
                WriteAt(position, buffer, offset, count);
                return;
            }

            var newSegment = WriteToCache(buffer, offset, count);
            InsertSegment(position, newSegment);
        }

        public void Remove(int count)
        {
            RemoveAt(Position, count);
        }

        public void RemoveAt(long position, int count)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");

            if (Segments.Count == 0/* || Length == 0*/)
            {
                //throw new Exception();
                return;
            }
            RemoveSegment(position, count);
        }

        private Segment WriteToCache(byte[] buffer, int offset, int count)
        {
            StreamCache currentCache = null;
            if (CachedData.Count == 0)
                currentCache = CreateCache();
            else
            {
                //TODO: implement cache max length
                currentCache = CachedData[0];
            }
            var segmentStartOffset = currentCache.Length;
            currentCache.Append(buffer, offset, count);
            return new Segment(count, currentCache.Index, segmentStartOffset);
        }

        private StreamCache CreateCache()
        {
            StreamCache newCache;
            if (CachingMethod == CachingMethod.InMemory)
                newCache = new MemoryCache();
            else
                newCache = new TempFileCache();
            newCache.Index = CachedData.Count;
            CachedData.Add(newCache);
            return newCache;
        }

        #endregion

        #region De-ctors

        ~ModifiableStream()
        {
            Dispose();
        }

        private void ClearCache()
        {
            CachedData.ForEach(c => c.Dispose());
            CachedData.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            ClearCache();

            if (Source != null)
            {
                Source.Dispose();
                Source = null;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Classes & structs

        public struct Segment
        {
            public static readonly Segment Invaild = default(Segment);

            public long Length;

            /// <remarks>A value of -1 means that the data resides in the source stream</remarks>
            public int CacheIndex;
            public long CacheOffset;

            public Segment(long length)
            {
                Length = length;
                CacheIndex = -1;
                CacheOffset = 0L;
            }

            public Segment(long length, int cacheIndex, long cacheOffset)
            {
                Length = length;
                CacheIndex = cacheIndex;
                CacheOffset = cacheOffset;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(this, Invaild) || ReferenceEquals(obj, Invaild))
                    return ReferenceEquals(this, Invaild) && ReferenceEquals(obj, Invaild);
                if (!(obj is Segment) || ReferenceEquals(obj, null))
                    return false;
                var other = (Segment)obj;
                return CacheIndex == other.CacheIndex && Length == other.Length && CacheOffset == other.CacheOffset;
            }

            public override int GetHashCode()
            {
                unchecked // Overflow is fine, just wrap
                {
                    int hash = 17;
                    hash = hash * 23 + CacheIndex.GetHashCode();
                    hash = hash * 23 + CacheOffset.GetHashCode();
                    hash = hash * 23 + Length.GetHashCode();
                    return hash;
                }
            }

            public static bool operator ==(Segment s1, Segment s2)
            {
                return s1.Equals(s2);
            }

            public static bool operator !=(Segment s1, Segment s2)
            {
                return !(s1 == s2);
            }
        }

        private class SegmentLocation
        {
            public LinkedListNode<Segment> Node { get; set; }
            public Segment Segment
            {
                get { return Node != null ? Node.Value : default(Segment); }
            }
            public long StreamPosition { get; set; }
            public long LocalPosition { get; set; }


            public SegmentLocation(LinkedListNode<Segment> node, long streamPosition, long localPosition)
            {
                Node = node;
                StreamPosition = streamPosition;
                LocalPosition = localPosition;
            }
        }

        #endregion
    }
}

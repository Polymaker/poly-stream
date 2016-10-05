using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolyStream
{
    public class ModifiableStream : Stream
    {
        private long _MaximumCacheSize;
        private readonly CachingMethod _CachingMethod;
        private LinkedList<Segment> Segments;
        private Stream Source;
        private List<StreamCache> CachedData;
        private long _Position;
        private long _Length;

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

        public bool SourceOwner { get; set; }

        public CachingMethod CachingMethod
        {
            get { return _CachingMethod; }
        }

        public long MaximumCacheSize
        {
            get { return _MaximumCacheSize; }
        }

        #endregion

        #region Ctor & initialization

        public ModifiableStream() : this(new MemoryStream(), CachingMethod.InMemory)
        {
            SourceOwner = true;
        }

        public ModifiableStream(CachingMethod cachingMethod) : this(new MemoryStream(), cachingMethod)
        {
            SourceOwner = true;
        }

        public ModifiableStream(Stream source) : this(source, CachingMethod.InMemory) { }

        public ModifiableStream(Stream source, CachingMethod cachingMethod)
        {
            ValidateStream(source);
            Source = source;
            _CachingMethod = cachingMethod;
            _Length = source.Length;
            _Position = source.Position;

            if (cachingMethod == CachingMethod.InMemory)
                _MaximumCacheSize = MemoryCache.DefaultMaxBuffer;
            else
                _MaximumCacheSize = TempFileCache.DefaultMaxBuffer;

            Segments = new LinkedList<Segment>();
            CachedData = new List<StreamCache>();
            InitFirstSegment();
        }

        public ModifiableStream(Stream source, CachingMethod cachingMethod, long maximumCacheSize)
        {
            ValidateStream(source);
            Source = source;
            _CachingMethod = cachingMethod;
            _Length = source.Length;
            _MaximumCacheSize = maximumCacheSize;
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
            if (Source.Length > 0)
                Segments.AddFirst(new Segment(Source.Length));
        }

        #endregion      

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
                if (position >= currentPos && position < currentPos + currentNode.Value.Length/* + (currentNode.Value.Length == 0 ? 1 : 0)*/)
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
                last = new SegmentLocation(Segments.Last, Length - Segments.Last.Value.Length, Segments.Last.Value.Length/* - 1*/);
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
            LinkedListNode<Segment> newNode = null;

            if (segmentAtPosition == null)//this either means that this is the first write or that we're at the end of the stream (appending)
            {
                //if (position != Length)
                //    throw new Exception();
                newNode = Segments.AddLast(newSegment);
            }
            else if (segmentAtPosition.LocalPosition == 0)
            {
                newNode = Segments.AddBefore(segmentAtPosition.Node, newSegment);
            }
            //this condition should not happen normally and should only occur if for some reason FindSegment returns the last segment instead of null
            else if (segmentAtPosition.LocalPosition == segmentAtPosition.Segment.Length)
            {
                //in this situation AddLast would technically work too, 
                //but if for some other unknown reason there's really a segment/node after the segment returned, then AddAfter will work as expected
                newNode = Segments.AddAfter(segmentAtPosition.Node, newSegment);
            }
            else
            {
                Segment left, right;
                var splitPos = position - segmentAtPosition.StreamPosition;
                SplitSegment(segmentAtPosition.Segment, splitPos, out left, out right);
                Segments.AddBefore(segmentAtPosition.Node, left);
                newNode = Segments.AddBefore(segmentAtPosition.Node, newSegment);
                segmentAtPosition.Node.Value = right;
            }

            _Length += newNode.Value.Length;

            TryMergeSegment(ref newNode);
        }

        private long RemoveSegment(long position, long length)
        {
            SegmentLocation start, end;
            if (!GetSegmentsFromTo(position, position + length, out start, out end))
                throw new Exception();

            return RemoveSegment(start, end);
        }

        private long RemoveSegment(SegmentLocation start, SegmentLocation end)
        {
            Segment left = Segment.Invaild, right = Segment.Invaild, dummy;

            if (start.LocalPosition > 0/* && start.LocalPosition < start.Segment.Length - 1*/)
                SplitSegment(start.Segment, start.LocalPosition, out left, out dummy);

            if (end.LocalPosition > 0 && end.LocalPosition < end.Segment.Length)
                SplitSegment(end.Segment, end.LocalPosition, out dummy, out right);

            var nodesToRemove = Segments.GetNodesInBetween(start.Node, end.Node);
            long amountRemoved = nodesToRemove.Sum(n => n.Value.Length);

            //_Length -= nodesToRemove.Sum(n => n.Value.Length);

            if (left != Segment.Invaild)
            {
                Segments.AddBefore(start.Node, left);
                //_Length += left.Length;
                amountRemoved -= left.Length;
            }

            if (right != Segment.Invaild)
            {
                Segments.AddAfter(end.Node, right);
                //_Length += right.Length;
                amountRemoved -= right.Length;
            }
            _Length -= amountRemoved;

            Segments.RemoveRange(nodesToRemove);

            return amountRemoved;
        }

        private void OverwriteSegment(long position, long length, Segment newSegment)
        {
            if (/*(Length == 0 && position == 0) || */position == Length)//we are appending
            {
                InsertSegment(/*0*/position, newSegment);
                return;
            }
            SegmentLocation start, end;
            if (!GetSegmentsFromTo(position, position + length, out start, out end))
                throw new Exception();

            RemoveSegment(start, end);
            InsertSegment(position, newSegment);
        }

        private void TryMergeSegment(ref LinkedListNode<Segment> segmentNode)
        {
            segmentNode = TryMergeSegments(segmentNode.Previous, segmentNode) ?? segmentNode;
            segmentNode = TryMergeSegments(segmentNode, segmentNode.Next) ?? segmentNode;
        }

        private LinkedListNode<Segment> TryMergeSegments(LinkedListNode<Segment> left, LinkedListNode<Segment> right)
        {
            if (left == null || right == null)
                return null;

            if (left.Value.CacheIndex != right.Value.CacheIndex)
                return null;

            if (right.Value.CacheOffset == left.Value.CacheOffset + left.Value.Length)
            {
                left.Value = new Segment(left.Value.Length + right.Value.Length, left.Value.CacheIndex, left.Value.CacheOffset);
                Segments.Remove(right);
                return left;
            }
            return null;
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

        #region Modification operations (Write/Overwrite, Insert, Remove & Append)

        /// <summary>
        /// Writes a block of bytes to this stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">The buffer containing data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAt(Position, buffer, offset, count);
            _Position += count;
        }

        /// <summary>
        /// Writes a block of bytes to this stream at the specified position and does not change the current position within this stream.
        /// </summary>
        /// <param name="position">The position within the stream to write at.</param>
        /// <param name="buffer">The buffer containing data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public void WriteAt(long position, byte[] buffer, int offset, int count)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");

            Segment newSegment = default(Segment);

            //A segment count of 0 means that the source stream was/is empty
            if (Segments.Count == 0)//first write can be directly done to the stream 
            {
                Source.Seek(position, SeekOrigin.Begin);
                newSegment = WriteToSource(buffer, offset, count);
                goto PerformWrite;
            }

            if (position == Length)//we are appending data to the end of the (virtual) stream
            {
                var lastSegment = Segments.Last.Value;
                var segmentCacheLength = GetCacheLength(lastSegment.CacheIndex);

                //check if we can append the data at the same location as the last segement 
                //will append direclty to the source stream when applicable instead of caching the data (matters if the source is on-disk and the cache is in memory)
                //or will append to the last segment's cache if there is still room (prevents creating new cache and linear cache is better for performance)

                if (lastSegment.CacheOffset + lastSegment.Length == segmentCacheLength && //the segment to be overwrited is the last that was written to it's cache
                    CanWriteToCache(lastSegment.CacheIndex, segmentCacheLength, count))//check if there's still room in the segment's cache
                {
                    newSegment = OverwriteCache(lastSegment, segmentCacheLength, buffer, offset, count);
                    goto PerformWrite;
                }

            }

            var foundLocation = FindSegment(position);
            if (foundLocation != null)//we will be overwriting something
            {
                var foundSegment = foundLocation.Segment;
                var segmentCacheLength = GetCacheLength(foundLocation.Segment.CacheIndex);
                var newCacheOffset = foundSegment.CacheOffset + foundLocation.LocalPosition;

                //(over)write the data directly inside the segment's cached (or source) data if it does not overflow into the data of another segement
                //doing so will prevent appending data to the cache if we're writing at the same place multiple times

                if ((foundSegment.CacheOffset + foundSegment.Length == segmentCacheLength ||//the segment to be overwrited is the last that was written to it's cache
                    foundLocation.LocalPosition + count <= foundSegment.Length) &&//the overwrited portion does not exceed the segment cached data length
                    CanWriteToCache(foundSegment.CacheIndex, newCacheOffset, count))//check if there's still room in the segment's cache
                {
                    newSegment = OverwriteCache(foundSegment, newCacheOffset, buffer, offset, count);
                    goto PerformWrite;
                }
            }

            newSegment = WriteToCache(buffer, offset, count);

            PerformWrite:
            OverwriteSegment(position, newSegment.Length, newSegment);
        }

        /// <summary>
        /// Writes a block of bytes to the end of this stream. The current position within this stream is not affected.
        /// </summary>
        /// <param name="buffer">The buffer containing data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public void Append(byte[] buffer, int offset, int count)
        {
            WriteAt(Length, buffer, offset, count);
        }

        [EditorBrowsable( EditorBrowsableState.Advanced)]
        public void AppendAt(long position, byte[] buffer, int offset, int count)//for the sake of continuity I had to add this function too...
        {
            throw new DivideByZeroException("Appending data at a specified position is called 'Insert' you moron.");
        }

        /// <summary>
        /// Inserts a block of bytes to this stream at the current position and advances by the number of bytes written.
        /// </summary>
        /// <param name="buffer">The buffer containing data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public void Insert(byte[] buffer, int offset, int count)
        {
            InsertAt(Position, buffer, offset, count);
            _Position += count;
        }

        /// <summary>
        /// Inserts a block of bytes to this stream at the specified position and does not change the current position within this stream.
        /// </summary>
        /// <param name="position">The position within the stream to insert at.</param>
        /// <param name="buffer">The buffer containing data to write to the stream.</param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer" /> at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public void InsertAt(long position, byte[] buffer, int offset, int count, bool offsetPosition = false)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");

            if (position == Length)
            {
                WriteAt(position, buffer, offset, count);
                return;
            }

            bool repositionStream = Position > position && offsetPosition;

            var newSegment = WriteToCache(buffer, offset, count);
            InsertSegment(position, newSegment);

            if (repositionStream)
                _Position += newSegment.Length;
        }

        /// <summary>
        /// Removes the specified number of bytes at the current position within this stream.
        /// </summary>
        /// <param name="count">The number of bytes to be removed from the stream.</param>
        public void Remove(int count)
        {
            RemoveAt(Position, count);
        }

        /// <summary>
        /// Removes the specified number of bytes at the specified position within this stream.
        /// </summary>
        /// <param name="position">The position within the stream to remove at.</param>
        /// <param name="count">The number of bytes to be removed from the stream.</param>
        public void RemoveAt(long position, int count, bool offsetPosition = false)
        {
            if (position > Length)
                throw new IndexOutOfRangeException("position");
            
            if (Segments.Count == 0/* || Length == 0*/)
            {
                //throw new Exception();
                return;
            }

            bool repositionStream = Position > position/* && offsetPosition*/;
            long positionOffset = repositionStream ? Position - position : 0;

            var removedLength = RemoveSegment(position, count);

            if (repositionStream)
            {
                if (offsetPosition)
                {
                    if (positionOffset <= removedLength)
                        _Position = position;
                    else
                        _Position -= positionOffset - removedLength;
                }
                else
                    _Position = Math.Min(_Position, Length);
            }
        }

        #endregion

        #region Cache management and segment creation

        private Segment WriteToCache(byte[] buffer, int offset, int count)
        {
            StreamCache currentCache = null;

            if (CachedData.Count == 0 || !CanAppendToCache(CachedData.Count - 1, count))
                currentCache = CreateCache();
            else
                currentCache = CachedData[CachedData.Count - 1];

            var segmentStartOffset = currentCache.Length;
            currentCache.Append(buffer, offset, count);
            return new Segment(count, currentCache.Index, segmentStartOffset);
        }

        private Segment WriteToSource(byte[] buffer, int offset, int count)
        {
            var segmentStartOffset = Source.Position;
            Source.Write(buffer, offset, count);
            return new Segment(count, -1, segmentStartOffset);
        }

        private Segment OverwriteCache(Segment segment, long position, byte[] buffer, int offset, int count)
        {
            //MAKE SURE TO HAVE ENCLOSED THIS METHOD CALL IN AN IF BLOCK CALLING CanWriteToCache OR BE PREPARED FOR THE CONSEQUENCES
            if (segment.CacheIndex < 0)
            {
                Source.Seek(position, SeekOrigin.Begin);
                Source.Write(buffer, offset, count);
                return new Segment(count, -1, position);
            }
            else
            {
                CachedData[segment.CacheIndex].Write(position, buffer, offset, count);
                return new Segment(count, segment.CacheIndex, position);
            }
        }

        private long GetCacheLength(int index)
        {
            if (index < 0)
                return Source.Length;
            return CachedData[index].Length;
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

        private bool CanAppendToCache(int cacheIndex, long dataLength)
        {
            if (cacheIndex < 0)
                return true;

            return CanWriteToCache(cacheIndex, GetCacheLength(cacheIndex), dataLength);
        }

        private bool CanWriteToCache(int cacheIndex, long position, long dataLength)
        {
            if (cacheIndex < 0)
                return true;
            
            return position + dataLength < MaximumCacheSize;
        }

        #endregion

        #region Final writing (Flush)

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
                if (SourceOwner)
                    Source.Dispose();
                Source = null;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Classes & structs

        [Serializable]
        public struct Segment
        {
            [NonSerialized]
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

            /// <summary>
            /// The segment starting position inside the stream
            /// </summary>
            public long StreamPosition { get; set; }

            /// <summary>
            /// The position relative to the start of the segment.
            /// </summary>
            public long LocalPosition { get; set; }

            /// <summary>
            /// The requested position.
            /// </summary>
            public long StreamOffset
            {
                get { return StreamPosition + LocalPosition; }
            }

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

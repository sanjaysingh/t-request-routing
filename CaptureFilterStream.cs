using System;
using System.IO;
using System.Web;

namespace RequestRouting
{
    /// <summary>
    /// A stream filter that captures the response body while passing it through to the original filter.
    /// Used for intercepting the response content for comparison purposes.
    /// </summary>
    public class CaptureFilterStream : Stream
    {
        private readonly Stream _originalFilter;
        private readonly MemoryStream _captureStream;

        /// <summary>
        /// Initializes a new instance of the CaptureFilterStream.
        /// </summary>
        /// <param name="originalFilter">The original response filter to wrap</param>
        /// <exception cref="ArgumentNullException">Thrown if originalFilter is null</exception>
        public CaptureFilterStream(Stream originalFilter)
        {
            _originalFilter = originalFilter ?? throw new ArgumentNullException(nameof(originalFilter));
            _captureStream = new MemoryStream();
        }

        /// <summary>
        /// Gets the captured data as a byte array.
        /// </summary>
        public byte[] GetCapturedData() => _captureStream.ToArray();

        #region Stream Implementation

        public override bool CanRead => _originalFilter.CanRead;
        public override bool CanSeek => _originalFilter.CanSeek;
        public override bool CanWrite => _originalFilter.CanWrite;
        public override long Length => _originalFilter.Length;
        public override long Position
        {
            get => _originalFilter.Position;
            set => _originalFilter.Position = value;
        }

        public override void Flush() => _originalFilter.Flush();

        public override long Seek(long offset, SeekOrigin origin) => 
            _originalFilter.Seek(offset, origin);

        public override void SetLength(long value) => _originalFilter.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count) => 
            _originalFilter.Read(buffer, offset, count);

        /// <summary>
        /// Writes data to both the capture stream and the original filter.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            // Capture the data being written
            _captureStream.Write(buffer, offset, count);

            // Pass the data to the original filter
            _originalFilter.Write(buffer, offset, count);
        }

        /// <summary>
        /// Ensures captured stream is properly disposed.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captureStream?.Dispose();
                // Don't dispose _originalFilter here, ASP.NET manages its lifetime
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Flushes any buffered data and closes the stream.
        /// </summary>
        public override void Close()
        {
            _captureStream.Flush();
            _originalFilter.Close();
            base.Close();
        }

        #endregion
    }
} 
using NPOI.SS.Formula.Functions;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class ReaderStream : Stream
    {
        readonly long _length;
        long _position;
        readonly Stream _stream;
        bool _isDisposed;

        public ReaderStream(Stream  stream, long length)
        {
            _stream = stream;
            _length = length;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
            }
            base.Dispose(disposing);
            _isDisposed = true;
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _stream.Position = value;
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesOfRead = await _stream.ReadAsync(buffer, offset, count, cancellationToken);
            this._position += bytesOfRead;
            return bytesOfRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesOfRead = await _stream.ReadAsync(buffer, cancellationToken);
            this._position += bytesOfRead;
            return bytesOfRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(Length);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}

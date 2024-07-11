using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeFilePipleReaderStream : Stream
    {
        readonly long _length;
        readonly PipeReader _pipeReader;
        readonly Stream _stream;

        public NodeFilePipleReaderStream(PipeReader pipeReader, long length)
        {
            _pipeReader = pipeReader;
            _stream = pipeReader.AsStream();
            _length = length;
        }

        public override async ValueTask DisposeAsync()
        {
            await _pipeReader.CompleteAsync();
            await base.DisposeAsync();
        }

        public override bool CanRead => _stream.CanRead;

        public override bool CanSeek => _stream.CanSeek;

        public override bool CanWrite => _stream.CanWrite;

        public override long Length => _length;

        public override long Position { get => _stream.Position; set => _stream.Position = value; }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
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
    }
}

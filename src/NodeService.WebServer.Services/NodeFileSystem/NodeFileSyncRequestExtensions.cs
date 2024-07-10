using NodeService.Infrastructure.NodeFileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class NodeFileSyncRequestExtensions
    {
        public static NodeFileSyncRecordModel CreateNodeFileSyncRecord(this NodeFileSyncRequest nodeFileSyncRequest)
        {
            var record = new NodeFileSyncRecord()
            {
                StoragePath = nodeFileSyncRequest.StoragePath,
                ConfigurationId = nodeFileSyncRequest.ConfigurationId,
                ConfigurationProtocol = nodeFileSyncRequest.ConfigurationProtocol,
                FileInfo = NodeFileInfoSnapshot.From(nodeFileSyncRequest.FileInfo),
            };
            return new NodeFileSyncRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                NodeInfoId = nodeFileSyncRequest.NodeInfoId,
                Name = nodeFileSyncRequest.FileInfo.FullName,
                Value = record,
                Status = NodeFileSyncStatus.Pendding,
                FullName = nodeFileSyncRequest.FileInfo.FullName,
            };
        }

        public static async ValueTask<NodeFileUploadContext> CreateNodeFileUploadContextAsync(
                                this NodeFileSyncRequest nodeFileSyncRequest,
                                IHashAlgorithmProvider hashAlgorithmProvider,
                                ICompressionProvider compressionProvider,
                                bool validateHash = false,
                                CancellationToken cancellationToken = default)
        {
            const string TempDirectory = "../NodeFileSystem/DecompressionTemp";
            var stream = nodeFileSyncRequest.Stream;
            if (nodeFileSyncRequest.FileInfo.CompressionInfo != null)
            {
                bool f = false;
                if (f)
                {
                    using var binaryReader = new BinaryReader(stream);
                    var bytes = binaryReader.ReadBytes((int)stream.Length);
                }
                if (stream.Length > 0 && stream.Length != nodeFileSyncRequest.FileInfo.CompressionInfo.Length)
                {
                    throw new InvalidDataException();
                }
                if (validateHash)
                {
                    var compressedStreamHashBytes = await hashAlgorithmProvider.HashAsync(stream, cancellationToken);
                    var compressedStreamHash = BitConverter.ToString(compressedStreamHashBytes).Replace("-", "").ToLowerInvariant();
                    if (compressedStreamHash != nodeFileSyncRequest.FileInfo.CompressionInfo.Hash)
                    {
                        throw new InvalidDataException();
                    }
                }
                if (!Directory.Exists(TempDirectory))
                {
                    Directory.CreateDirectory(TempDirectory);
                }
                var tempFilePath = Path.Combine(TempDirectory, Guid.NewGuid().ToString());
                var decompressedStream = File.Create(tempFilePath);
                if (stream.CanSeek)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }
                using (var decompressor = compressionProvider.CreateDecompressionStream(stream))
                {
                    await decompressor.CopyToAsync(decompressedStream, cancellationToken);
                    decompressedStream.Seek(0, SeekOrigin.Begin);
                }

                stream = decompressedStream;
            }
            if (stream.Length != nodeFileSyncRequest.FileInfo.Length)
            {
                throw new InvalidDataException();
            }

            if (validateHash)
            {
                var hashBytes = await hashAlgorithmProvider.HashAsync(stream, cancellationToken);
                var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                if (hash != nodeFileSyncRequest.FileInfo.Hash)
                {
                    throw new InvalidDataException();
                }
            }
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }
            var syncRecord = CreateNodeFileSyncRecord(nodeFileSyncRequest);
            return new NodeFileUploadContext(syncRecord.Value.FileInfo, syncRecord, stream);
        }

    }
}

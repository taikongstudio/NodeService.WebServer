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
        public static NodeFileSyncRecord CreateNodeFileSyncRecord(this NodeFileInfo nodeFileInfo)
        {
            return new NodeFileSyncRecord()
            {
                Id = Guid.NewGuid().ToString(),
                StoragePath = nodeFileInfo.StoragePath,
                NodeInfoId = nodeFileInfo.NodeInfoId,
                Status = NodeFileSyncStatus.Unknown,
                FileInfo = nodeFileInfo,
                ConfigurationId = nodeFileInfo.ConfigurationId,
                ConfigurationProtocol = nodeFileInfo.ConfigurationProtocol,
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
                stream.Seek(0, SeekOrigin.Begin);
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

            stream.Seek(0, SeekOrigin.Begin);
            return new NodeFileUploadContext()
            {
                FileInfo = nodeFileSyncRequest.FileInfo,
                SyncRecord = CreateNodeFileSyncRecord(nodeFileSyncRequest.FileInfo),
                Stream = stream,
            };
        }

    }
}

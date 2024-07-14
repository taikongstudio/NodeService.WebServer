using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.DataQueue
{
    public class NodeInfoQueryService
    {
        readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        readonly ApplicationRepositoryFactory<NodeProfileModel> _nodeProfileRepoFactory;

        public NodeInfoQueryService(
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<NodeProfileModel> nodeProfileRepoFactory)
        {
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _nodeProfileRepoFactory = nodeProfileRepoFactory;
        }

        public async ValueTask<NodeId> EnsureNodeInfoAsync(
            string nodeId,
            string nodeName,
            CancellationToken cancellationToken = default)
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            await using var nodeProfileRepo = await _nodeProfileRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);

            if (nodeInfo == null)
            {
                nodeInfo = NodeInfoModel.Create(nodeId, nodeName, NodeDeviceType.Computer);
                var nodeProfile =
                    await nodeProfileRepo.FirstOrDefaultAsync(new NodeProfileListSpecification(nodeName), cancellationToken);
                if (nodeProfile != null)
                {
                    var oldNodeInfo = await nodeInfoRepo.GetByIdAsync(nodeProfile.NodeInfoId, cancellationToken);
                    if (oldNodeInfo != null) await nodeInfoRepo.DeleteAsync(oldNodeInfo, cancellationToken);
                    nodeProfile.NodeInfoId = nodeId;
                    nodeInfo.ProfileId = nodeProfile.Id;
                }

                nodeInfo.Status = NodeStatus.Online;
                await nodeInfoRepo.AddAsync(nodeInfo, cancellationToken);
            }

            return new NodeId(nodeInfo.Id);
        }

        public async ValueTask<NodeInfoModel?> QueryNodeInfoByIdAsync(
            string nodeId,
            CancellationToken cancellationToken = default)
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);
            return nodeInfo;
        }


    }
}

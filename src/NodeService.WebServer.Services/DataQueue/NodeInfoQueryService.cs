using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.DataQueue
{
    public class NodeInfoQueryService
    {
        readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        readonly ApplicationRepositoryFactory<NodeProfileModel> _nodeProfileRepoFactory;
        readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
        private readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropsRepoFactory;
        readonly ObjectCache _objectCache;
        private readonly IDbContextFactory<LimsDbContext> _limsDbContextFactory;
        private readonly ILogger<NodeInfoQueryService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        readonly JsonSerializerOptions _jsonOptions;

        public NodeInfoQueryService(
            ILogger<NodeInfoQueryService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<NodeProfileModel> nodeProfileRepoFactory,
            ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
            ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertySnapshotRepositoryFactory,
            ObjectCache objectCache,
            IDbContextFactory<LimsDbContext> limsDbContextFactory)
        {
            _jsonOptions = _jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _nodeProfileRepoFactory = nodeProfileRepoFactory;
            _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
            _nodePropsRepoFactory = nodePropertySnapshotRepositoryFactory;
            _objectCache = objectCache;
            _limsDbContextFactory = limsDbContextFactory;
            _logger = logger;
            _exceptionCounter = exceptionCounter;
        }

        public async ValueTask<dl_equipment_ctrl_computer?> Query_dl_equipment_ctrl_computer_Async(string nodeInfoId, string? limsDataId, CancellationToken cancellationToken = default)
        {
            try
            {
                dl_equipment_ctrl_computer? result = null;
                await using var dbContext = await _limsDbContextFactory.CreateDbContextAsync(cancellationToken);

                if (limsDataId == null)
                {
                    var nodeInfo = await QueryNodeInfoByIdAsync(nodeInfoId, false, cancellationToken);
                    if (nodeInfo == null)
                    {
                        return null;
                    }
                    var fullName = $"{nodeInfo.Name}.{nodeInfo.Profile.ComputerDomain}";
                    result = await dbContext.dl_equipment_ctrl_computer.Where(x => x.full_name == fullName).FirstOrDefaultAsync(cancellationToken);
                    if (result == null)
                    {
                        result = await dbContext.dl_equipment_ctrl_computer.Where(x => x.name == nodeInfo.Name).FirstOrDefaultAsync(cancellationToken);
                    }
                }
                else
                {
                    result = await dbContext.dl_equipment_ctrl_computer.FindAsync([limsDataId], cancellationToken);
                }

                if (result != null)
                {
                    if (result.area_id != null)
                    {
                        var lab_area = await dbContext.dl_common_area.FindAsync([result.area_id], cancellationToken);
                        result.LabArea = lab_area;
                    }

                    if (result.laboratory != null)
                    {
                        var lab_info = await dbContext.dl_common_area.FindAsync([result.laboratory], cancellationToken);
                        result.LabInfo = lab_info;
                    }

                    if (result.base_id != null)
                    {
                        var factory_info = await dbContext.dl_common_area.FindAsync([result.base_id], cancellationToken);
                        result.Factory = factory_info;
                    }

                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex, nodeInfoId);

            }
            return null;
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

        public async ValueTask<NodePropertySnapshotModel?> QueryNodePropsAsync(
            string nodeId,
            bool useCache = false,
            CancellationToken cancellationToken = default)
        {
            var nodeInfo = await QueryNodeInfoByIdAsync(nodeId, useCache, cancellationToken);
            if (nodeInfo == null || nodeInfo.LastNodePropertySnapshotId == null)
            {
                return null;
            }
            var nodePropSnapshot = await _objectCache.GetEntityAsync<NodePropertySnapshotModel>(nodeInfo.LastNodePropertySnapshotId, cancellationToken);
            if (nodePropSnapshot == null && nodeInfo.LastNodePropertySnapshotId != null)
            {
                await using var nodePropRepo = await _nodePropsRepoFactory.CreateRepositoryAsync(cancellationToken);
                nodePropSnapshot = await nodePropRepo.GetByIdAsync(nodeInfo.LastNodePropertySnapshotId, cancellationToken);
            }
            return nodePropSnapshot;
        }

        public async ValueTask AddOrUpdateNodeInfoAsync(
            NodeInfoModel nodeInfo,
            CancellationToken cancellationToken = default)
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfoFromDb = await nodeInfoRepo.GetByIdAsync(nodeInfo.Id);
            if (nodeInfo.Properties != null)
            {
                var nodePropertyBagId = nodeInfo.GetPropertyBagId();
                await using var propertyBagRepo = await _propertyBagRepositoryFactory.CreateRepositoryAsync(cancellationToken);
                var propertyBag = await propertyBagRepo.GetByIdAsync(nodePropertyBagId);
                if (propertyBag == null)
                {
                    propertyBag = new PropertyBag
                    {
                        { "Id", nodePropertyBagId },
                        { "Value", JsonSerializer.Serialize(nodeInfo.Properties) }
                    };
                    propertyBag["CreatedDate"] = DateTime.UtcNow;
                    await propertyBagRepo.AddAsync(propertyBag);
                }
                else
                {
                    propertyBag["Value"] = JsonSerializer.Serialize(nodeInfo.Properties);
                    await propertyBagRepo.UpdateAsync(propertyBag);
                }
            }

            if (nodeInfoFromDb == null)
            {
                nodeInfoFromDb = nodeInfo;
                await nodeInfoRepo.AddAsync(nodeInfoFromDb, cancellationToken);
            }
            else
            {
                nodeInfoFromDb.Id = nodeInfo.Id;
                nodeInfoFromDb.Name = nodeInfo.Name;
                nodeInfoFromDb.DeviceType = nodeInfo.DeviceType;
                nodeInfoFromDb.Status = nodeInfo.Status;
                nodeInfoFromDb.Description = nodeInfo.Description;
                nodeInfoFromDb.Profile.Manufacturer = nodeInfo.Profile.Manufacturer;
                await nodeInfoRepo.UpdateAsync(nodeInfoFromDb, cancellationToken);
            }
        }

        public async ValueTask<NodeInfoModel?> QueryNodeInfoByIdAsync(
            string nodeId,
            bool useCache,
            CancellationToken cancellationToken = default)
        {
            NodeInfoModel? nodeInfo = null;
            if (useCache)
            {
                nodeInfo = await _objectCache.GetEntityAsync<NodeInfoModel>(nodeId);
            }
            else
            {
                await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
                nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);
                if (nodeInfo != null)
                {
                    await _objectCache.SetEntityAsync(nodeInfo, cancellationToken);
                }

            }
            return nodeInfo;
        }

        public async ValueTask UpdateNodeInfoListAsync(
            IEnumerable<NodeInfoModel> nodeInfoList,
            CancellationToken cancellationToken = default)
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            await nodeInfoRepo.UpdateRangeAsync(nodeInfoList, cancellationToken);
            foreach (var nodeInfo in nodeInfoList)
            {
                if (nodeInfo == null)
                {
                    continue;
                }
                await _objectCache.SetEntityAsync(nodeInfo, cancellationToken);
            }
        }

        public async ValueTask<List<NodeInfoModel>> QueryNodeInfoListAsync(
            IEnumerable<string> nodeIdList,
            bool useCache,
            CancellationToken cancellationToken = default)
        {
            List<NodeInfoModel> resultList = [];

            if (useCache)
            {
                foreach (var item in nodeIdList)
                {
                    var entity = await _objectCache.GetEntityAsync<NodeInfoModel>(item, cancellationToken);
                    if (entity == null)
                    {
                        continue;
                    }
                    resultList.Add(entity);
                }
                nodeIdList = nodeIdList.Except(resultList.Select(static x => x.Id)).ToArray();
            }


            if (nodeIdList.Any())
            {
                await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);

                var nodeList = await nodeInfoRepo.ListAsync(
                      new NodeInfoSpecification(
                        DataFilterCollection<string>.Includes(nodeIdList)),
                      cancellationToken);
                if (useCache)
                {
                    foreach (var item in nodeList)
                    {
                        await _objectCache.SetEntityAsync(item, cancellationToken);
                    }
                }

                resultList = resultList.Union(nodeList).ToList();
            }
            return resultList;
        }


        public async ValueTask<ListQueryResult<NodeInfoModel>> QueryNodeInfoListByQueryParameters(
            QueryNodeListParameters queryParameters,
            CancellationToken cancellationToken = default)
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();

            ListQueryResult<NodeInfoModel> queryResult = default;
            if (queryParameters.IdList == null || queryParameters.IdList.Count == 0)
                queryResult = await nodeInfoRepo.PaginationQueryAsync(new NodeInfoSpecification(
                queryParameters.AreaTag,
                        queryParameters.Status,
                        queryParameters.DeviceType,
                        queryParameters.Keywords,
                        queryParameters.SearchProfileProperties,
                        queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            else
                queryResult = await nodeInfoRepo.PaginationQueryAsync(new NodeInfoSpecification(
                        queryParameters.AreaTag,
                        queryParameters.Status,
                queryParameters.DeviceType,
                        new DataFilterCollection<string>(DataFilterTypes.Include, queryParameters.IdList)),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);


            if (queryParameters.IncludeProperties)
            {
                await using var propertyBagRepo = await _propertyBagRepositoryFactory.CreateRepositoryAsync();
                foreach (var nodeInfo in queryResult.Items)
                {
                    var propertyBag = await propertyBagRepo.GetByIdAsync(nodeInfo.GetPropertyBagId());
                    if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value) ||
                        value is not string json) continue;

                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                    nodeInfo.Properties = dict;
                }
            }

            return queryResult;
        }

        public async ValueTask SaveNodePropSnapshotAsync(
            NodeInfoModel nodeInfo,
            NodePropertySnapshotModel nodePropertySnapshot,
            bool saveToDb,
            CancellationToken cancellationToken = default)
        {
            await _objectCache.SetEntityAsync(nodePropertySnapshot, cancellationToken);
            if (saveToDb)
            {
                await using var nodePropertyRepo = await _nodePropsRepoFactory.CreateRepositoryAsync(cancellationToken);
                nodePropertySnapshot = nodePropertySnapshot with { Id = Guid.NewGuid().ToString(), EntitySource = EntitySource.Unknown };
                await nodePropertyRepo.AddAsync(nodePropertySnapshot, cancellationToken);
                var oldId = nodeInfo.LastNodePropertySnapshotId;
                nodeInfo.LastNodePropertySnapshotId = nodePropertySnapshot.Id;
                await nodePropertyRepo.DbContext.Set<NodePropertySnapshotModel>().Where(x => x.Id == oldId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }


        public async ValueTask UpdateNodeProfileAsync(string nodeId,
        UpdateNodeProfileModel value,
        CancellationToken cancellationToken = default)
        {
            var nodeInfo = await QueryNodeInfoByIdAsync(nodeId, false, cancellationToken);
            if (nodeInfo == null)
            {
                throw new Exception("invalid node info id");
            }
            else
            {
                nodeInfo.Profile.TestInfo = value.TestInfo;
                nodeInfo.Profile.Usages = value.Usages;
                nodeInfo.Profile.Remarks = value.Remarks;
                await UpdateNodeInfoListAsync([nodeInfo], cancellationToken);
            }
        }

    }
}

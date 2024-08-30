using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.NodeSessions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Immutable;
using System.Threading;

namespace NodeService.WebServer.Services.DataServices
{
    public class NodeInfoQueryService
    {
        readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        readonly ApplicationRepositoryFactory<NodeProfileModel> _nodeProfileRepoFactory;
        readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
        readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropsRepoFactory;
        readonly ApplicationRepositoryFactory<NodeExtendInfoModel> _nodeExtendInfoRepoFactory;
        readonly ObjectCache _objectCache;
        readonly ILogger<NodeInfoQueryService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly IServiceProvider _serviceProvider;
        readonly JsonSerializerOptions _jsonOptions;

        public NodeInfoQueryService(
            IServiceProvider serviceProvider,
            ILogger<NodeInfoQueryService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<NodeProfileModel> nodeProfileRepoFactory,
            ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
            ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertySnapshotRepositoryFactory,
            ApplicationRepositoryFactory<NodeExtendInfoModel> nodeExtendInfoRepoFactory,
            ObjectCache objectCache)
        {
            _jsonOptions = _jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _nodeProfileRepoFactory = nodeProfileRepoFactory;
            _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
            _nodePropsRepoFactory = nodePropertySnapshotRepositoryFactory;
            _nodeExtendInfoRepoFactory = nodeExtendInfoRepoFactory;
            _objectCache = objectCache;
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<dl_equipment_ctrl_computer?> Query_dl_equipment_ctrl_computer_Async(string nodeInfoId, string? externalBindingId, CancellationToken cancellationToken = default)
        {
            try
            {
                dl_equipment_ctrl_computer? result = null;

                var limsDbContextFactory = _serviceProvider.GetService<IDbContextFactory<LimsDbContext>>();

                await using var dbContext = await limsDbContextFactory.CreateDbContextAsync(cancellationToken);

                if (externalBindingId != null)
                {
                    result = await dbContext.dl_equipment_ctrl_computer.FindAsync([externalBindingId], cancellationToken);
                }

                if (result == null)
                {
                    var nodeInfo = await QueryNodeInfoByIdAsync(nodeInfoId, true, cancellationToken);
                    if (nodeInfo == null)
                    {
                        return null;
                    }
                    var fullName = $"{nodeInfo.Name}.{nodeInfo.Profile.ComputerDomain}";
                    result = await dbContext.dl_equipment_ctrl_computer.Where(x => x.full_name != null && x.full_name.Equals(fullName, StringComparison.OrdinalIgnoreCase)).FirstOrDefaultAsync(cancellationToken);
                    if (result == null)
                    {
                        result = await dbContext.dl_equipment_ctrl_computer.Where(x => x.name != null && x.name.Equals(nodeInfo.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefaultAsync(cancellationToken);
                    }
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
                var createNewNode = false;
                var nodeProfile = await nodeProfileRepo.FirstOrDefaultAsync(new NodeProfileListSpecification(nodeName), cancellationToken);
                if (nodeProfile != null)
                {
                    nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeProfile.NodeInfoId, cancellationToken);
                }
                if (nodeInfo == null)
                {
                    nodeInfo = NodeInfoModel.Create(nodeId, nodeName, NodeDeviceType.Computer);
                    createNewNode = true;
                }
                nodeInfo.Status = NodeStatus.Online;
                if (createNewNode)
                {
                    await nodeInfoRepo.AddAsync(nodeInfo, cancellationToken);
                }
                else
                {
                    await nodeInfoRepo.UpdateAsync(nodeInfo, cancellationToken);
                }
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
            if (nodeInfo == null)
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

        public async ValueTask<ImmutableArray<NodeInfoModel>> QueryNodeInfoListByExtendInfoAsync(
            NodeExtendInfo nodeExtendInfo,
            CancellationToken cancellationToken = default)
        {
            var builder = ImmutableArray.CreateBuilder<NodeInfoModel>();
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            await using var nodeExtendInfoRepo = await _nodeExtendInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfoList = await nodeInfoRepo.ListAsync(cancellationToken);
            foreach (var nodeInfoArray in nodeInfoList.Chunk(10))
            {
                var idList = DataFilterCollection<string>.Includes(nodeInfoArray.Select(static x => x.Id));
                var nodeExtendInfoList = await nodeExtendInfoRepo.ListAsync(new NodeExtendInfoSpecification(idList), cancellationToken);
                foreach (var nodeExtendInfoFromDb in nodeExtendInfoList)
                {
                    var isCpuMatched = false;
                    foreach (var cpuInfoLeft in nodeExtendInfoFromDb.Value.CpuInfoList)
                    {
                        foreach (var cpuInfoRight in nodeExtendInfo.CpuInfoList)
                        {
                            if (cpuInfoLeft == cpuInfoRight)
                            {
                                isCpuMatched = true;
                                break;
                            }
                        }
                    }
                    bool isBiosMatched = false;
                    foreach (var biosInfoLeft in nodeExtendInfoFromDb.Value.BIOSInfoList)
                    {
                        foreach (var biosInfoRight in nodeExtendInfo.BIOSInfoList)
                        {
                            if (biosInfoLeft == biosInfoRight)
                            {
                                isBiosMatched = true;
                                break;
                            }
                        }
                    }

                    bool isPhysicalMediaInfoMatched = false;
                    foreach (var physicalMediaInfoLeft in nodeExtendInfoFromDb.Value.PhysicalMediaInfoList)
                    {
                        foreach (var physicalMediaInfoRight in nodeExtendInfo.PhysicalMediaInfoList)
                        {
                            if (physicalMediaInfoLeft == physicalMediaInfoRight)
                            {
                                isPhysicalMediaInfoMatched = true;
                                break;
                            }
                        }
                    }
                    if (isCpuMatched || isPhysicalMediaInfoMatched || isBiosMatched)
                    {
                        builder.Add(nodeInfoArray.FirstOrDefault(x => x.Id == nodeExtendInfoFromDb.Id));
                    }
                }

            }

            return builder.ToImmutable();
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

        public async ValueTask<NodeExtendInfoModel> QueryExtendInfoAsync(NodeInfoModel nodeInfo, CancellationToken cancellationToken = default)
        {
            await using var nodeExtendInfoRepo = await _nodeExtendInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeExtendInfo = await nodeExtendInfoRepo.GetByIdAsync(nodeInfo.Id, cancellationToken);
            if (nodeExtendInfo == null)
            {
                nodeExtendInfo = new NodeExtendInfoModel()
                {
                    Id = nodeInfo.Id,
                    Name = nodeInfo.Name,
                };
                await nodeExtendInfoRepo.AddAsync(nodeExtendInfo, cancellationToken);
            }
            return nodeExtendInfo;
        }

        public async ValueTask UpdateExtendInfoAsync(NodeExtendInfoModel nodeExtendInfo, CancellationToken cancellationToken = default)
        {
            await using var nodeExtendInfoRepo = await _nodeExtendInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            await nodeExtendInfoRepo.UpdateAsync(nodeExtendInfo, cancellationToken);
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

        public async ValueTask<Stream> ExportNodeListAsync(QueryNodeListParameters parameters, CancellationToken cancellationToken = default)
        {
            var queryResult = await QueryNodeInfoListByQueryParameters(parameters, cancellationToken);

            if (TryWriteToExcel(queryResult.Items.ToImmutableArray(), out Stream? stream) && stream != null)
            {
                return stream;
            }
            return Stream.Null;
        }

        public bool TryWriteToExcel(ImmutableArray<NodeInfoModel> nodeList, out Stream? stream)
        {
            stream = null;
            try
            {
                //创建工作薄  
                IWorkbook wb = new XSSFWorkbook();

                //创建一个表单
                ISheet sheet = wb.CreateSheet("上位机列表");
                //设置列宽
                int[] columnWidth = { 20, 20, 20, 20, 20, 20, 20, 50, 50 };
                for (int i = 0; i < columnWidth.Length; i++)
                {
                    //设置列宽度，256*字符数，因为单位是1/256个字符
                    sheet.SetColumnWidth(i, 256 * columnWidth[i]);
                }

                //测试数据

                IRow headerRow = sheet.CreateRow(0);
                var headers = new string[] { "上位机Id", "上位机名称", "上位机状态", "最后在线时间", "业务类型", "实验室名称", "测试区域", "上位机负责人", "IP地址", "Lims关联性", "用途" };
                {
                    for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    {
                        var cell = headerRow.CreateCell(columnIndex);//创建第j列
                        SetCellValue(cell, headers[columnIndex]);
                    }
                }


                var rowIndex = 1;
                for (int dataIndex = 0; dataIndex < nodeList.Length; dataIndex++)
                {
                    var result = nodeList[dataIndex];

                    var dataRow = sheet.CreateRow(rowIndex);
                    rowIndex++;
                    for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    {
                        var cell = dataRow.CreateCell(columnIndex);
                        switch (columnIndex)
                        {
                            case 0:
                                SetCellValue(cell, result.Id);
                                break;
                            case 1:
                                SetCellValue(cell, result.Name);
                                break;
                            case 2:
                                SetCellValue(cell, result.Status.GetDisplayName());
                                break;
                            case 3:
                                SetCellValue(cell, result.Profile.ServerUpdateTimeUtc);
                                break;
                            case 4:
                                SetCellValue(cell, result.Profile.TestInfo ?? string.Empty);
                                break;
                            case 5:
                                SetCellValue(cell, result.Profile.LabName ?? string.Empty);
                                break;
                            case 6:
                                SetCellValue(cell, result.Profile.LabArea ?? string.Empty);
                                break;
                            case 7:
                                SetCellValue(cell, result.Profile.Manager ?? string.Empty);
                                break;
                            case 8:
                                SetCellValue(cell, result.Profile.IpAddress ?? string.Empty);
                                break;
                            case 9:
                                SetCellValue(cell, result.Profile.FoundInLims ? "是" : "否");
                                break;
                            case 10:
                                SetCellValue(cell, result.Profile.Usages ?? string.Empty);
                                break;
                            default:
                                break;
                        }
                    }

                }

                stream = new MemoryStream();
                wb.Write(stream, true);//向打开的这个Excel文件中写入表单并保存。  
                stream.Position = 0;

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            return false;
        }

        public static void SetCellValue<T>(ICell cell, T obj)
        {
            if (obj is int intValue)
            {
                cell.SetCellValue(intValue);
            }
            else if (obj is double doubleValue)
            {
                cell.SetCellValue(doubleValue);
            }
            else if (obj is IRichTextString richTextString)
            {
                cell.SetCellValue(richTextString);
            }
            else if (obj is string stringValue)
            {
                cell.SetCellValue(stringValue);
            }
            else if (obj is DateTime dateTimeValue)
            {
                cell.SetCellValue(dateTimeValue.ToString("yyyy/MM/dd HH:mm:ss"));
            }
            else if (obj is bool boolValue)
            {
                cell.SetCellValue(boolValue);
            }
            else
            {
                cell.SetCellValue(obj.ToString());
            }
        }

    }
}

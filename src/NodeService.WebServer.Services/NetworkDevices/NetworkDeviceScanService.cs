using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NetworkDevices;

public class NetworkDeviceScanService : BackgroundService
{
    private readonly ILogger<NetworkDeviceScanService> _logger;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepoFactory;
    private readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly JsonSerializerOptions _jsonOptions;
    private NodeSettings _nodeSettings;


    public NetworkDeviceScanService(
        ILogger<NetworkDeviceScanService> logger,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepoFactory,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatBatchQueue,
        ExceptionCounter exceptionCounter
    )
    {
        _logger = logger;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _propertyBagRepoFactory = propertyBagRepoFactory;
        _heartBeatBatchQueue = heartBeatBatchQueue;
        _exceptionCounter = exceptionCounter;
        _jsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ScanNetworkDevicesAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private async ValueTask ScanNetworkDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RefreshNodeSettingsAsync(cancellationToken);
            using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
            List<NodeInfoModel> networkDeviceList = await QueryNetworkDeviceListAsync(nodeInfoRepo, cancellationToken);

            await Parallel.ForEachAsync(networkDeviceList, new ParallelOptions()
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4
            }, ProcessNodeInfoAsync);

            await nodeInfoRepo.UpdateRangeAsync(networkDeviceList, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask ProcessNodeInfoAsync(
        NodeInfoModel nodeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var propertyBagRepo = _propertyBagRepoFactory.CreateRepository();
            switch (nodeInfo.DeviceType)
            {
                case NodeDeviceType.NetworkDevice:
                {
                    var propertyBag =
                        await propertyBagRepo.GetByIdAsync(nodeInfo.GetPropertyBagId(), cancellationToken);
                    if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value) ||
                        value is not string json) return;

                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                    if (dict == null || !dict.TryGetValue("Settings", out var settings) ||
                        settings is not JsonElement element) return;
                    var ipAddressPortSettings = element.Deserialize<IPAddressPortSettings>(_jsonOptions);
                    if (ipAddressPortSettings == null || ipAddressPortSettings.IpAddress == null) return;

                    if (await TestNodeStatusAsync(
                            ipAddressPortSettings.IpAddress,
                            TimeSpan.FromSeconds(ipAddressPortSettings.PingTimeOutSeconds),
                            cancellationToken))
                    {
                        nodeInfo.Status = NodeStatus.Online;
                        nodeInfo.Profile.UpdateTime = DateTime.Now;
                        nodeInfo.Profile.ServerUpdateTimeUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        nodeInfo.Status = NodeStatus.Offline;
                    }

                    nodeInfo.Profile.IpAddress = ipAddressPortSettings.IpAddress;
                    _nodeSettings.MatchAreaTag(nodeInfo);
                }
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask<bool> TestNodeStatusAsync(string ipAddress, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, timeout, cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return false;
    }

    private async ValueTask<List<NodeInfoModel>> QueryNetworkDeviceListAsync(
        IRepository<NodeInfoModel> nodeInfoRepo,
        CancellationToken cancellationToken = default)
    {
        var nodeInfoList = await nodeInfoRepo.ListAsync(
            new NodeInfoSpecification(
                null,
                NodeStatus.All,
                NodeDeviceType.NetworkDevice,
                [new SortDescription(nameof(NodeInfoModel.Status), "descend")]),
            cancellationToken);
        return nodeInfoList;
    }

    private async ValueTask RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var repo = _propertyBagRepoFactory.CreateRepository();
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
        if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();

        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);
    }
}
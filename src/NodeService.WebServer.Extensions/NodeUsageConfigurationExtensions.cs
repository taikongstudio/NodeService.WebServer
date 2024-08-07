using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class NodeUsageConfigurationExtensions
    {

        public static bool DetectProcess(this NodeUsageConfigurationModel nodeUsageConfiguration, ProcessInfo processInfo)
        {
            var isDetected = false;
            foreach (var detection in nodeUsageConfiguration.ServiceProcessDetections)
            {
                switch (detection.DetectionType)
                {
                    case NodeUsageServiceProcessDetectionType.FileName:
                        if (processInfo.FileName.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            isDetected = true;
                        }
                        break;
                    case NodeUsageServiceProcessDetectionType.ProcessName:
                        if (processInfo.FileName.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            isDetected = true;
                        }
                        break;
                    case NodeUsageServiceProcessDetectionType.ServiceName:
                        break;
                    default:
                        break;
                }
            }

            return isDetected;
        }

        public static bool DetectServiceProcess(this NodeUsageConfigurationModel nodeUsageConfiguration, ServiceProcessInfo serviceProcessInfo)
        {
            var isDetected = false;
            foreach (var detection in nodeUsageConfiguration.ServiceProcessDetections)
            {
                switch (detection.DetectionType)
                {
                    case NodeUsageServiceProcessDetectionType.FileName:
                        if (serviceProcessInfo.PathName != null && serviceProcessInfo.PathName.Contains(
                            detection.Value,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            isDetected = true;
                        }
                        break;
                    case NodeUsageServiceProcessDetectionType.ProcessName:

                        break;
                    case NodeUsageServiceProcessDetectionType.ServiceName:
                        if (serviceProcessInfo.Name != null && serviceProcessInfo.Name.Contains(
                            detection.Value,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            isDetected = true;
                        }
                        break;
                    default:
                        break;
                }
            }

            return isDetected;
        }


        public static void AddToConfigurationNodeList(this NodeUsageConfigurationModel nodeUsageConfiguration,NodeInfoModel nodeInfo)
        {
            if (nodeInfo == null)
            {
                return;
            }
            if (nodeUsageConfiguration == null)
            {
                return;
            }
            if (nodeUsageConfiguration.Nodes == null)
            {
                return;
            }
            lock (nodeUsageConfiguration.Nodes)
            {
                NodeUsageInfo? nodeUsageInfo = null;
                foreach (var item in nodeUsageConfiguration.Nodes)
                {
                    if (item.NodeInfoId == nodeInfo.Id)
                    {
                        nodeUsageInfo = item;
                        break;
                    }
                }
                if (nodeUsageInfo == null)
                {
                    nodeUsageInfo = new NodeUsageInfo()
                    {
                        Name = nodeInfo.Name,
                        NodeInfoId = nodeInfo.Id
                    };
                    nodeUsageConfiguration.Nodes.Add(nodeUsageInfo);
                }
            }
        }


    }
}

using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class dl_equipment_ctrl_computerModelExtensions
    {
        public static bool IsScrapped(this dl_equipment_ctrl_computer computerInfo)
        {
            if (computerInfo == null)
            {
                return false;
            }
            if (computerInfo.remark != null)
            {
                if ((computerInfo.remark.Contains("废弃") || computerInfo.remark.Contains("报废")))
                {
                    return true;
                }
            }

            return false;
        }


    }
}

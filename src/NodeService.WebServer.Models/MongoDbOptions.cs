﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class MongoDbOptions
    {
        public string ConnectionString { get; set; }

        public string TaskLogDatabaseName { get; set; }
    }
}
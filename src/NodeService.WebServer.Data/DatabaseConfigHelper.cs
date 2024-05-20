using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data
{
    public static class DatabaseConfigHelper
    {
        public static bool TryBuildConnectionString(
            this DatabaseConfigModel databaseConfig,
            out DatabaseProviderType databaseProviderType,
            out string? connectionString)
        {
            connectionString = null;
            databaseProviderType = DatabaseProviderType.Unknown;
            if (databaseConfig == null)
            {
                return false;
            }
            switch (databaseConfig.Provider)
            {
                case "MySql":
                    connectionString = databaseConfig.BuildMySqlConnectionString();
                    databaseProviderType = DatabaseProviderType.MySql;
                    break;
                case "SqlServer":
                    connectionString = databaseConfig.BuildSqlServerConnectionString();
                    databaseProviderType = DatabaseProviderType.SqlServer;
                    break;

            }
            return connectionString != null;
        }

        private static string BuildSqlServerConnectionString(this DatabaseConfigModel databaseConfig)
        {
            var builder = new SqlConnectionStringBuilder();
            builder["Server"] = databaseConfig.Host;
            if (databaseConfig.Port != 0)
            {
                builder["Server"] += $",{databaseConfig.Port}";
            }

            builder.InitialCatalog = databaseConfig.Database;
            builder.UserID = databaseConfig.UserId;
            builder.Password = databaseConfig.Password;
            //builder.IntegratedSecurity = true;
            builder["Trusted_Connection"] = true;
            //return builder.ConnectionString;
            return "Server=(localdb)\\mssqllocaldb;Database=NodeServiceUserDb_current;Trusted_Connection=True";
        }

        private static string BuildMySqlConnectionString(this DatabaseConfigModel databaseConfig)
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder();
            builder.UserID = databaseConfig.UserId;
            builder.Password = databaseConfig.Password;
            builder.Database = databaseConfig.Database;
            builder.Server = databaseConfig.Host;
            builder.Port = (uint)databaseConfig.Port;
            builder.Password = databaseConfig.Password;
            return builder.ConnectionString;
        }

    }
}

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using MySqlConnector;
using NodeService.Infrastructure.Data;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data;

public class DataQualityStatisticsDbContext : DbContext
{
    private readonly DatabaseProviderType _databaseProviderType;
    private string _connectionString;

    public DataQualityStatisticsDbContext(DatabaseProviderType databaseProviderType, string connectionString)
    {
        _databaseProviderType = databaseProviderType;
        _connectionString = connectionString;
    }

    public DataQualityStatisticsDbContext(DbContextOptions<DataQualityStatisticsDbContext> options) : base(options)
    {
    }


    protected DataQualityStatisticsDbContext(DbContextOptions contextOptions)
        : base(contextOptions)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DataQualityNodeStatisticsItem>(builder => { builder.HasNoKey(); });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        switch (_databaseProviderType)
        {
            case DatabaseProviderType.Unknown:
                break;
            case DatabaseProviderType.MySql:
                optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString), options => { });
                break;
            case DatabaseProviderType.SqlServer:
                optionsBuilder.UseSqlServer(_connectionString, options => { });
                break;
            case DatabaseProviderType.Sqlite:
                optionsBuilder.UseSqlite(_connectionString, options => { });
                break;
            default:
                break;
        }

        base.OnConfiguring(optionsBuilder);
    }
}
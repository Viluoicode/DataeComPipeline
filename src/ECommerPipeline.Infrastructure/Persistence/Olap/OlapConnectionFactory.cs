using System.Data;
using ECommerPipeline.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ECommerPipeline.Infrastructure.Persistence.Olap;

public class OlapOptions
{
    public const string SectionName = "ConnectionStrings";
    public string OlapConnection { get; set; } = null!;
}

public class OlapConnectionFactory : IOlapConnectionFactory
{
    private readonly string _connectionString;

    public OlapConnectionFactory(IOptions<OlapOptions> options)
    {
        _connectionString = options.Value.OlapConnection;
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}

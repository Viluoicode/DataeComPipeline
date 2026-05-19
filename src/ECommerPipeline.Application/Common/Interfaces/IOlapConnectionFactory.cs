using System.Data;

namespace ECommerPipeline.Application.Common.Interfaces;

public interface IOlapConnectionFactory
{
    IDbConnection CreateConnection();
}

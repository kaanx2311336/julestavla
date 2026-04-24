using System.Data;
using System.Threading.Tasks;

namespace TavlaJules.Data.Repositories;

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateConnectionAsync();
}

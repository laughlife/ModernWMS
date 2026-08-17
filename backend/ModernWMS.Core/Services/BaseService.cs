
using ModernWMS.Core.Models;

namespace ModernWMS.Core.Services
{
    public class BaseService<TEntity> : IBaseService<TEntity> where TEntity : BaseModel
    {

    }
}

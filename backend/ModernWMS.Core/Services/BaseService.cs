
using ModernWMS.Core.Models;

namespace ModernWMS.Core.Services
{
    /// <summary>
    /// 表示 BaseService 类型。
    /// </summary>
    public class BaseService<TEntity> : IBaseService<TEntity> where TEntity : BaseModel
    {

    }
}

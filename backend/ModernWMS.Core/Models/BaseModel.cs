using System.ComponentModel.DataAnnotations;

namespace ModernWMS.Core.Models
{
    /// <summary>
    /// 表示 BaseModel 类型。
    /// </summary>
    [Serializable]
    public abstract class BaseModel
    {
        /// <summary>
        /// id
        /// </summary>
        [Key]
        public int id { get; set; } = 0;

    }
}

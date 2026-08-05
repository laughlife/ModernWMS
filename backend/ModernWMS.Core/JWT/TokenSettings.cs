
namespace ModernWMS.Core.JWT
{
    /// <summary>
    /// token settings
    /// </summary>
    public class TokenSettings
    {
        /// <summary>
        /// Audience
        /// </summary>
        public string Audience { get; set; } = string.Empty;
        /// <summary>
        /// Issuer
        /// </summary>
        public string Issuer { get; set; } = string.Empty;
        /// <summary>
        /// SigningKey
        /// </summary>
        public string SigningKey { get; set; } = string.Empty;
        /// <summary>
        ///  Expire
        /// </summary>
        public int ExpireMinute { get; set; }
    }
}

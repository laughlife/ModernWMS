namespace ModernWMS.Core.Models
{
    /// <summary>
    /// LoginOutputViewModel
    /// </summary>
    public class LoginOutputViewModel
    {
        /// <summary>
        /// user's num
        /// </summary>
        public string user_num { get; set; } = string.Empty;

        /// <summary>
        /// user's name
        /// </summary>
        public string user_name { get; set; } = string.Empty;

        /// <summary>
        ///  user's id
        /// </summary>
        public int user_id { get; set; }

        /// <summary>
        ///  user's role
        /// </summary>
        public string user_role { get; set; } = string.Empty;

        /// <summary>
        ///  id of user's role
        /// </summary>
        public int userrole_id { get; set; }


        /// <summary>
        /// token expire time
        /// </summary>
        public int expire { get; set; }

        /// <summary>
        /// token
        /// </summary>
        public string access_token { get; set; } = string.Empty;

        /// <summary>
        /// refresh token
        /// </summary>
        public string refresh_token { get; set; } = string.Empty;

    }
}

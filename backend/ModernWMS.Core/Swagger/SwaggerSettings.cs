
using System.Collections.Generic;

namespace ModernWMS.Core.Swagger
{
    /// <summary>
    /// Swagger Settings
    /// </summary>
    public class SwaggerSettings
    {
        /// <summary>
        /// SwaggerDoc Name
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// OpenApiInfo Title
        /// </summary>
        public string ApiTitle { get; set; } = string.Empty;
        /// <summary>
        /// OpenApiInfo Version
        /// </summary>
        public string ApiVersion { get; set; } = string.Empty;
        /// <summary>
        /// OpenApiInfo Description
        /// </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// Whether to turn on authorization verification
        /// </summary>
        public bool SecurityDefinition { get; set; }

        /// <summary>
        /// Included XML documents
        /// </summary>
        public List<string> XmlFiles { get; set; } = [];
         
    }
}

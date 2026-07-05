namespace DmxControlUtilities.Web.Options
{
    /// <summary>
    /// Configuration options for Umbra server connections.
    /// </summary>
    public class UmbraConnectionOptions
    {
        public const string SectionName = "UmbraConnection";

        /// <summary>
        /// Gets or sets the client application name.
        /// </summary>
        public string ClientName { get; set; } = "DMXControl Utilities";

        /// <summary>
        /// Gets or sets the client program description.
        /// </summary>
        public string ClientProgramDescription { get; set; } = "DMXControl Utilities Timecode Syncer";

        /// <summary>
        /// Gets or sets the client hostname (defaults to localhost).
        /// </summary>
        public string ClientHostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// Gets or sets the default username for authentication.
        /// </summary>
        public string DefaultUsername { get; set; } = "Tool";

        /// <summary>
        /// Gets or sets the default password hash for authentication.
        /// </summary>
        public string DefaultPasswordHash { get; set; } = "123";

        /// <summary>
        /// Gets or sets the request timeout in seconds.
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = 30;
    }
}

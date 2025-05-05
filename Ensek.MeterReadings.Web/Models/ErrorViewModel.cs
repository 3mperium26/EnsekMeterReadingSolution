namespace Ensek.MeterReadings.Web.Models
{
    /// <summary>
    /// View model for the standard Error page.
    /// </summary>
    public class ErrorViewModel
    {
        /// <summary>
        /// Gets or sets the request identifier associated with the error.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Gets a value indicating whether the RequestId should be shown.
        /// Returns true if RequestId is not null or empty.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        // Optional: Add properties for exception details (only populate in Development)
        // public string? ExceptionMessage { get; set; }
        // public string? StackTrace { get; set; }
        // public bool ShowExceptionDetails => !string.IsNullOrEmpty(ExceptionMessage);
    }
}
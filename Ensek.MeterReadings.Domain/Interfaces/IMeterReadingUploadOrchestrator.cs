using Ensek.MeterReadings.Domain.Dtos;

namespace Ensek.MeterReadings.Domain.Interfaces
{
    /// <summary>
    /// Orchestrates the meter reading upload process.
    /// SRP: Coordinates the flow (parse -> validate -> save).
    /// DIP: Depends on abstractions (ICsvParsingService, IMeterReadingValidationService, IMeterReadingRepository).
    /// </summary>
    public interface IMeterReadingUploadOrchestrator
    {
        /// <summary>
        /// Processes a stream containing CSV meter reading data.
        /// </summary>
        /// <param name="csvStream">The stream to process.</param>
        /// <param name="originalFileName">Optional: The original name of the uploaded file for context in results/logging.</param>
        /// <returns>A MeterReadingUploadResult summarizing the outcome.</returns>
        Task<MeterReadingUploadResult> ProcessUploadAsync(Stream csvStream, string? originalFileName = null);
    }
}
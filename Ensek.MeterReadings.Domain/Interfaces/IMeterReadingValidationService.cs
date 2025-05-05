using Ensek.MeterReadings.Domain.Dtos;

namespace Ensek.MeterReadings.Domain.Interfaces
{
    /// <summary>
    /// Validates meter reading records using a set of rules.
    /// SRP: Responsible only for coordinating validation logic.
    /// DIP: Depends on IValidationRule abstractions.
    /// </summary>
    public interface IMeterReadingValidationService
    {
        /// <summary>
        /// Validates a single meter reading record against all registered rules.
        /// </summary>
        /// <param name="record">The record to validate.</param>
        /// <param name="context">The validation context.</param>
        /// <returns>A list of error messages if validation fails, or an empty list if valid.</returns>
        Task<List<string>> ValidateReadingAsync(MeterReadingCsvRecord record, ValidationContext context);

        /// <summary>
        /// Prepares the validation context (e.g., loads necessary data).
        /// </summary>
        /// <param name="recordsToValidate">An initial list of records (optional, can help optimize data loading).</param>
        /// <returns>The validation context.</returns>
        Task<ValidationContext> BuildValidationContextAsync(IEnumerable<MeterReadingCsvRecord>? recordsToValidate = null);
    }
}
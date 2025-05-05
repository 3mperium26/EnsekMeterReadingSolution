using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Models; // May need models if context includes them

namespace Ensek.MeterReadings.Domain.Interfaces
{
    /// <summary>
    /// Defines a single validation rule for a meter reading record.
    /// OCP: New rules can be added by implementing this interface.
    /// </summary>
    public interface IValidationRule
    {
        /// <summary>
        /// Validates a meter reading record.
        /// </summary>
        /// <param name="record">The parsed CSV record.</param>
        /// <param name="context">Validation context containing necessary data (e.g., existing readings, account IDs).</param>
        /// <returns>A validation result indicating success or failure with a message.</returns>
        Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context);

        /// <summary>
        /// Indicates if this rule requires database access (used for optimization).
        /// </summary>
        bool RequiresDbAccess { get; }
    }

    /// <summary>
    /// Context passed to validation rules.
    /// </summary>
    public class ValidationContext
    {
        public IReadOnlySet<int> ValidAccountIds { get; }
        public ILookup<int, MeterReads> ExistingReadingsByAccount { get; } // Efficient lookup for existing reads
        public ISet<(int AccountId, DateTime DateTime, int Value)>? ProcessedInBatch { get; set; } // Track duplicates within the same batch

        // Constructor initializes the context, potentially loading data needed by rules
        public ValidationContext(IReadOnlySet<int> validAccountIds, ILookup<int, Models.MeterReads> existingReadings)
        {
            ValidAccountIds = validAccountIds ?? throw new ArgumentNullException(nameof(validAccountIds));
            ExistingReadingsByAccount = existingReadings ?? throw new ArgumentNullException(nameof(existingReadings));
            ProcessedInBatch = new HashSet<(int AccountId, DateTime DateTime, int Value)>();
        }
    }

    /// <summary>
    /// Represents the outcome of a validation check.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; } // Message if validation failed

        private ValidationResult(bool isValid, string? errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Success() => new(true);
        public static ValidationResult Failure(string message) => new(false, message);
    }
}
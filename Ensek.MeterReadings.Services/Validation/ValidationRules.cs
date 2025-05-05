using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Interfaces;
using System.Text.RegularExpressions; // Required for Regex

namespace Ensek.MeterReadings.Services.Validation
{
    /// <summary>
    /// Validation rule to check if the AccountId exists in the system.
    /// Does not require database access as it uses pre-loaded context data.
    /// </summary>
    public class AccountExistsRule : IValidationRule
    {
        public bool RequiresDbAccess => false; // Uses context.ValidAccountIds

        public Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            // Check if the AccountId from the record exists in the set of valid IDs provided in the context.
            if (!context.ValidAccountIds.Contains(record.AccountId))
            {
                // If not found, return a failure result with an informative message.
                return Task.FromResult(ValidationResult.Failure($"Invalid AccountId: {record.AccountId}. Account does not exist."));
            }
            // If found, return a success result.
            return Task.FromResult(ValidationResult.Success());
        }
    }

    /// <summary>
    /// Validation rule to check if the MeterReadValue is in the correct format (NNNNN - 1 to 5 digits).
    /// Does not require database access.
    /// </summary>
    public class MeterValueFormatRule : IValidationRule
    {
        // Compiled Regex for efficiency: Matches strings containing only 1 to 5 digits from start to end.
        private static readonly Regex MeterReadValueRegex = new Regex(@"^\d{1,5}$", RegexOptions.Compiled);
        public bool RequiresDbAccess => false;

        public Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            // Check if the value is null, empty, or whitespace.
            if (string.IsNullOrWhiteSpace(record.MeterReadValue))
            {
                return Task.FromResult(ValidationResult.Failure("MeterReadValue is missing or empty."));
            }
            // Check if the value matches the NNNNN format using the regex.
            if (!MeterReadValueRegex.IsMatch(record.MeterReadValue))
            {
                return Task.FromResult(ValidationResult.Failure($"Invalid MeterReadValue format: '{record.MeterReadValue}'. Must be NNNNN (1-5 digits)."));
            }
            // As a final check, ensure it can be parsed as an integer (though regex should cover valid cases).
            if (!int.TryParse(record.MeterReadValue, out _))
            {
                // This indicates an unexpected issue if the regex passed but parsing failed.
                return Task.FromResult(ValidationResult.Failure($"Internal Error: MeterReadValue '{record.MeterReadValue}' passed format check but failed integer parsing."));
            }
            // If all checks pass, return success.
            return Task.FromResult(ValidationResult.Success());
        }
    }

    /// <summary>
    /// Validation rule to check for duplicate entries within the *same* uploaded file/batch.
    /// Uses a HashSet stored in the ValidationContext to track processed entries.
    /// Does not require database access itself.
    /// </summary>
    public class DuplicateInBatchRule : IValidationRule
    {
        public bool RequiresDbAccess => false; // Uses context.ProcessedInBatch

        public Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            // This rule assumes the MeterValueFormatRule has already run and succeeded.
            // Attempt to parse the value again for safety.
            if (!int.TryParse(record.MeterReadValue, out int meterReadValueInt))
            {
                // If parsing fails here, it implies the format rule might have missed something or wasn't run first.
                return Task.FromResult(ValidationResult.Failure("Invalid value format prevented batch duplicate check. Ensure format validation runs first."));
            }

            // Create a unique identifier tuple for this specific reading entry.
            var entryTuple = (record.AccountId, record.MeterReadingDateTime, meterReadValueInt);

            // Check if the context's tracking set has been initialized (should be done by the orchestrator/validation service).
            if (context.ProcessedInBatch == null)
            {
                // This indicates a setup error in the calling code.
                return Task.FromResult(ValidationResult.Failure("Internal Error: ProcessedInBatch context was not initialized. Cannot check for duplicates within the batch."));
            }

            // Attempt to add the entry tuple to the HashSet.
            // HashSet.Add returns false if the item already exists in the set.
            if (!context.ProcessedInBatch.Add(entryTuple))
            {
                // If Add returns false, it's a duplicate within this batch.
                return Task.FromResult(ValidationResult.Failure("Duplicate entry found within the uploaded file/batch."));
            }

            // If Add returns true, it's the first time seeing this entry in the batch.
            return Task.FromResult(ValidationResult.Success());
        }
    }

    /// <summary>
    /// Validation rule to check if an identical meter reading already exists in the database.
    /// Requires database access via the injected repository.
    /// </summary>
    public class DuplicateInDbRule : IValidationRule
    {
        private readonly IMeterReadingRepository _repository; // Injected repository for DB access
        public bool RequiresDbAccess => true; // This rule needs the database

        /// <summary>
        /// Initializes a new instance of the DuplicateInDbRule.
        /// </summary>
        /// <param name="repository">The meter reading repository instance.</param>
        public DuplicateInDbRule(IMeterReadingRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            // Assumes MeterValueFormatRule has run successfully. Parse for safety.
            if (!int.TryParse(record.MeterReadValue, out int meterReadValueInt))
            {
                return ValidationResult.Failure("Invalid value format prevented database duplicate check.");
            }

            // Call the repository to check if this specific reading exists.
            bool exists = await _repository.DoesReadingExistAsync(record.AccountId, record.MeterReadingDateTime, meterReadValueInt);

            // If the repository confirms it exists, return a failure result.
            if (exists)
            {
                return ValidationResult.Failure("Duplicate entry already exists in the database.");
            }
            // Otherwise, return success.
            return ValidationResult.Success();
        }
    }

    /// <summary>
    /// Validation rule (Nice to Have) to ensure a new reading is not older than the latest existing reading for the same account.
    /// Does not require direct database access as it uses pre-loaded data from the ValidationContext.
    /// </summary>
    public class OlderReadingRule : IValidationRule
    {
        public bool RequiresDbAccess => false; // Uses context.ExistingReadingsByAccount

        public Task<ValidationResult> ValidateAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            // Use the ILookup in the context for efficient access to existing readings for this account.
            // Find the reading with the latest MeterReadingDateTime.
            var latestExistingReading = context.ExistingReadingsByAccount[record.AccountId]
                                              .OrderByDescending(mr => mr.MeterReadDateTime)
                                              .FirstOrDefault(); // Returns null if no existing readings for this account

            // If an existing reading is found and the new reading's date is older, it's invalid.
            if (latestExistingReading != null && record.MeterReadingDateTime < latestExistingReading.MeterReadDateTime)
            {
                // Return a failure result with details about the dates.
                return Task.FromResult(ValidationResult.Failure($"Reading date ({record.MeterReadingDateTime:dd/MM/yyyy HH:mm}) is older than latest existing reading date ({latestExistingReading.MeterReadDateTime:dd/MM/yyyy HH:mm}) for this account."));
            }
            // If no existing reading or the new reading is not older, return success.
            return Task.FromResult(ValidationResult.Success());
        }
    }
}
using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic; // Required for List
using System.Linq; // Required for LINQ methods like Where, Any

namespace Ensek.MeterReadings.Services
{
    /// <summary>
    /// Service responsible for validating MeterReadingCsvRecord objects using a collection of IValidationRule implementations.
    /// </summary>
    public class MeterReadingValidationService : IMeterReadingValidationService
    {
        // Collection to hold all registered validation rules, injected via DI.
        private readonly IEnumerable<IValidationRule> _validationRules;
        // Repository needed to fetch data required for building the validation context.
        private readonly IMeterReadingRepository _repository;
        private readonly ILogger<MeterReadingValidationService> _logger;

        /// <summary>
        /// Initializes a new instance of the MeterReadingValidationService.
        /// </summary>
        /// <param name="validationRules">The collection of validation rules.</param>
        /// <param name="repository">The meter reading repository.</param>
        /// <param name="logger">The logger instance.</param>
        public MeterReadingValidationService(IEnumerable<IValidationRule> validationRules, IMeterReadingRepository repository, ILogger<MeterReadingValidationService> logger)
        {
            _validationRules = validationRules ?? throw new ArgumentNullException(nameof(validationRules));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Builds the ValidationContext required by the validation rules.
        /// This involves fetching necessary data (like existing account IDs and readings) from the repository.
        /// </summary>
        /// <param name="recordsToValidate">Optional: A collection of records being validated. If provided, can optimize data fetching.</param>
        /// <returns>A fully populated ValidationContext.</returns>
        public async Task<ValidationContext> BuildValidationContextAsync(IEnumerable<MeterReadingCsvRecord>? recordsToValidate = null)
        {
            _logger.LogDebug("Building validation context...");
            try
            {
                // Fetch all current account IDs from the database.
                var accountIds = await _repository.GetAccountIdsAsync();
                _logger.LogDebug("Fetched {Count} valid account IDs.", accountIds.Count);

                // Determine the set of accounts for which we need to fetch existing readings.
                // If specific records are provided, only fetch for those accounts to optimize.
                // Otherwise, fetch for all known accounts (less optimal if only a subset is needed).
                var relevantAccountIds = recordsToValidate?.Select(r => r.AccountId).Distinct().ToList() ?? accountIds.ToList();

                _logger.LogDebug("Fetching existing readings for {Count} relevant accounts.", relevantAccountIds.Count);
                // Fetch existing readings required by rules (e.g., OlderReadingRule).
                var existingReadings = await _repository.GetLatestReadingsForAccountsAsync(relevantAccountIds);

                // Update the instantiation of ValidationContext to ensure the first argument matches the expected type.
                var context = new ValidationContext(accountIds.ToHashSet(), existingReadings);
                _logger.LogDebug("Validation context built successfully.");
                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while building validation context.");
                // Re-throw the exception to be handled by the calling orchestrator.
                // Building the context is critical for validation.
                throw new InvalidOperationException("Failed to build validation context.", ex);
            }
        }

        /// <summary>
        /// Validates a single MeterReadingCsvRecord against all registered validation rules.
        /// Executes rules efficiently by running non-database rules first.
        /// </summary>
        /// <param name="record">The record to validate.</param>
        /// <param name="context">The pre-built validation context.</param>
        /// <returns>A list of error messages. An empty list indicates the record is valid.</returns>
        public async Task<List<string>> ValidateReadingAsync(MeterReadingCsvRecord record, ValidationContext context)
        {
            var errors = new List<string>();

            // --- Execute Non-DB rules first for efficiency ---
            // Filter rules that don't require database access.
            var nonDbRules = _validationRules.Where(r => !r.RequiresDbAccess).ToList();
            _logger.LogTrace("Executing {Count} non-DB validation rules.", nonDbRules.Count);
            foreach (var rule in nonDbRules)
            {
                var result = await rule.ValidateAsync(record, context);
                if (!result.IsValid)
                {
                    // Add error message if validation fails. Use a default message if none provided.
                    errors.Add(result.ErrorMessage ?? $"Validation failed (Rule: {rule.GetType().Name})");
                }
            }

            // --- If any Non-DB rule failed, stop validation here for this record ---
            // Example: If the format is wrong, no need to check for DB duplicates.
            if (errors.Any())
            {
                _logger.LogDebug("Validation stopped after non-DB rules failed: {Errors}", string.Join("; ", errors));
                return errors; // Return the errors found so far.
            }

            // --- Execute DB-dependent rules only if basic checks passed ---
            var dbRules = _validationRules.Where(r => r.RequiresDbAccess).ToList();
            _logger.LogTrace("Executing {Count} DB-dependent validation rules.", dbRules.Count);
            foreach (var rule in dbRules)
            {
                var result = await rule.ValidateAsync(record, context);
                if (!result.IsValid)
                {
                    errors.Add(result.ErrorMessage ?? $"Validation failed (Rule: {rule.GetType().Name})");
                }
            }
            _logger.LogTrace("Validation complete. Found {Count} errors.", errors.Count);
            return errors; // Return all accumulated errors.
        }
    }
}
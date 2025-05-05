using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Interfaces;
using Ensek.MeterReadings.Domain.Models; // Need models for saving
using Microsoft.Extensions.Logging;
using System.Collections.Generic; // Required for List
using System.Threading.Tasks; // Required for Task
using System; // Required for Exception
using System.IO; // Required for Stream

namespace Ensek.MeterReadings.Services
{
    /// <summary>
    /// Orchestrates the entire process of uploading meter readings from a CSV stream.
    /// Coordinates parsing, validation, and saving of data.
    /// </summary>
    public class MeterReadingUploadOrchestrator : IMeterReadingUploadOrchestrator
    {
        private readonly ICsvParsingService _parsingService;
        private readonly IMeterReadingValidationService _validationService;
        private readonly IMeterReadingRepository _repository;
        private readonly ILogger<MeterReadingUploadOrchestrator> _logger;

        /// <summary>
        /// Initializes a new instance of the MeterReadingUploadOrchestrator.
        /// </summary>
        /// <param name="parsingService">Service for parsing CSV data.</param>
        /// <param name="validationService">Service for validating records.</param>
        /// <param name="repository">Repository for data access.</param>
        /// <param name="logger">Logger instance.</param>
        public MeterReadingUploadOrchestrator(
           ICsvParsingService parsingService,
           IMeterReadingValidationService validationService,
           IMeterReadingRepository repository,
           ILogger<MeterReadingUploadOrchestrator> logger)
        {
            // Ensure dependencies are provided
            _parsingService = parsingService ?? throw new ArgumentNullException(nameof(parsingService));
            _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes the meter reading upload from a given CSV stream.
        /// </summary>
        /// <param name="csvStream">The stream containing the CSV data.</param>
        /// <param name="originalFileName">Optional name of the original file for logging/results.</param>
        /// <returns>A MeterReadingUploadResult summarizing the success and failures.</returns>
        public async Task<MeterReadingUploadResult> ProcessUploadAsync(Stream csvStream, string? originalFileName = null)
        {
            string logFileName = originalFileName ?? "Unnamed Stream";
            _logger.LogInformation("Starting upload process for file: {FileName}", logFileName);

            var result = new MeterReadingUploadResult { FileName = originalFileName };
            var validReadingsToSave = new List<MeterReads>(); // List to hold valid entities

            ValidationContext validationContext;
            try
            {
                // --- 1. Prepare Validation Context ---
                // Build the context needed for validation rules (fetches accounts, existing readings etc.)
                // Pass null initially as we don't have records yet to optimize fetching.
                validationContext = await _validationService.BuildValidationContextAsync(null);
            }
            catch (Exception ex)
            {
                // If context building fails, we cannot proceed with validation.
                _logger.LogCritical(ex, "Failed to build validation context for file {FileName}. Aborting processing.", logFileName);
                result.Errors.Add($"Critical Error: Failed to initialize validation context - {ex.Message}. Upload aborted.");
                result.FailedReadings = -1; // Indicate a total failure
                return result;
            }

            try
            {
                // --- 2. Process Stream Row by Row ---
                // Use IAsyncEnumerable to process the stream efficiently without loading all into memory.
                await foreach (var parseResult in _parsingService.ReadCsvStreamAsync(csvStream))
                {
                    string rowId = $"Row {parseResult.RowNumber}"; // Identifier for logging/errors

                    // --- 2a. Check Parsing ---
                    if (!parseResult.IsSuccess || parseResult.Record == null)
                    {
                        // If parsing failed for this row, record the error and skip to the next row.
                        result.FailedReadings++;
                        result.Errors.Add($"{rowId}: Parse Error - {parseResult.Error}");
                        _logger.LogWarning("Parsing failed for {RowId} in {FileName}: {Error}", rowId, logFileName, parseResult.Error);
                        continue;
                    }

                    var record = parseResult.Record;
                    // Create a detail string for better error context.
                    string recordDetail = $"Acc={record.AccountId}, Date={record.MeterReadingDateTime:dd/MM/yy HH:mm}, Val='{record.MeterReadValue}'";

                    // --- 2b. Validate Record ---
                    // Validate the successfully parsed record using the pre-built context.
                    var validationErrors = await _validationService.ValidateReadingAsync(record, validationContext);

                    if (validationErrors.Any())
                    {
                        // If validation errors exist, record them and mark the reading as failed.
                        result.FailedReadings++;
                        validationErrors.ForEach(e => result.Errors.Add($"{rowId}: {e} ({recordDetail})"));
                        _logger.LogWarning("Validation failed for {RowId} in {FileName}: {Errors}", rowId, logFileName, string.Join("; ", validationErrors));
                    }
                    else
                    {
                        // --- 2c. Prepare Valid Record for Saving ---
                        // If validation passes, convert the string value to int and create the entity.
                        // Parsing here is safe because MeterValueFormatRule should have ensured it's possible.
                        if (int.TryParse(record.MeterReadValue, out int meterValueInt))
                        {
                            validReadingsToSave.Add(new MeterReads
                            {
                                AccountId = record.AccountId,
                                MeterReadDateTime = record.MeterReadingDateTime,
                                MeterReadValue = meterValueInt
                            });
                        }
                        else
                        {
                            // This indicates an internal logic error if validation passed but parsing failed.
                            result.FailedReadings++;
                            result.Errors.Add($"{rowId}: Internal Error - Failed to parse validated value '{record.MeterReadValue}'. ({recordDetail})");
                            _logger.LogError("Internal error parsing validated value for {RowId} in {FileName}. Record: {@Record}", rowId, logFileName, record);
                        }
                    }
                } // End foreach row
            }
            catch (Exception ex)
            {
                // Catch unexpected errors during stream processing/validation loop
                _logger.LogCritical(ex, "Unexpected error during stream processing for file {FileName}. Processing stopped.", logFileName);
                result.Errors.Add($"Critical Error during processing: {ex.Message}. Results may be incomplete.");
                // Depending on where the error occurred, FailedReadings count might be inaccurate.
            }


            // --- 3. Save Valid Readings ---
            if (validReadingsToSave.Any())
            {
                _logger.LogInformation("Attempting to save {Count} valid readings from file {FileName}.", validReadingsToSave.Count, logFileName);
                try
                {
                    // Call the repository to save the collected valid readings in a single transaction.
                    int savedCount = await _repository.AddMeterReadingsAsync(validReadingsToSave);
                    result.SuccessfulReadings = savedCount;

                    // Check if the number saved matches the number intended. Discrepancy might indicate
                    // concurrency issues or duplicates missed by validation (if DB constraints are stricter).
                    if (savedCount != validReadingsToSave.Count)
                    {
                        int discrepancy = validReadingsToSave.Count - savedCount;
                        result.FailedReadings += discrepancy; // Add the difference to failed count
                        result.Errors.Add($"Database Warning: Only {savedCount} of {validReadingsToSave.Count} readings were saved. {discrepancy} failed, possibly due to duplicates or other DB constraints. Check logs.");
                        _logger.LogWarning("Partial save occurred for {FileName}: {SavedCount}/{AttemptedCount} readings saved.", logFileName, savedCount, validReadingsToSave.Count);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully saved {Count} readings from file {FileName}.", savedCount, logFileName);
                    }
                }
                catch (Exception ex) // Catch errors specifically during the database save operation.
                {
                    _logger.LogError(ex, "Database save operation failed for batch from file {FileName}.", logFileName);
                    // If saving fails, assume all readings in this batch failed.
                    result.FailedReadings += validReadingsToSave.Count;
                    result.SuccessfulReadings = 0; // Reset success count as the save failed.
                    result.Errors.Add($"Database Save Failed: {ex.Message}. None of the {validReadingsToSave.Count} prepared readings could be saved.");
                }
            }
            else
            {
                _logger.LogInformation("No valid readings found to save from file {FileName}.", logFileName);
            }

            // --- 4. Final Log and Return Result ---
            _logger.LogInformation("Upload processing finished for {FileName}. Success: {SuccessCount}, Failed: {FailedCount}", logFileName, result.SuccessfulReadings, result.FailedReadings);
            return result;
        }
    }
}
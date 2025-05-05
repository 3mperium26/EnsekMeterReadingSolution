using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using System.Runtime.CompilerServices; // For IAsyncEnumerable support

namespace Ensek.MeterReadings.Services
{
    /// <summary>
    /// Service responsible for parsing CSV streams into MeterReadingCsvRecord objects.
    /// </summary>
    public class CsvParsingService : ICsvParsingService
    {
        private readonly ILogger<CsvParsingService> _logger;

        /// <summary>
        /// CsvHelper mapping configuration for MeterReadingCsvRecord.
        /// Ensures correct mapping between CSV columns and object properties,
        /// including specific date formatting.
        /// </summary>
        public sealed class MeterReadingCsvRecordMap : ClassMap<MeterReadingCsvRecord>
        {
            public MeterReadingCsvRecordMap()
            {
                // Map CSV column "AccountId" to the AccountId property.
                Map(m => m.AccountId).Name("AccountId");
                // Map CSV column "MeterReadingDateTime" to the MeterReadingDateTime property.
                // Specify the expected date/time format in the CSV ("dd/MM/yyyy HH:mm").
                // CsvHelper will use this format for parsing the string into a DateTime object.
                Map(m => m.MeterReadingDateTime).Name("MeterReadingDateTime").TypeConverterOption.Format("dd/MM/yyyy HH:mm");
                // Map CSV column "MeterReadValue" to the MeterReadValue property (string).
                Map(m => m.MeterReadValue).Name("MeterReadValue");
            }
        }

        /// <summary>
        /// Initializes a new instance of the CsvParsingService.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public CsvParsingService(ILogger<CsvParsingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Asynchronously reads a CSV stream and yields parsing results for each row.
        /// </summary>
        /// <param name="csvStream">The stream containing the CSV data.</param>
        /// <returns>An asynchronous enumerable of CsvParseResult objects.</returns>
        public async IAsyncEnumerable<CsvParseResult<MeterReadingCsvRecord>> ReadCsvStreamAsync(Stream csvStream)
        {
            // Configure CsvHelper behavior
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) // Use invariant culture for consistency
            {
                HasHeaderRecord = true, // Expects a header row
                MissingFieldFound = null, // Don't throw an error if a field is missing
                HeaderValidated = null, // Don't strictly validate header names
                TrimOptions = TrimOptions.Trim, // Trim whitespace from fields
                // BadDataFound is handled per record below to provide row-specific errors
            };

            // Use using declarations for automatic disposal of reader and csv parser
            using var reader = new StreamReader(csvStream);
            using var csv = new CsvReader(reader, config);

            // Register the custom mapping configuration
            csv.Context.RegisterClassMap<MeterReadingCsvRecordMap>();

            int rowNumber = 1; // Start counting rows after the header
            // Read the CSV file row by row asynchronously
            while (await csv.ReadAsync())
            {
                rowNumber++; // Increment row number for error reporting
                MeterReadingCsvRecord? record = null;
                string? error = null;

                try
                {
                    // Attempt to parse the current row into a MeterReadingCsvRecord object
                    record = csv.GetRecord<MeterReadingCsvRecord>();
                    // CsvReader might return null for completely empty rows depending on config
                    if (record == null)
                    {
                        error = "Failed to parse row (empty or invalid structure).";
                    }
                }
                // Catch specific CsvHelper exceptions for more detailed error messages
                catch (CsvHelper.TypeConversion.TypeConverterException ex)
                {
                    // Error during conversion (e.g., invalid date format, non-numeric AccountId)
                    error = $"Type Conversion Error: {ex.Message} (Field Value: '{ex.Text}', Target Type: '{ex.MemberMapData?.Member?.Name}')";
                    _logger.LogWarning(ex, "CSV Type Conversion Error at row {RowNumber}", rowNumber);
                }
                catch (CsvHelper.MissingFieldException ex)
                {
                    // A required field (based on mapping) was missing
                    error = $"Missing Field Error: {ex.Message}";
                    _logger.LogWarning(ex, "CSV Missing Field Error at row {RowNumber}", rowNumber);
                }
                catch (CsvHelperException ex) // Catch other CsvHelper specific errors
                {
                    error = $"CSV Parsing Error: {ex.Message}";
                    _logger.LogWarning(ex, "CSV Parsing Error at row {RowNumber}", rowNumber);
                }
                catch (Exception ex) // Catch any other unexpected errors during parsing
                {
                    error = $"Unexpected Parsing Error: {ex.Message}";
                    _logger.LogError(ex, "Unexpected Parsing Error at row {RowNumber}", rowNumber);
                }

                // Yield the result (either success with the record or failure with an error message)
                if (record != null && error == null)
                {
                    yield return CsvParseResult<MeterReadingCsvRecord>.Success(rowNumber, record);
                }
                else
                {
                    yield return CsvParseResult<MeterReadingCsvRecord>.Failure(rowNumber, error ?? "Unknown parsing failure.");
                }
            }
        }
    }
}
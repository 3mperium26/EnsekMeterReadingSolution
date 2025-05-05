using Ensek.MeterReadings.Domain.Dtos; // Use DTOs from Domain

namespace Ensek.MeterReadings.Domain.Interfaces
{
    /// <summary>
    /// Parses meter readings from a CSV stream.
    /// SRP: Responsible only for parsing CSV data.
    /// </summary>
    public interface ICsvParsingService
    {
        /// <summary>
        /// Reads meter reading records from a CSV stream asynchronously.
        /// </summary>
        /// <param name="csvStream">The input stream containing CSV data.</param>
        /// <returns>An asynchronous enumerable of parsed records or parsing errors.</returns>
        IAsyncEnumerable<CsvParseResult<MeterReadingCsvRecord>> ReadCsvStreamAsync(Stream csvStream);
    }

    /// <summary>
    /// Represents the result of parsing a single CSV row.
    /// </summary>
    /// <typeparam name="T">The type of the successfully parsed record.</typeparam>
    public class CsvParseResult<T> where T : class
    {
        public T? Record { get; } // The parsed record if successful
        public string? Error { get; } // Error message if parsing failed for this row
        public int RowNumber { get; } // Original row number in the CSV

        public bool IsSuccess => Record != null && Error == null;

        private CsvParseResult(int rowNumber, T? record, string? error)
        {
            RowNumber = rowNumber;
            Record = record;
            Error = error;
        }

        public static CsvParseResult<T> Success(int rowNumber, T record) => new(rowNumber, record, null);
        public static CsvParseResult<T> Failure(int rowNumber, string error) => new(rowNumber, null, error);
    }
}
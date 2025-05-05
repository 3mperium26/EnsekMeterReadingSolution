namespace Ensek.MeterReadings.Domain.Dtos
{
    /// <summary>
    /// Represents a row read from the Meter_Reading.csv file.
    /// Used by CsvHelper for mapping.
    /// </summary>
    public class MeterReadingCsvRecord
    {
        public int AccountId { get; set; }
        public DateTime MeterReadingDateTime { get; set; }
        public string? MeterReadValue { get; set; } // Read as string for initial validation
    }
}
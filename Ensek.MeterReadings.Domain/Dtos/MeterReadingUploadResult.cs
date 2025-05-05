namespace Ensek.MeterReadings.Domain.Dtos
{
    /// <summary>
    /// DTO (Data Transfer Object) for the result of the upload process.
    /// </summary>
    public class MeterReadingUploadResult
    {
        public int SuccessfulReadings { get; set; }
        public int FailedReadings { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string? FileName { get; set; } // Optional: Add filename for context
    }
}
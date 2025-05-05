using Ensek.MeterReadings.Domain.Dtos; // Need DTO for result

namespace Ensek.MeterReadings.Domain.ViewModels
{
    // ViewModel for the MVC Upload View
    public class UploadViewModel
    {
        public MeterReadingUploadResult? UploadResult { get; set; }
    }
}
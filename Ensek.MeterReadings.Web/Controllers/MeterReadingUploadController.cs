using Microsoft.AspNetCore.Mvc;
using Ensek.MeterReadings.Domain.Interfaces; // Use Domain interfaces
using Ensek.MeterReadings.Domain.Dtos; // Use Domain DTOs
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.AspNetCore.Http; // Required for IFormFile, StatusCodes
using System; // Required for Exception, StringComparison
using System.Threading.Tasks; // Required for Task, async

namespace Ensek.MeterReadings.Web.Controllers.Api
{
    /// <summary>
    /// API Controller for handling meter reading uploads.
    /// </summary>
    [ApiController] // Indicates this is an API controller, enabling behaviors like automatic model validation
    [Route("api/meter-reading-uploads")] // Defines the base route for actions in this controller
    public class MeterReadingUploadController : ControllerBase
    {
        private readonly IMeterReadingUploadOrchestrator _uploadOrchestrator;
        private readonly ILogger<MeterReadingUploadController> _logger;

        /// <summary>
        /// Initializes a new instance of the MeterReadingUploadController.
        /// </summary>
        /// <param name="uploadOrchestrator">The orchestrator service instance.</param>
        /// <param name="logger">The logger instance.</param>
        public MeterReadingUploadController(
            IMeterReadingUploadOrchestrator uploadOrchestrator,
            ILogger<MeterReadingUploadController> logger)
        {
            _uploadOrchestrator = uploadOrchestrator ?? throw new ArgumentNullException(nameof(uploadOrchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes a CSV file containing meter readings uploaded via the API.
        /// Accepts multipart/form-data with a file field named 'file'.
        /// </summary>
        /// <param name="file">The uploaded CSV file.</param>
        /// <returns>An IActionResult containing the processing summary (MeterReadingUploadResult).</returns>
        /// <response code="200">File processed successfully. Returns processing summary.</response>
        /// <response code="400">Bad request. Invalid input (e.g., no file, wrong type, file too large). Returns ValidationProblemDetails.</response>
        /// <response code="500">Internal server error. An unexpected error occurred during processing. Returns ProblemDetails.</response>
        [HttpPost] // Handles POST requests to the base route: api/meter-reading-uploads
        [ProducesResponseType(typeof(MeterReadingUploadResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        // Apply request size limit to prevent excessively large uploads
        [RequestSizeLimit(10 * 1024 * 1024)] // Example: 10 MB limit
                                             // Parameter name 'file' must match the name attribute of the file input in the consuming client/form
        public async Task<IActionResult> UploadMeterReadingsApi(IFormFile file)
        {
            // --- 1. Input Validation ---
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("API Upload: No file or empty file received.");
                // Add error to ModelState for automatic ValidationProblemDetails generation
                ModelState.AddModelError(nameof(file), "Please provide a file to upload.");
                return BadRequest(new ValidationProblemDetails(ModelState));
            }

            // Validate file extension and MIME type
            string? fileName = file.FileName;
            string? contentType = file.ContentType;
            bool isValidCsvExtension = fileName != null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            // Allow common CSV MIME types
            bool isValidMimeType = contentType != null &&
                                   (string.Equals(contentType, "text/csv", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "application/csv", StringComparison.OrdinalIgnoreCase));


            if (!isValidCsvExtension || !isValidMimeType)
            {
                _logger.LogWarning("API Upload: Invalid file type/name: {FileName}, ContentType: {ContentType}", fileName, contentType);
                ModelState.AddModelError(nameof(file), "Invalid file type. Please upload a valid CSV file (.csv).");
                return BadRequest(new ValidationProblemDetails(ModelState));
            }

            _logger.LogInformation("API Upload: Received file {FileName} ({Length} bytes). Processing...", fileName, file.Length);

            // --- 2. Processing Logic ---
            try
            {
                // Get the stream from the uploaded file
                using var stream = file.OpenReadStream();
                // Call the orchestrator service to handle the core logic
                var result = await _uploadOrchestrator.ProcessUploadAsync(stream, fileName); // Pass filename

                _logger.LogInformation("API Upload Processing completed for {FileName}. Success: {SuccessCount}, Failed: {FailedCount}",
                    fileName, result.SuccessfulReadings, result.FailedReadings);

                // --- 3. Return Result ---
                // Return 200 OK with the result object. The client is responsible for inspecting
                // the SuccessfulReadings, FailedReadings, and Errors properties.
                return Ok(result);
            }
            catch (Exception ex) // Catch unexpected errors during orchestration or saving
            {
                _logger.LogError(ex, "API Upload: Unexpected error processing file {FileName}.", fileName);
                // Return a standardized 500 Internal Server Error response
                return StatusCode(StatusCodes.Status500InternalServerError,
                     new ProblemDetails // Use ProblemDetails for RFC 7807 compliance
                     {
                         Title = "Internal Server Error",
                         Detail = $"An unexpected error occurred while processing the file '{fileName}'. Please check server logs or contact support.",
                         Status = StatusCodes.Status500InternalServerError,
                         // Include instance to help correlate logs with specific requests
                         Instance = HttpContext.Request.Path
                     });
            }
        }
    }
}
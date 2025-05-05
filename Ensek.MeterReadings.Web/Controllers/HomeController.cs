using Microsoft.AspNetCore.Mvc;
using System.Diagnostics; // Required for Activity
using Ensek.MeterReadings.Domain.Interfaces; // Use Domain interfaces
using Ensek.MeterReadings.Domain.ViewModels; // Use ViewModel
using Ensek.MeterReadings.Domain.Dtos; // Use Domain DTOs
using Ensek.MeterReadings.Web.Models; // Required for JsonSerializer
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.AspNetCore.Http; // Required for IFormFile
using System; // Required for Exception, StringComparison
using System.Threading.Tasks; // Required for Task, async
using System.Linq; // Required for LINQ methods like SelectMany
using System.Collections.Generic; // Required for List
using System.Text.Json;

namespace Ensek.MeterReadings.Web.Controllers
{
    /// <summary>
    /// Controller for handling home page actions, including MVC file uploads.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IMeterReadingUploadOrchestrator _uploadOrchestrator;

        /// <summary>
        /// Initializes a new instance of the HomeController.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="uploadOrchestrator">Orchestrator service instance.</param>
        public HomeController(ILogger<HomeController> logger, IMeterReadingUploadOrchestrator uploadOrchestrator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uploadOrchestrator = uploadOrchestrator ?? throw new ArgumentNullException(nameof(uploadOrchestrator));
        }

        /// <summary>
        /// GET action for the Index page. Displays the upload form and any results
        /// passed via TempData from a previous POST request (Post-Redirect-Get pattern).
        /// </summary>
        /// <returns>The Index view.</returns>
        [HttpGet]
        public IActionResult Index()
        {
            var viewModel = new UploadViewModel(); // Start with an empty view model

            // --- Retrieve results/errors from TempData (set by the POST action) ---

            // Check for upload results
            if (TempData["UploadResultJson"] is string jsonResult)
            {
                try
                {
                    // Attempt to deserialize the result from JSON stored in TempData
                    var uploadResult = JsonSerializer.Deserialize<MeterReadingUploadResult>(jsonResult);
                    viewModel.UploadResult = uploadResult; // Assign to the view model
                    _logger.LogInformation("Displaying upload results from TempData for file {FileName}", uploadResult?.FileName ?? "N/A");
                }
                catch (JsonException ex)
                {
                    // Log error if deserialization fails and inform the user
                    _logger.LogError(ex, "Failed to deserialize UploadResult from TempData.");
                    ModelState.AddModelError("", "Error: Could not display previous upload results due to a data issue.");
                }
            }

            // Check for validation errors stored from the POST action
            if (TempData["ModelStateErrors"] is string jsonErrors)
            {
                try
                {
                    // Deserialize the list of error messages
                    var errors = JsonSerializer.Deserialize<List<string>>(jsonErrors);
                    if (errors != null)
                    {
                        // Add each error back to the ModelState to be displayed in the validation summary
                        foreach (var error in errors) { ModelState.AddModelError(string.Empty, error); }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize ModelStateErrors from TempData.");
                    ModelState.AddModelError("", "Error: Could not display previous validation errors due to a data issue.");
                }
            }

            // Check for general processing errors stored from the POST action
            if (TempData["ProcessingError"] is string procError)
            {
                ModelState.AddModelError(string.Empty, procError); // Add the processing error message
            }

            // Return the view with the populated view model (containing results) and any errors added to ModelState.
            return View(viewModel);
        }

        /// <summary>
        /// POST action for handling the file upload from the MVC form.
        /// Implements the Post-Redirect-Get (PRG) pattern to prevent form resubmission on refresh.
        /// </summary>
        /// <param name="meterReadingFile">The uploaded file from the form (name must match input field).</param>
        /// <returns>A RedirectToAction result, redirecting back to the GET Index action.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken] // Prevent Cross-Site Request Forgery attacks
        [RequestSizeLimit(10 * 1024 * 1024)] // Example: 10 MB file size limit
        public async Task<IActionResult> Upload(IFormFile? meterReadingFile)
        {
            // --- 1. Basic File Validation ---
            if (meterReadingFile == null || meterReadingFile.Length == 0)
            {
                ModelState.AddModelError("meterReadingFile", "Please select a file to upload.");
                _logger.LogWarning("MVC Upload: No file selected.");
                // Store errors in TempData and redirect
                TempData["ModelStateErrors"] = JsonSerializer.Serialize(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            // Validate file extension and MIME type
            string? fileName = meterReadingFile.FileName;
            string? contentType = meterReadingFile.ContentType;
            bool isValidCsvExtension = fileName != null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            // Allow common CSV MIME types
            bool isValidMimeType = contentType != null &&
                                   (string.Equals(contentType, "text/csv", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "application/csv", StringComparison.OrdinalIgnoreCase));


            if (!isValidCsvExtension || !isValidMimeType)
            {
                ModelState.AddModelError("meterReadingFile", "Invalid file type. Please upload a valid CSV file (.csv).");
                _logger.LogWarning("MVC Upload: Invalid file type/name: {FileName}, ContentType: {ContentType}", fileName, contentType);
                TempData["ModelStateErrors"] = JsonSerializer.Serialize(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Index));
            }

            // --- 2. Process the File ---
            _logger.LogInformation("MVC Upload: Received file {FileName} ({Length} bytes). Processing...", fileName, meterReadingFile.Length);
           MeterReadingUploadResult? uploadResult = null;

            try
            {
                // Open the file stream and pass it to the orchestrator service
                using (var stream = meterReadingFile.OpenReadStream())
                {
                    uploadResult = await _uploadOrchestrator.ProcessUploadAsync(stream, fileName);
                }

                _logger.LogInformation("MVC Upload Processing completed for {FileName}. Success: {SuccessCount}, Failed: {FailedCount}",
                   fileName, uploadResult?.SuccessfulReadings ?? 0, uploadResult?.FailedReadings ?? 0);

                // Store the successful result in TempData for the redirect
                TempData["UploadResultJson"] = JsonSerializer.Serialize(uploadResult);

            }
            catch (Exception ex) // Catch unexpected errors during processing
            {
                _logger.LogError(ex, "MVC Upload: Error processing file {FileName}", fileName);
                // Store a generic error message in TempData for the redirect
                TempData["ProcessingError"] = $"An unexpected error occurred while processing the file '{fileName}'. Please check logs or try again later.";
                // Optionally serialize the exception message (be careful about exposing sensitive info)
                // TempData["ProcessingError"] = $"Error processing '{fileName}': {ex.Message}";
            }

            // --- 3. Redirect back to the GET Index action ---
            // The GET action will pick up the results/errors from TempData and display them.
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Standard MVC Error action.
        /// </summary>
        /// <returns>The Error view.</returns>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Create an ErrorViewModel with the current Activity ID (if available) for tracing.
            var errorViewModel = new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier };
            return View(errorViewModel);
        }
    }
}
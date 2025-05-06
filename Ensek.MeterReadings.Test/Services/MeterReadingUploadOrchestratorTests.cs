using Microsoft.VisualStudio.TestTools.UnitTesting; // Use MSTest
using Moq;
using Microsoft.Extensions.Logging;
using Ensek.MeterReadings.Services; // Service implementation
using Ensek.MeterReadings.Domain.Interfaces; // Interfaces
using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Models;
using System.Threading.Tasks; // For Task
using System.Collections.Generic; // For List, IEnumerable
using System.Linq; // For LINQ methods
using System.IO; // For MemoryStream
using System.Text; // For Encoding
using System; // For Exception, DateTime

namespace Ensek.MeterReadings.Test.Services
{
    [TestClass] // MSTest attribute
    public class MeterReadingUploadOrchestratorTests
    {
        // Use fields initialized in TestInitialize
        private Mock<ICsvParsingService> _mockParsingService = null!;
        private Mock<IMeterReadingValidationService> _mockValidationService = null!;
        private Mock<IMeterReadingRepository> _mockRepository = null!;
        private Mock<ILogger<MeterReadingUploadOrchestrator>> _mockLogger = null!;
        private MeterReadingUploadOrchestrator _orchestrator = null!;
        private ValidationContext _defaultValidationContext = null!;

        // MSTest initialization method
        [TestInitialize]
        public void TestInitialize()
        {
            _mockParsingService = new Mock<ICsvParsingService>();
            _mockValidationService = new Mock<IMeterReadingValidationService>();
            _mockRepository = new Mock<IMeterReadingRepository>();
            _mockLogger = new Mock<ILogger<MeterReadingUploadOrchestrator>>();

            // Create a default validation context for convenience
            _defaultValidationContext = new ValidationContext(
                new HashSet<int> { 1, 2, 3 }, // Example valid accounts
                new List<MeterReads>().ToLookup(r => r.AccountId) // Empty existing readings
            );

            // Default setup for validation context building - return the default context
            _mockValidationService.Setup(v => v.BuildValidationContextAsync(It.IsAny<IEnumerable<MeterReadingCsvRecord>?>()))
                                  .ReturnsAsync(_defaultValidationContext);

            // Create the orchestrator instance for tests
            _orchestrator = new MeterReadingUploadOrchestrator(
                _mockParsingService.Object,
                _mockValidationService.Object,
                _mockRepository.Object,
                _mockLogger.Object);
        }

        // Helper to create a stream from string
        private MemoryStream CreateStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Helper to create an IAsyncEnumerable from a list
        private async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                yield return item;
                await Task.CompletedTask; // Simulate async behavior if needed
            }
        }

        [TestMethod] // MSTest attribute
        public async Task ProcessUploadAsync_AllRecordsValid_SavesAllAndReturnsSuccess()
        {
            // Arrange
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue\n1,22/04/2019 09:24,00001\n2,23/04/2019 10:00,00002";
            using var stream = CreateStream(csvContent);
            var fileName = "valid.csv";

            var parsedRecords = new List<CsvParseResult<MeterReadingCsvRecord>> {
                CsvParseResult<MeterReadingCsvRecord>.Success(2, new MeterReadingCsvRecord { AccountId = 1, MeterReadingDateTime = new DateTime(2019,4,22,9,24,0), MeterReadValue = "00001" }),
                CsvParseResult<MeterReadingCsvRecord>.Success(3, new MeterReadingCsvRecord { AccountId = 2, MeterReadingDateTime = new DateTime(2019,4,23,10,0,0), MeterReadValue = "00002" })
            };

            _mockParsingService.Setup(p => p.ReadCsvStreamAsync(It.IsAny<Stream>()))
                               .Returns(CreateAsyncEnumerable(parsedRecords));

            // Simulate validation succeeding for all records
            _mockValidationService.Setup(v => v.ValidateReadingAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                                  .ReturnsAsync(new List<string>()); // Empty list means valid

            // Simulate repository saving successfully
            _mockRepository.Setup(r => r.AddMeterReadingsAsync(It.IsAny<IEnumerable<MeterReads>>()))
                           .ReturnsAsync(2); // Simulate 2 records saved

            // Act
            var result = await _orchestrator.ProcessUploadAsync(stream, fileName);

            // Assert
            Assert.AreEqual(2, result.SuccessfulReadings, "SuccessfulReadings count mismatch");
            Assert.AreEqual(0, result.FailedReadings, "FailedReadings count should be 0");
            Assert.AreEqual(0, result.Errors.Count, "Errors list should be empty");
            Assert.AreEqual(fileName, result.FileName, "FileName mismatch");
            // Verify repository was called once with exactly 2 MeterReading objects
            _mockRepository.Verify(r => r.AddMeterReadingsAsync(It.Is<IEnumerable<MeterReads>>(list => list.Count() == 2)), Times.Once, "Repository AddMeterReadingsAsync call verification failed");
        }

        [TestMethod]
        public async Task ProcessUploadAsync_SomeRecordsInvalid_SavesValidAndReturnsPartialFailure()
        {
            // Arrange
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue\n1,22/04/2019 09:24,00001\nINVALID\n3,23/04/2019 10:00,00003";
            using var stream = CreateStream(csvContent);

            var parsedRecords = new List<CsvParseResult<MeterReadingCsvRecord>> {
                CsvParseResult<MeterReadingCsvRecord>.Success(2, new MeterReadingCsvRecord { AccountId = 1, MeterReadingDateTime = new DateTime(2019,4,22,9,24,0), MeterReadValue = "00001" }),
                CsvParseResult<MeterReadingCsvRecord>.Failure(3, "Parsing failed"), // Simulate parsing error
                CsvParseResult<MeterReadingCsvRecord>.Success(4, new MeterReadingCsvRecord { AccountId = 3, MeterReadingDateTime = new DateTime(2019,4,23,10,0,0), MeterReadValue = "00003" })
            };

            _mockParsingService.Setup(p => p.ReadCsvStreamAsync(It.IsAny<Stream>()))
                               .Returns(CreateAsyncEnumerable(parsedRecords));

            // Simulate validation: Succeeds for record 1, Fails for record 3
            _mockValidationService.Setup(v => v.ValidateReadingAsync(It.Is<MeterReadingCsvRecord>(r => r.AccountId == 1), It.IsAny<ValidationContext>()))
                                 .ReturnsAsync(new List<string>()); // Record 1 is valid
            _mockValidationService.Setup(v => v.ValidateReadingAsync(It.Is<MeterReadingCsvRecord>(r => r.AccountId == 3), It.IsAny<ValidationContext>()))
                                 .ReturnsAsync(new List<string> { "Validation Rule Failed" }); // Record 3 is invalid

            // Simulate repository saving successfully (only 1 record should be passed)
            _mockRepository.Setup(r => r.AddMeterReadingsAsync(It.IsAny<IEnumerable<MeterReads>>()))
                           .ReturnsAsync(1); // Simulate 1 record saved

            // Act
            var result = await _orchestrator.ProcessUploadAsync(stream);

            // Assert
            Assert.AreEqual(1, result.SuccessfulReadings, "SuccessfulReadings count mismatch"); // Only record 1 was valid
            Assert.AreEqual(2, result.FailedReadings, "FailedReadings count mismatch"); // Record 2 (parsing) + Record 3 (validation)
            Assert.AreEqual(2, result.Errors.Count, "Errors count mismatch");
            StringAssert.Contains(result.Errors[0], "Row 3: Parse Error - Parsing failed", "Error message 1 mismatch");
            StringAssert.Contains(result.Errors[1], "Row 4: Validation Rule Failed", "Error message 2 mismatch");
            // Verify AddMeterReadingsAsync was called with only the single valid record
            _mockRepository.Verify(r => r.AddMeterReadingsAsync(It.Is<IEnumerable<MeterReads>>(
                list => list.Count() == 1 && list.First().AccountId == 1)),
                Times.Once, "Repository AddMeterReadingsAsync call verification failed");
        }

        [TestMethod]
        public async Task ProcessUploadAsync_DatabaseSaveFails_ReturnsZeroSuccessAndIncludesDbError()
        {
            // Arrange
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue\n1,22/04/2019 09:24,00001";
            using var stream = CreateStream(csvContent);

            var parsedRecords = new List<CsvParseResult<MeterReadingCsvRecord>> {
                CsvParseResult<MeterReadingCsvRecord>.Success(2, new MeterReadingCsvRecord { AccountId = 1, MeterReadingDateTime = new DateTime(2019,4,22,9,24,0), MeterReadValue = "00001" })
            };

            _mockParsingService.Setup(p => p.ReadCsvStreamAsync(It.IsAny<Stream>()))
                               .Returns(CreateAsyncEnumerable(parsedRecords));
            _mockValidationService.Setup(v => v.ValidateReadingAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                                  .ReturnsAsync(new List<string>()); // Valid record

            // Simulate repository throwing an exception during save
            var dbException = new Exception("Database connection lost");
            _mockRepository.Setup(r => r.AddMeterReadingsAsync(It.IsAny<IEnumerable<MeterReads>>()))
                           .ThrowsAsync(dbException);

            // Act
            var result = await _orchestrator.ProcessUploadAsync(stream);

            // Assert
            Assert.AreEqual(0, result.SuccessfulReadings, "SuccessfulReadings should be 0 on DB save failure");
            Assert.AreEqual(1, result.FailedReadings, "FailedReadings should include the record that failed to save");
            Assert.AreEqual(1, result.Errors.Count, "Should contain one error message");
            StringAssert.Contains(result.Errors[0], "Database Save Failed", "Error message prefix mismatch");
            StringAssert.Contains(result.Errors[0], dbException.Message, "Error message should contain original exception message");
            _mockRepository.Verify(r => r.AddMeterReadingsAsync(It.IsAny<IEnumerable<MeterReads>>()), Times.Once, "Repository AddMeterReadingsAsync should be called once");
        }

        [TestMethod]
        public async Task ProcessUploadAsync_PartialDatabaseSave_AdjustsCountsAndIncludesWarning()
        {
            // Arrange - Two valid records
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue\n1,22/04/2019 09:24,00001\n2,23/04/2019 10:00,00002";
            using var stream = CreateStream(csvContent);

            var parsedRecords = new List<CsvParseResult<MeterReadingCsvRecord>> {
                CsvParseResult<MeterReadingCsvRecord>.Success(2, new MeterReadingCsvRecord { AccountId = 1, MeterReadingDateTime = new DateTime(2019,4,22,9,24,0), MeterReadValue = "00001" }),
                CsvParseResult<MeterReadingCsvRecord>.Success(3, new MeterReadingCsvRecord { AccountId = 2, MeterReadingDateTime = new DateTime(2019,4,23,10,0,0), MeterReadValue = "00002" })
            };

            _mockParsingService.Setup(p => p.ReadCsvStreamAsync(It.IsAny<Stream>()))
                               .Returns(CreateAsyncEnumerable(parsedRecords));
            _mockValidationService.Setup(v => v.ValidateReadingAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                                  .ReturnsAsync(new List<string>()); // Both valid

            // Simulate repository saving only 1 out of 2 records (e.g., due to concurrency/constraint)
            _mockRepository.Setup(r => r.AddMeterReadingsAsync(It.Is<IEnumerable<MeterReads>>(list => list.Count() == 2)))
                           .ReturnsAsync(1); // Only 1 saved

            // Act
            var result = await _orchestrator.ProcessUploadAsync(stream);

            // Assert
            Assert.AreEqual(1, result.SuccessfulReadings, "SuccessfulReadings count mismatch (should be 1)"); // Repo reported 1 saved
            Assert.AreEqual(1, result.FailedReadings, "FailedReadings count mismatch (should be 1)"); // (Total Valid - Saved) = (2 - 1) = 1 failed
            Assert.AreEqual(1, result.Errors.Count, "Should contain one warning message");
            StringAssert.Contains(result.Errors[0], "Database Warning: Only 1 of 2 readings were saved", "Warning message mismatch");
            _mockRepository.Verify(r => r.AddMeterReadingsAsync(It.Is<IEnumerable<MeterReads>>(list => list.Count() == 2)), Times.Once, "Repository AddMeterReadingsAsync call verification failed");
        }

        [TestMethod]
        public async Task ProcessUploadAsync_ContextBuildFails_ReturnsCriticalError()
        {
            // Arrange
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue\n1,22/04/2019 09:24,00001";
            using var stream = CreateStream(csvContent);
            var contextException = new InvalidOperationException("Cannot connect to DB to get accounts");

            // Simulate context building failing by overriding the default setup
            _mockValidationService.Setup(v => v.BuildValidationContextAsync(It.IsAny<IEnumerable<MeterReadingCsvRecord>?>()))
                                  .ThrowsAsync(contextException);

            // Recreate orchestrator instance *after* setting up the specific exception for this test
            var orchestrator = new MeterReadingUploadOrchestrator(
                _mockParsingService.Object,
                _mockValidationService.Object,
                _mockRepository.Object,
                _mockLogger.Object);


            // Act
            var result = await orchestrator.ProcessUploadAsync(stream); // Use the locally created orchestrator

            // Assert
            Assert.AreEqual(0, result.SuccessfulReadings, "SuccessfulReadings should be 0 on critical context failure");
            Assert.AreEqual(-1, result.FailedReadings, "FailedReadings should indicate total failure (-1)");
            Assert.AreEqual(1, result.Errors.Count, "Should contain one critical error message");
            StringAssert.Contains(result.Errors[0], "Critical Error: Failed to initialize validation context", "Error message prefix mismatch");
            StringAssert.Contains(result.Errors[0], contextException.Message, "Error message should contain original exception message");

            // Verify parsing and saving were NOT attempted because context build failed
            _mockParsingService.Verify(p => p.ReadCsvStreamAsync(It.IsAny<Stream>()), Times.Never, "Parsing service should not be called if context build fails");
            _mockRepository.Verify(r => r.AddMeterReadingsAsync(It.IsAny<IEnumerable<MeterReads>>()), Times.Never, "Repository should not be called if context build fails");
        }
    }
}
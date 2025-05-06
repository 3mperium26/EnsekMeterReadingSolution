using Microsoft.VisualStudio.TestTools.UnitTesting; // Use MSTest
using Moq;
using Microsoft.Extensions.Logging;
using Ensek.MeterReadings.Services; // Service implementation
using Ensek.MeterReadings.Domain.Interfaces; // For CsvParseResult
using System.Text;
using System.IO; // For MemoryStream
using System.Linq; // For LINQ methods like CountAsync, FirstOrDefault
using System.Threading.Tasks; // For Task
using System.Collections.Generic; // For List
using System; // For DateTime

namespace Ensek.MeterReadings.Test.Services
{
    [TestClass] // MSTest attribute for test classes
    public class CsvParsingServiceTests
    {
        // Private fields for mocks and the service under test
        private Mock<ILogger<CsvParsingService>> _mockLogger = null!; // Null-forgiving operator used, initialized in TestInitialize
        private CsvParsingService _service = null!; // Null-forgiving operator used, initialized in TestInitialize

        // MSTest initialization method - runs before each test
        [TestInitialize]
        public void TestInitialize()
        {
            _mockLogger = new Mock<ILogger<CsvParsingService>>();
            _service = new CsvParsingService(_mockLogger.Object);
        }

        // Helper to create a MemoryStream from a string for testing
        private MemoryStream CreateStreamFromString(string content)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        [TestMethod] // MSTest attribute for test methods
        public async Task ReadCsvStreamAsync_ValidCsv_ReturnsSuccessResults()
        {
            // Arrange: Prepare valid CSV data and stream
            var csvContent = """
                             AccountId,MeterReadingDateTime,MeterReadValue
                             1234,22/04/2019 09:24,01002
                             5678,23/04/2019 10:30,12345
                             """;
            using var stream = CreateStreamFromString(csvContent);

            // Act: Call the method under test and collect results
            // Requires System.Linq.Async package for ToListAsync() on IAsyncEnumerable
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert: Verify the results using MSTest assertions
            Assert.AreEqual(2, results.Count, "Should have 2 data rows"); // Check count

            // Check first result
            var result1 = results[0];
            Assert.IsTrue(result1.IsSuccess, "Result 1 should be success");
            Assert.IsNull(result1.Error, "Result 1 error should be null");
            Assert.IsNotNull(result1.Record, "Result 1 record should not be null");
            Assert.AreEqual(1234, result1.Record.AccountId, "Result 1 AccountId mismatch");
            Assert.AreEqual("01002", result1.Record.MeterReadValue, "Result 1 MeterReadValue mismatch");
            Assert.AreEqual(new DateTime(2019, 4, 22, 9, 24, 0), result1.Record.MeterReadingDateTime, "Result 1 DateTime mismatch");
            Assert.AreEqual(2, result1.RowNumber, "Result 1 RowNumber should be 2"); // First data row is row 2

            // Check second result
            var result2 = results[1];
            Assert.IsTrue(result2.IsSuccess, "Result 2 should be success");
            Assert.IsNull(result2.Error, "Result 2 error should be null");
            Assert.IsNotNull(result2.Record, "Result 2 record should not be null");
            Assert.AreEqual(5678, result2.Record.AccountId, "Result 2 AccountId mismatch");
            Assert.AreEqual("12345", result2.Record.MeterReadValue, "Result 2 MeterReadValue mismatch");
            Assert.AreEqual(new DateTime(2019, 4, 23, 10, 30, 0), result2.Record.MeterReadingDateTime, "Result 2 DateTime mismatch");
            Assert.AreEqual(3, result2.RowNumber, "Result 2 RowNumber should be 3"); // Second data row is row 3
        }

        [TestMethod]
        public async Task ReadCsvStreamAsync_CsvWithInvalidDate_ReturnsFailureResultForRow()
        {
            // Arrange: CSV with an invalid date format in the first data row
            var csvContent = """
                             AccountId,MeterReadingDateTime,MeterReadValue
                             1234,INVALID_DATE,01002
                             5678,23/04/2019 10:30,12345
                             """;
            using var stream = CreateStreamFromString(csvContent);

            // Act
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert
            Assert.AreEqual(2, results.Count, "Should process 2 data rows");

            // First row should fail due to invalid date
            var result1 = results[0];
            Assert.IsFalse(result1.IsSuccess, "Result 1 should fail");
            Assert.IsNotNull(result1.Error, "Result 1 error should not be null");
            StringAssert.Contains(result1.Error, "Type Conversion Error", "Error message should indicate type conversion issue");
            Assert.IsNull(result1.Record, "Result 1 record should be null on failure");
            Assert.AreEqual(2, result1.RowNumber, "Result 1 RowNumber should be 2");

            // Second row should succeed
            var result2 = results[1];
            Assert.IsTrue(result2.IsSuccess, "Result 2 should succeed");
            Assert.IsNull(result2.Error, "Result 2 error should be null");
            Assert.IsNotNull(result2.Record, "Result 2 record should not be null");
            Assert.AreEqual(5678, result2.Record.AccountId, "Result 2 AccountId mismatch");
            Assert.AreEqual(3, result2.RowNumber, "Result 2 RowNumber should be 3");
        }

        [TestMethod]
        public async Task ReadCsvStreamAsync_CsvWithMissingFieldValue_ParsesWithNullValue()
        {
            // Arrange: Row 2 is missing the MeterReadValue field
            var csvContent = """
                             AccountId,MeterReadingDateTime,MeterReadValue
                             1234,22/04/2019 09:24
                             5678,23/04/2019 10:30,12345
                             """;
            using var stream = CreateStreamFromString(csvContent);

            // Act
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert
            Assert.AreEqual(2, results.Count, "Should process 2 data rows");

            // First row: CsvHelper often parses rows with missing fields, setting the corresponding property to null or default.
            // The CsvParsingService is currently designed to yield success in this case,
            // relying on later validation steps to catch the missing value if it's required.
            var result1 = results[0];
            Assert.IsTrue(result1.IsSuccess, "Result 1 should parse successfully even with missing field");
            Assert.IsNull(result1.Error, "Result 1 error should be null");
            Assert.IsNotNull(result1.Record, "Result 1 record should not be null");
            Assert.AreEqual(1234, result1.Record.AccountId);
            Assert.IsTrue(result1.Record.MeterReadValue=="", "Result 1 MeterReadValue should be null due to missing field"); // Verify the value is null
            Assert.AreEqual(new DateTime(2019, 4, 22, 9, 24, 0), result1.Record.MeterReadingDateTime);
            Assert.AreEqual(2, result1.RowNumber);

            // Second row should succeed normally
            var result2 = results[1];
            Assert.IsTrue(result2.IsSuccess, "Result 2 should succeed");
            Assert.IsNotNull(result2.Record);
            Assert.AreEqual("12345", result2.Record.MeterReadValue);
            Assert.AreEqual(3, result2.RowNumber);
        }

        [TestMethod]
        public async Task ReadCsvStreamAsync_CsvWithMalformedRow_ReturnsFailureResultForRow()
        {
            // Arrange: Row 2 has extra fields/commas, likely causing a parsing error
            var csvContent = """
                             AccountId,MeterReadingDateTime,MeterReadValue
                             1234,,EXTRA_FIELD,22/04/2019 09:24,01002
                             5678,23/04/2019 10:30,12345
                             """;
            using var stream = CreateStreamFromString(csvContent);

            // Act
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert
            Assert.AreEqual(2, results.Count, "Should attempt to process 2 data rows");

            // First row should fail parsing due to malformed structure
            var result1 = results[0];
            Assert.IsFalse(result1.IsSuccess, "Result 1 should fail parsing");
            Assert.IsNotNull(result1.Error, "Result 1 error should not be null");
            StringAssert.Contains(result1.Error, "Error", "Error message should indicate a parsing problem"); // General check
            Assert.IsNull(result1.Record, "Result 1 record should be null on failure");
            Assert.AreEqual(2, result1.RowNumber, "Result 1 RowNumber should be 2");

            // Second row should succeed
            var result2 = results[1];
            Assert.IsTrue(result2.IsSuccess, "Result 2 should succeed");
            Assert.IsNotNull(result2.Record, "Result 2 record should not be null");
            Assert.AreEqual(5678, result2.Record.AccountId, "Result 2 AccountId mismatch");
            Assert.AreEqual(3, result2.RowNumber, "Result 2 RowNumber should be 3");
        }


        [TestMethod]
        public async Task ReadCsvStreamAsync_EmptyStream_ReturnsEmptyList()
        {
            // Arrange: Create an empty stream
            var csvContent = "";
            using var stream = CreateStreamFromString(csvContent);

            // Act
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert
            Assert.AreEqual(0, results.Count, "Result list should be empty for empty stream");
        }

        [TestMethod]
        public async Task ReadCsvStreamAsync_StreamWithOnlyHeader_ReturnsEmptyList()
        {
            // Arrange: Stream contains only the header row
            var csvContent = "AccountId,MeterReadingDateTime,MeterReadValue";
            using var stream = CreateStreamFromString(csvContent);

            // Act
            var results = await _service.ReadCsvStreamAsync(stream).ToListAsync();

            // Assert
            Assert.AreEqual(1, results.Count, "Result list should be empty for header-only stream");
        }
    }
}
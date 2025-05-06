using Microsoft.VisualStudio.TestTools.UnitTesting; // Use MSTest
using Moq;
using Ensek.MeterReadings.Services.Validation; // Rule implementations
using Ensek.MeterReadings.Domain.Interfaces; // Interfaces (IValidationRule, ValidationContext, etc.)
using Ensek.MeterReadings.Domain.Dtos; // MeterReadingCsvRecord
using Ensek.MeterReadings.Domain.Models; // MeterReading (Model)
using System.Threading.Tasks; // For Task
using System.Collections.Generic; // For List, HashSet, ILookup
using System.Linq; // For ILookup creation
using System; // For DateTime

namespace Ensek.MeterReadings.Test.Services
{
    [TestClass] // MSTest attribute
    public class ValidationRuleTests
    {
        // --- Helper Methods ---
        private ValidationContext CreateContext(
            HashSet<int>? validAccountIds = null,
            ILookup<int, MeterReads>? existingReadings = null)
        {
            // Provide defaults if null
            validAccountIds ??= new HashSet<int>();
            existingReadings ??= Enumerable.Empty<Domain.Models.MeterReads>().ToLookup(mr => mr.AccountId);

            return new ValidationContext(validAccountIds, existingReadings);
        }

        private MeterReadingCsvRecord CreateRecord(int accountId = 1, string dateTime = "22/04/2019 09:24", string value = "12345")
        {
            DateTime.TryParseExact(dateTime, "dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate);
            return new MeterReadingCsvRecord
            {
                AccountId = accountId,
                MeterReadingDateTime = parsedDate,
                MeterReadValue = value
            };
        }

        // --- AccountExistsRule Tests ---

        [TestMethod] // MSTest attribute
        public async Task AccountExistsRule_ValidAccountId_ReturnsSuccess()
        {
            // Arrange
            var rule = new AccountExistsRule();
            var context = CreateContext(validAccountIds: new HashSet<int> { 123, 456 });
            var record = CreateRecord(accountId: 123);

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "Result should be valid"); // MSTest assertion
            Assert.IsNull(result.ErrorMessage, "Error message should be null"); // MSTest assertion
        }

        [TestMethod]
        public async Task AccountExistsRule_InvalidAccountId_ReturnsFailure()
        {
            // Arrange
            var rule = new AccountExistsRule();
            var context = CreateContext(validAccountIds: new HashSet<int> { 123, 456 });
            var record = CreateRecord(accountId: 999); // Account 999 does not exist

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, "Result should be invalid"); // MSTest assertion
            Assert.IsNotNull(result.ErrorMessage, "Error message should not be null"); // MSTest assertion
            StringAssert.Contains(result.ErrorMessage, "Invalid AccountId: 999", "Error message content mismatch"); // MSTest assertion
        }

        // --- MeterValueFormatRule Tests ---

        // Use DataTestMethod for parameterized tests in MSTest
        [DataTestMethod]
        [DataRow("00000")]
        [DataRow("12345")]
        [DataRow("99999")]
        [DataRow("01234")]
        [DataRow("1")] // Allows 1-5 digits
        [DataRow("123")]
        public async Task MeterValueFormatRule_ValidFormat_ReturnsSuccess(string validValue)
        {
            // Arrange
            var rule = new MeterValueFormatRule();
            var context = CreateContext();
            var record = CreateRecord(value: validValue);

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, $"Value '{validValue}' should be valid");
        }

        [DataTestMethod]
        [DataRow("123456")] // Too long
        [DataRow("abcde")] // Non-numeric
        [DataRow("-1234")] // Negative
        [DataRow("12.34")] // Decimal
        [DataRow(" 1234")] // Leading space
        [DataRow("1234 ")] // Trailing space
        public async Task MeterValueFormatRule_InvalidFormat_ReturnsFailure(string invalidValue)
        {
            // Arrange
            var rule = new MeterValueFormatRule();
            var context = CreateContext();
            var record = CreateRecord(value: invalidValue);

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, $"Value '{invalidValue}' should be invalid");
            Assert.IsNotNull(result.ErrorMessage, "Error message should not be null for invalid format");
            StringAssert.Contains(result.ErrorMessage, "Invalid MeterReadValue format", "Error message content mismatch");
        }

        [DataTestMethod]
        [DataRow("")]      // Empty
        [DataRow(null)]    // Null
        public async Task MeterValueFormatRule_MissingOrEmpty_ReturnsFailure(string invalidValue)
        {
            // Arrange
            var rule = new MeterValueFormatRule();
            var context = CreateContext();
            var record = CreateRecord(value: invalidValue);

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, $"Value '{invalidValue}' should be invalid");
            Assert.IsNotNull(result.ErrorMessage, "Error message should not be null for invalid format");
            StringAssert.Contains(result.ErrorMessage, "MeterReadValue is missing or empty", "Error message content mismatch");
        }

        // --- DuplicateInBatchRule Tests ---

        [TestMethod]
        public async Task DuplicateInBatchRule_FirstOccurrence_ReturnsSuccess()
        {
            // Arrange
            var rule = new DuplicateInBatchRule();
            var context = CreateContext(); // ProcessedInBatch is initialized inside
            var record = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24", value: "00001");

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "First occurrence should be valid");
            Assert.AreEqual(1, context.ProcessedInBatch?.Count, "ProcessedInBatch count should be 1"); // Check item was added
        }

        [TestMethod]
        public async Task DuplicateInBatchRule_SecondOccurrence_ReturnsFailure()
        {
            // Arrange
            var rule = new DuplicateInBatchRule();
            var context = CreateContext();
            var record1 = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24", value: "00001");
            var record2 = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24", value: "00001"); // Identical

            // Act
            var result1 = await rule.ValidateAsync(record1, context);
            var result2 = await rule.ValidateAsync(record2, context); // Try adding the same one again

            // Assert
            Assert.IsTrue(result1.IsValid, "First occurrence should be valid");
            Assert.IsFalse(result2.IsValid, "Second occurrence should be invalid");
            Assert.IsNotNull(result2.ErrorMessage, "Error message should not be null for duplicate");
            StringAssert.Contains(result2.ErrorMessage, "Duplicate entry found within the uploaded file/batch", "Error message content mismatch");
            Assert.AreEqual(1, context.ProcessedInBatch?.Count, "ProcessedInBatch count should still be 1"); // Still only one unique item in the set
        }

        [TestMethod]
        public async Task DuplicateInBatchRule_DifferentRecords_ReturnsSuccess()
        {
            // Arrange
            var rule = new DuplicateInBatchRule();
            var context = CreateContext();
            var record1 = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24", value: "00001");
            var record2 = CreateRecord(accountId: 2, dateTime: "22/04/2019 09:24", value: "00001"); // Different account
            var record3 = CreateRecord(accountId: 1, dateTime: "23/04/2019 09:24", value: "00001"); // Different time
            var record4 = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24", value: "00002"); // Different value

            // Act
            var result1 = await rule.ValidateAsync(record1, context);
            var result2 = await rule.ValidateAsync(record2, context);
            var result3 = await rule.ValidateAsync(record3, context);
            var result4 = await rule.ValidateAsync(record4, context);

            // Assert
            Assert.IsTrue(result1.IsValid, "Record 1 should be valid");
            Assert.IsTrue(result2.IsValid, "Record 2 should be valid");
            Assert.IsTrue(result3.IsValid, "Record 3 should be valid");
            Assert.IsTrue(result4.IsValid, "Record 4 should be valid");
            Assert.AreEqual(4, context.ProcessedInBatch?.Count, "ProcessedInBatch count should be 4"); // 4 unique items added
        }

        [TestMethod]
        public async Task DuplicateInBatchRule_InvalidValueFormat_ReturnsFailure()
        {
            // Arrange
            var rule = new DuplicateInBatchRule();
            var context = CreateContext();
            var record = CreateRecord(value: "INVALID"); // Value cannot be parsed

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid due to value format");
            StringAssert.Contains(result.ErrorMessage, "Invalid value format", "Error message content mismatch");
        }


        // --- DuplicateInDbRule Tests ---

        [TestMethod]
        public async Task DuplicateInDbRule_ReadingDoesNotExist_ReturnsSuccess()
        {
            // Arrange
            var mockRepo = new Mock<IMeterReadingRepository>();
            mockRepo.Setup(r => r.DoesReadingExistAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                    .ReturnsAsync(false); // Simulate reading NOT found

            var rule = new DuplicateInDbRule(mockRepo.Object);
            var context = CreateContext();
            var record = CreateRecord(value: "11111");

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "Result should be valid when reading doesn't exist in DB");
            // Verify repository method was called with correct parameters
            mockRepo.Verify(r => r.DoesReadingExistAsync(record.AccountId, record.MeterReadingDateTime, 11111), Times.Once);
        }

        [TestMethod]
        public async Task DuplicateInDbRule_ReadingExists_ReturnsFailure()
        {
            // Arrange
            var mockRepo = new Mock<IMeterReadingRepository>();
            mockRepo.Setup(r => r.DoesReadingExistAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()))
                    .ReturnsAsync(true); // Simulate reading IS found

            var rule = new DuplicateInDbRule(mockRepo.Object);
            var context = CreateContext();
            var record = CreateRecord(value: "22222");

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, "Result should be invalid when reading exists in DB");
            Assert.IsNotNull(result.ErrorMessage, "Error message should not be null");
            StringAssert.Contains(result.ErrorMessage, "Duplicate entry already exists in the database.", "Error message content mismatch");
            mockRepo.Verify(r => r.DoesReadingExistAsync(record.AccountId, record.MeterReadingDateTime, 22222), Times.Once);
        }

        [TestMethod]
        public async Task DuplicateInDbRule_InvalidValueFormat_ReturnsFailure()
        {
            // Arrange
            var mockRepo = new Mock<IMeterReadingRepository>(); // Repo setup doesn't matter here
            var rule = new DuplicateInDbRule(mockRepo.Object);
            var context = CreateContext();
            var record = CreateRecord(value: "INVALID"); // Value cannot be parsed

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, "Should be invalid due to value format");
            StringAssert.Contains(result.ErrorMessage, "Invalid value format", "Error message content mismatch");
            mockRepo.Verify(r => r.DoesReadingExistAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never); // Verify repo NOT called
        }

        // --- OlderReadingRule Tests ---

        [TestMethod]
        public async Task OlderReadingRule_NoExistingReadings_ReturnsSuccess()
        {
            // Arrange
            var rule = new OlderReadingRule();
            var context = CreateContext(); // No existing readings
            var record = CreateRecord(dateTime: "22/04/2019 09:24");

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "Should be valid with no existing readings");
        }

        [TestMethod]
        public async Task OlderReadingRule_NewerReading_ReturnsSuccess()
        {
            // Arrange
            var rule = new OlderReadingRule();
            var existing = new List<MeterReads> {
                new MeterReads { AccountId = 1, MeterReadDateTime = new DateTime(2019, 4, 20, 10, 0, 0), MeterReadValue = 100 }
            }.ToLookup(r => r.AccountId);
            var context = CreateContext(existingReadings: existing);
            var record = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24"); // Newer than 20/04

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "Newer reading should be valid");
        }

        [TestMethod]
        public async Task OlderReadingRule_SameDateTimeReading_ReturnsSuccess()
        {
            // Arrange
            var rule = new OlderReadingRule();
            var existingDate = new DateTime(2019, 4, 22, 9, 24, 0);
            var existing = new List<MeterReads> {
                new MeterReads { AccountId = 1, MeterReadDateTime = existingDate, MeterReadValue = 100 }
            }.ToLookup(r => r.AccountId);
            var context = CreateContext(existingReadings: existing);
            var record = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24"); // Same date/time

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsTrue(result.IsValid, "Reading with same date/time should be valid (duplicate check is separate)");
        }


        [TestMethod]
        public async Task OlderReadingRule_OlderReading_ReturnsFailure()
        {
            // Arrange
            var rule = new OlderReadingRule();
            var existing = new List<MeterReads> {
                new MeterReads { AccountId = 1, MeterReadDateTime = new DateTime(2019, 4, 25, 12, 0, 0), MeterReadValue = 200 } // Existing is 25/04
            }.ToLookup(r => r.AccountId);
            var context = CreateContext(existingReadings: existing);
            var record = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24"); // Older than 25/04

            // Act
            var result = await rule.ValidateAsync(record, context);

            // Assert
            Assert.IsFalse(result.IsValid, "Older reading should be invalid");
            Assert.IsNotNull(result.ErrorMessage, "Error message should not be null");
            StringAssert.Contains(result.ErrorMessage, "older than latest existing reading date", "Error message content mismatch");
        }

        [TestMethod]
        public async Task OlderReadingRule_MultipleExistingReadings_UsesLatest()
        {
            // Arrange
            var rule = new OlderReadingRule();
            var existing = new List<MeterReads> {
                new MeterReads { AccountId = 1, MeterReadDateTime = new DateTime(2019, 4, 20, 10, 0, 0), MeterReadValue = 100 },
                new MeterReads { AccountId = 1, MeterReadDateTime = new DateTime(2019, 4, 25, 12, 0, 0), MeterReadValue = 200 }, // This is the latest
                new MeterReads { AccountId = 1, MeterReadDateTime = new DateTime(2019, 4, 15, 0, 0, 0), MeterReadValue = 50 }
            }.ToLookup(r => r.AccountId);
            var context = CreateContext(existingReadings: existing);
            var recordOlder = CreateRecord(accountId: 1, dateTime: "22/04/2019 09:24"); // Older than latest (25/04)
            var recordNewer = CreateRecord(accountId: 1, dateTime: "26/04/2019 09:24"); // Newer than latest (25/04)

            // Act
            var resultOlder = await rule.ValidateAsync(recordOlder, context);
            var resultNewer = await rule.ValidateAsync(recordNewer, context);

            // Assert
            Assert.IsFalse(resultOlder.IsValid, "Older record should fail against latest");
            StringAssert.Contains(resultOlder.ErrorMessage, "older than latest existing reading date (25/04/2019 12:00)", "Older error message mismatch");

            Assert.IsTrue(resultNewer.IsValid, "Newer record should succeed against latest");
        }
    }
}
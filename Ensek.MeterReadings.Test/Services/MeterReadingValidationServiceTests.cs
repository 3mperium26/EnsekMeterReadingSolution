using Microsoft.VisualStudio.TestTools.UnitTesting; // Use MSTest
using Moq;
using Microsoft.Extensions.Logging;
using Ensek.MeterReadings.Services; // Service implementation
using Ensek.MeterReadings.Domain.Interfaces; // Interfaces
using Ensek.MeterReadings.Domain.Dtos;
using Ensek.MeterReadings.Domain.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System; // For ArgumentNullException

namespace Ensek.MeterReadings.Test.Services
{
    [TestClass] // MSTest attribute
    public class MeterReadingValidationServiceTests
    {
        // Use fields initialized in TestInitialize
        private Mock<IMeterReadingRepository> _mockRepo = null!;
        private Mock<ILogger<MeterReadingValidationService>> _mockLogger = null!;
        private List<Mock<IValidationRule>> _mockRules = null!; // Store mocks to verify calls

        // MSTest initialization method
        [TestInitialize]
        public void TestInitialize()
        {
            _mockRepo = new Mock<IMeterReadingRepository>();
            _mockLogger = new Mock<ILogger<MeterReadingValidationService>>();
            _mockRules = new List<Mock<IValidationRule>>();

            // Default setup for repository methods used in BuildValidationContextAsync
            _mockRepo.Setup(r => r.GetAccountIdsAsync()).ReturnsAsync(new HashSet<int>());
            _mockRepo.Setup(r => r.GetLatestReadingsForAccountsAsync(It.IsAny<IEnumerable<int>>()))
                     .ReturnsAsync(Enumerable.Empty<MeterReads>().ToLookup(r => r.AccountId));
        }


        // Helper to create the service with a specific set of rules
        private MeterReadingValidationService CreateService(params Mock<IValidationRule>[] rules)
        {
            _mockRules.AddRange(rules);
            // Pass the mocked objects to the constructor
            return new MeterReadingValidationService(_mockRules.Select(r => r.Object), _mockRepo.Object, _mockLogger.Object);
        }

        // Helper to create a basic record
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

        [TestMethod] // MSTest attribute
        public async Task BuildValidationContextAsync_CallsRepositoryMethods()
        {
            // Arrange
            var service = CreateService(); // No rules needed for this test
            var expectedAccountIds = new HashSet<int> { 1, 2, 3 };
            var expectedReadings = new List<MeterReads>().ToLookup(r => r.AccountId); // Empty lookup

            // Override default setup for this specific test
            _mockRepo.Setup(r => r.GetAccountIdsAsync()).ReturnsAsync(expectedAccountIds);
            _mockRepo.Setup(r => r.GetLatestReadingsForAccountsAsync(It.IsAny<IEnumerable<int>>()))
                     .ReturnsAsync(expectedReadings);

            // Act
            var context = await service.BuildValidationContextAsync();

            // Assert
            Assert.IsNotNull(context, "Context should not be null");
            CollectionAssert.AreEquivalent(expectedAccountIds.ToList(), context.ValidAccountIds.ToList(), "ValidAccountIds mismatch"); // Use CollectionAssert for sets/lists
            Assert.IsFalse(context.ExistingReadingsByAccount.Any(), "ExistingReadings should be empty"); // Check lookup content
            Assert.IsNotNull(context.ProcessedInBatch, "ProcessedInBatch should be initialized");

            _mockRepo.Verify(r => r.GetAccountIdsAsync(), Times.Once, "GetAccountIdsAsync should be called once");
            // Verify GetLatestReadingsForAccountsAsync was called with the IDs from GetAccountIdsAsync
            _mockRepo.Verify(r => r.GetLatestReadingsForAccountsAsync(
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(expectedAccountIds))), // Verify exact IDs passed
                Times.Once, "GetLatestReadingsForAccountsAsync should be called once with correct IDs");
        }

        [TestMethod]
        public async Task ValidateReadingAsync_AllRulesSucceed_ReturnsEmptyErrorList()
        {
            // Arrange
            var mockRule1 = new Mock<IValidationRule>();
            mockRule1.Setup(r => r.RequiresDbAccess).Returns(false);
            mockRule1.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Success());

            var mockRule2 = new Mock<IValidationRule>();
            mockRule2.Setup(r => r.RequiresDbAccess).Returns(true); // DB rule
            mockRule2.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Success());

            var service = CreateService(mockRule1, mockRule2);
            // Use the default empty context setup in TestInitialize
            var context = await service.BuildValidationContextAsync();
            var record = CreateRecord();

            // Act
            var errors = await service.ValidateReadingAsync(record, context);

            // Assert
            Assert.AreEqual(0, errors.Count, "Error list should be empty when all rules succeed");
            mockRule1.Verify(r => r.ValidateAsync(record, context), Times.Once, "Non-DB rule should be called");
            mockRule2.Verify(r => r.ValidateAsync(record, context), Times.Once, "DB rule should be called");
        }

        [TestMethod]
        public async Task ValidateReadingAsync_NonDbRuleFails_ReturnsErrorAndSkipsDbRules()
        {
            // Arrange
            var mockRule1 = new Mock<IValidationRule>(); // Non-DB, Fails
            mockRule1.Setup(r => r.RequiresDbAccess).Returns(false);
            mockRule1.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Failure("Non-DB Rule Failed"));

            var mockRule2 = new Mock<IValidationRule>(); // DB, Succeeds (but shouldn't be called)
            mockRule2.Setup(r => r.RequiresDbAccess).Returns(true);
            mockRule2.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Success());

            var service = CreateService(mockRule1, mockRule2);
            var context = await service.BuildValidationContextAsync();
            var record = CreateRecord();

            // Act
            var errors = await service.ValidateReadingAsync(record, context);

            // Assert
            Assert.AreEqual(1, errors.Count, "Should have one error");
            Assert.AreEqual("Non-DB Rule Failed", errors[0], "Error message mismatch");
            mockRule1.Verify(r => r.ValidateAsync(record, context), Times.Once, "Non-DB rule should be called");
            mockRule2.Verify(r => r.ValidateAsync(record, context), Times.Never, "DB rule should NOT be called"); // <<< Verify DB rule NOT called
        }

        [TestMethod]
        public async Task ValidateReadingAsync_DbRuleFails_ReturnsError()
        {
            // Arrange
            var mockRule1 = new Mock<IValidationRule>(); // Non-DB, Succeeds
            mockRule1.Setup(r => r.RequiresDbAccess).Returns(false);
            mockRule1.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Success());

            var mockRule2 = new Mock<IValidationRule>(); // DB, Fails
            mockRule2.Setup(r => r.RequiresDbAccess).Returns(true);
            mockRule2.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Failure("DB Rule Failed"));

            var service = CreateService(mockRule1, mockRule2);
            var context = await service.BuildValidationContextAsync();
            var record = CreateRecord();

            // Act
            var errors = await service.ValidateReadingAsync(record, context);

            // Assert
            Assert.AreEqual(1, errors.Count, "Should have one error");
            Assert.AreEqual("DB Rule Failed", errors[0], "Error message mismatch");
            mockRule1.Verify(r => r.ValidateAsync(record, context), Times.Once, "Non-DB rule should be called");
            mockRule2.Verify(r => r.ValidateAsync(record, context), Times.Once, "DB rule should be called");
        }

        [TestMethod]
        public async Task ValidateReadingAsync_MultipleRulesFail_ReturnsAllErrorsFromFirstStage()
        {
            // Arrange
            var mockRule1 = new Mock<IValidationRule>(); // Non-DB, Fails
            mockRule1.Setup(r => r.RequiresDbAccess).Returns(false);
            mockRule1.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Failure("Non-DB Fail 1"));

            var mockRule2 = new Mock<IValidationRule>(); // Non-DB, Fails
            mockRule2.Setup(r => r.RequiresDbAccess).Returns(false);
            mockRule2.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Failure("Non-DB Fail 2"));

            var mockRule3 = new Mock<IValidationRule>(); // DB, Fails (but shouldn't be called)
            mockRule3.Setup(r => r.RequiresDbAccess).Returns(true);
            mockRule3.Setup(r => r.ValidateAsync(It.IsAny<MeterReadingCsvRecord>(), It.IsAny<ValidationContext>()))
                     .ReturnsAsync(ValidationResult.Failure("DB Fail"));


            var service = CreateService(mockRule1, mockRule2, mockRule3);
            var context = await service.BuildValidationContextAsync();
            var record = CreateRecord();

            // Act
            var errors = await service.ValidateReadingAsync(record, context);

            // Assert
            Assert.AreEqual(2, errors.Count, "Should only contain non-DB errors");
            CollectionAssert.Contains(errors, "Non-DB Fail 1", "Error list missing 'Non-DB Fail 1'");
            CollectionAssert.Contains(errors, "Non-DB Fail 2", "Error list missing 'Non-DB Fail 2'");

            mockRule1.Verify(r => r.ValidateAsync(record, context), Times.Once, "Non-DB rule 1 should be called");
            mockRule2.Verify(r => r.ValidateAsync(record, context), Times.Once, "Non-DB rule 2 should be called");
            mockRule3.Verify(r => r.ValidateAsync(record, context), Times.Never, "DB rule should be skipped"); // DB rule skipped
        }
    }
}
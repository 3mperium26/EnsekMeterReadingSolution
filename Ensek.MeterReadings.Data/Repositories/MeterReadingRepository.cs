using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ensek.MeterReadings.Domain.Models; // Use Domain models
using Ensek.MeterReadings.Domain.Interfaces; // Implement Domain interface
using System.Linq; // Required for ILookup and other LINQ methods

namespace Ensek.MeterReadings.Data.Repositories
{
    /// <summary>
    /// Repository implementation for accessing meter reading and account data
    /// using Entity Framework Core.
    /// </summary>
    public class MeterReadingRepository : IMeterReadingRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MeterReadingRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the MeterReadingRepository.
        /// </summary>
        /// <param name="context">The database context injected by DI.</param>
        /// <param name="logger">The logger instance injected by DI.</param>
        public MeterReadingRepository(ApplicationDbContext context, ILogger<MeterReadingRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Adds a collection of meter readings to the database.
        /// </summary>
        /// <param name="readings">The readings to add.</param>
        /// <returns>The number of readings successfully saved.</returns>
        public async Task<int> AddMeterReadingsAsync(IEnumerable<Domain.Models.MeterReads> readings)
        {
            if (readings == null || !readings.Any())
            {
                _logger.LogDebug("AddMeterReadingsAsync called with no readings to add.");
                return 0; // Nothing to add
            }

            // Add the range of readings to the context
            await _context.MeterReads.AddRangeAsync(readings);
            try
            {
                // Save changes to the database
                var savedCount = await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully saved {Count} meter readings.", savedCount);
                return savedCount;
            }
            catch (DbUpdateException ex) // Catch specific EF Core update exceptions
            {
                // Log detailed error, including inner exception if available
                _logger.LogError(ex, "Error saving meter readings to database. Inner Exception: {InnerMessage}", ex.InnerException?.Message);

                // Check for specific SQL Server unique constraint violation error number (2627 or 2601)
                var sqlException = ex.InnerException as Microsoft.Data.SqlClient.SqlException;
                if (sqlException != null && (sqlException.Number == 2627 || sqlException.Number == 2601))
                {
                    _logger.LogWarning("A unique constraint violation occurred during bulk save. Some duplicates might exist.");
                }
                throw; // Re-throw the exception so the calling layer knows the operation failed
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                _logger.LogError(ex, "An unexpected error occurred while saving meter readings.");
                throw;
            }
        }

        /// <summary>
        /// Checks if a specific meter reading already exists in the database.
        /// </summary>
        /// <param name="accountId">Account ID.</param>
        /// <param name="readingDate">Reading date and time.</param>
        /// <param name="readingValue">Reading value.</param>
        /// <returns>True if the reading exists, false otherwise.</returns>
        public async Task<bool> DoesReadingExistAsync(int accountId, DateTime readingDate, int readingValue)
        {
            // Use AsNoTracking for read-only queries to improve performance, as we don't need to track changes.
            return await _context.MeterReads
                                 .AsNoTracking()
                                 .AnyAsync(mr =>
                                    mr.AccountId == accountId &&
                                    mr.MeterReadDateTime == readingDate &&
                                    mr.MeterReadValue == readingValue);
        }

        /// <summary>
        /// Retrieves an account by its ID.
        /// </summary>
        /// <param name="accountId">The ID of the account.</param>
        /// <returns>The account, or null if not found.</returns>
        public async Task<Domain.Models.Account?> GetAccountByIdAsync(int accountId)
        {
            // Use AsNoTracking as we likely only need to read account details here.
            return await _context.Accounts
                                 .AsNoTracking()
                                 .FirstOrDefaultAsync(a => a.AccountId == accountId);
        }

        /// <summary>
        /// Gets a set of all active account IDs.
        /// </summary>
        /// <returns>A HashSet containing all account IDs.</returns>
        public async Task<ISet<int>> GetAccountIdsAsync()
        {
            // Efficiently select only the AccountId column.
            var ids = await _context.Accounts
                                    .Select(a => a.AccountId)
                                    .ToListAsync();
            // Return as a HashSet for efficient lookups (O(1) average time complexity for Contains).
            return new HashSet<int>(ids);
        }

        /// <summary>
        /// Gets existing meter readings for specified accounts, grouped by Account ID.
        /// </summary>
        /// <param name="accountIds">The accounts to fetch readings for.</param>
        /// <returns>A Lookup mapping Account ID to its meter readings.</returns>
        public async Task<ILookup<int, Domain.Models.MeterReads>> GetLatestReadingsForAccountsAsync(IEnumerable<int> accountIds)
        {
            if (accountIds == null || !accountIds.Any())
            {
                // Return an empty lookup if no account IDs are provided.
                return Enumerable.Empty<Domain.Models.MeterReads>().ToLookup(mr => mr.AccountId);
            }

            var readings = await _context.MeterReads
                                   .Where(mr => accountIds.Contains(mr.AccountId)) // Filter by the provided account IDs
                                   .AsNoTracking() // Use AsNoTracking for read-only data needed for validation context
                                   .ToListAsync(); // Execute the query and load data into memory

            // Group the results by AccountId in memory using ToLookup for efficient access in validation rules.
            return readings.ToLookup(mr => mr.AccountId);
        }
    }
}
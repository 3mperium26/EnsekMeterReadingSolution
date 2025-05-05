using Ensek.MeterReadings.Domain.Models; // Use models from Domain

namespace Ensek.MeterReadings.Domain.Interfaces
{
    /// <summary>
    /// Handles data persistence for meter readings and accounts.
    /// SRP: Responsible only for database interactions.
    /// </summary>
    public interface IMeterReadingRepository
    {
        /// <summary>
        /// Gets a set of all valid Account IDs from the data store.
        /// </summary>
        /// <returns>A set of integers representing Account IDs.</returns>
        Task<ISet<int>> GetAccountIdsAsync();

        /// <summary>
        /// Gets existing meter readings for a specified set of accounts, grouped by account ID.
        /// Used primarily for the "older reading" validation rule.
        /// </summary>
        /// <param name="accountIds">The account IDs to fetch readings for.</param>
        /// <returns>A lookup where the key is the Account ID and the value is a collection of MeterReadings for that account.</returns>
        Task<ILookup<int, Models.MeterReads>> GetLatestReadingsForAccountsAsync(IEnumerable<int> accountIds);

        /// <summary>
        /// Checks if a specific meter reading (identified by account, date, and value) already exists.
        /// </summary>
        /// <param name="accountId">The account ID.</param>
        /// <param name="readingDate">The date and time of the reading.</param>
        /// <param name="readingValue">The value of the meter reading.</param>
        /// <returns>True if the reading exists, false otherwise.</returns>
        Task<bool> DoesReadingExistAsync(int accountId, DateTime readingDate, int readingValue);

        /// <summary>
        /// Adds a collection of new meter readings to the data store.
        /// </summary>
        /// <param name="readings">The meter readings to add.</param>
        /// <returns>The number of readings successfully added.</returns>
        Task<int> AddMeterReadingsAsync(IEnumerable<Models.MeterReads> readings);

        /// <summary>
        /// Gets a specific account by its ID.
        /// </summary>
        /// <param name="accountId">The ID of the account to retrieve.</param>
        /// <returns>The Account object, or null if not found.</returns>
        Task<Models.Account?> GetAccountByIdAsync(int accountId);
    }
}
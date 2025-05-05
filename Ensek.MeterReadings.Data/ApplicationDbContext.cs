using Microsoft.EntityFrameworkCore;
using Ensek.MeterReadings.Domain.Models; // Use models from Domain
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Ensek.MeterReadings.Data
{
    /// <summary>
    /// Represents the database context for the application using Entity Framework Core.
    /// Manages the connection to the database and the mapping of domain models to database tables.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        /// <summary>
        /// Gets or sets the Accounts table in the database.
        /// </summary>
        public DbSet<Account> Accounts { get; set; }
        /// <summary>
        /// Gets or sets the MeterReads table in the database.
        /// </summary>
        public DbSet<MeterReads> MeterReads { get; set; }

        /// <summary>
        /// Initializes a new instance of the ApplicationDbContext.
        /// Called by Dependency Injection.
        /// </summary>
        /// <param name="options">The options to be used by the DbContext.</param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Configures the model schema using the Fluent API.
        /// </summary>
        /// <param name="modelBuilder">The builder being used to construct the model for this context.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- MeterReading Entity Configuration ---
            // Configure composite unique index on MeterReading table
            // Ensures uniqueness based on AccountId, DateTime, and Value, preventing duplicate readings.
            modelBuilder.Entity<MeterReads>()
                .HasIndex(mr => new { mr.AccountId, mr.MeterReadDateTime, mr.MeterReadValue }, "IX_MeterRead_Unique")
                .IsUnique();

            // Configure relationships (optional if conventions like naming FKs are followed, but explicit is clearer)
            modelBuilder.Entity<MeterReads>()
                .HasOne(mr => mr.Account) // MeterReading has one Account
                .WithMany(a => a.MeterReadings) // Account has many MeterReadings
                .HasForeignKey(mr => mr.AccountId) // The foreign key property in MeterReading
                .OnDelete(DeleteBehavior.Cascade); // Optional: Define delete behavior (Cascade deletes readings if account is deleted)

            // --- Account Entity Configuration ---
            // (No specific Fluent API configuration needed here beyond table/key attributes in the model)

            // --- Data Seeding ---
            // Seed initial account data from CSV file.
            SeedAccounts(modelBuilder);
        }

        /// <summary>
        /// Seeds the Accounts table with initial data from the Test_Accounts.csv file.
        /// This method is called during the model creation process.
        /// </summary>
        /// <param name="modelBuilder">The model builder instance.</param>
        private void SeedAccounts(ModelBuilder modelBuilder)
        {
            var accounts = new List<Account>();
            // Path relative to the *execution* directory (usually the Web project's bin folder during runtime)
            // Ensure Test_Accounts.csv is copied to the output directory of the *startup* project (Web).
            var filePath = Path.Combine(AppContext.BaseDirectory, "DataSeed", "Test_Accounts.csv");

            if (!File.Exists(filePath))
            {
                // Log a warning if the seed file cannot be found.
                Console.WriteLine($"Warning: Seed file not found at {filePath}. Skipping account seeding. Ensure Test_Accounts.csv is set to 'Copy to Output Directory' in the Web project.");
                return; // Stop seeding if the file is missing.
            }

            // Configure CsvHelper to read the file
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true, // Assumes the first row is a header
                TrimOptions = TrimOptions.Trim, // Trim whitespace from fields
                MissingFieldFound = null, // Allow missing fields without throwing error (handle later if needed)
                HeaderValidated = null, // Don't strictly validate header names if they might vary slightly
            };

            try
            {
                // Read records from the CSV file
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, config))
                {
                    // Register the mapping configuration for the Account model
                    csv.Context.RegisterClassMap<AccountMap>();
                    // Get all records and convert them to a list
                    accounts = csv.GetRecords<Account>().ToList();
                }

                // If accounts were successfully read, add them to the database model for seeding.
                if (accounts.Any())
                {
                    Console.WriteLine($"Seeding {accounts.Count} accounts from {filePath}...");
                    // Use HasData to seed the database. EF Core handles inserting these records.
                    modelBuilder.Entity<Account>().HasData(accounts);
                    Console.WriteLine("Account seeding successful.");
                }
                else
                {
                    Console.WriteLine($"Warning: No accounts found in seed file {filePath}.");
                }
            }
            catch (Exception ex)
            {
                // Log any errors encountered during the seeding process.
                Console.WriteLine($"Error seeding accounts from {filePath}: {ex.ToString()}");
                // Depending on requirements, might re-throw or handle more gracefully.
                throw; // Re-throwing ensures the problem is visible during startup/migration.
            }
        }
    }

    /// <summary>
    /// Defines the mapping between the Test_Accounts.csv columns and the Account model properties
    /// for use with CsvHelper during seeding.
    /// </summary>
    public sealed class AccountMap : ClassMap<Account>
    {
        /// <summary>
        /// Map CSV column names (case-insensitive by default) to model properties.
        /// </summary>
        public AccountMap()
        {
            Map(m => m.AccountId).Name("AccountId");
            Map(m => m.FirstName).Name("FirstName");
            Map(m => m.LastName).Name("LastName");
        }
    }
}
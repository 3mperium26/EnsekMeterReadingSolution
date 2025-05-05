# Ensek Meter Reading Upload Application

## Project Overview

This application provides a solution for uploading customer meter readings via a CSV file. It includes both a web-based user interface (MVC) and a RESTful API endpoint for processing the uploads. The application validates each reading against a set of rules and stores valid readings in a SQL Server database. It also seeds initial customer account data from a provided CSV file.

## Technology Stack

* **.NET 8** (or specify the target framework used, e.g., .NET 6/7)
* **ASP.NET Core:** For building the Web API and MVC application.
* **Entity Framework Core 8:** For data access and interaction with the database.
* **SQL Server:** As the relational database management system.
* **CsvHelper:** For parsing CSV files.
* **Swagger/OpenAPI:** For API documentation and testing (optional, included in development).

## Architecture

The solution follows a clean architecture pattern with distinct projects for different concerns:

* **`Ensek.MeterReadings.Domain`:** Contains core domain models (Entities like `Accounts`, `MeterReads`), Data Transfer Objects (DTOs like `MeterReadingCsvRecord`), ViewModels, and service interfaces.
* **`Ensek.MeterReadings.Data`:** Handles data persistence using EF Core. Includes the `ApplicationDbContext`, repository implementations (`MeterReadingRepository`), and EF Core migrations. Responsible for database interactions and seeding initial account data.
* **`Ensek.MeterReadings.Services`:** Contains the business logic, including CSV parsing (`CsvParsingService`), validation rules (`IValidationRule` implementations), validation coordination (`MeterReadingValidationService`), and the overall upload process orchestration (`MeterReadingUploadOrchestrator`).
* **`Ensek.MeterReadings.Web`:** The main ASP.NET Core application hosting both the MVC user interface (Controllers, Views) and the Web API endpoint (`MeterReadingUploadController`). Handles user requests, dependency injection setup, and configuration.

## Features

* **CSV Upload (MVC):** User-friendly web interface (`/`) to upload the `Meter_Reading.csv` file.
* **CSV Upload (API):** RESTful endpoint `POST /api/meter-reading-uploads` accepting multipart/form-data for programmatic uploads.
* **Data Validation:** Each meter reading is validated against the following criteria:
    * **Account Exists:** The `AccountId` must correspond to an existing account in the database (seeded from `Test_Accounts.csv`).
    * **Value Format:** The `MeterReadValue` must be in the format NNNNN (1 to 5 digits).
    * **Database Duplicates:** An identical reading (same AccountId, DateTime, and Value) must not already exist in the database.
    * **Batch Duplicates:** An identical reading must not appear multiple times within the *same* uploaded CSV file.
    * **Older Readings:** A new reading's `MeterReadDateTime` must not be earlier than the latest existing reading for the same account (Nice to Have requirement).
* **Data Persistence:** Valid meter readings are saved to the `MeterReads` table in the configured SQL Server database.
* **Account Seeding:** The `Accounts` table is automatically seeded with data from `Test_Accounts.csv` when the database is created/migrated.
* **Upload Results:** Both the MVC UI and the API endpoint return a summary indicating the number of successful and failed readings, along with specific error messages for failed entries.
* **API Documentation:** Includes Swagger UI (available at `/swagger` in development) for easy testing and exploration of the API endpoint.

## Setup Instructions

1.  **Prerequisites:**
    * .NET SDK (Version 8.0 or the version targeted by the projects).
    * SQL Server Instance (e.g., LocalDB, Developer Edition, Express Edition, Azure SQL).

2.  **Clone/Download:** Obtain the project source code.

3.  **Configuration:**
    * Open the `Ensek.MeterReadings.Web/appsettings.json` file.
    * Locate the `ConnectionStrings` section.
    * Update the `DefaultConnection` value with the correct connection string for your SQL Server instance. Ensure the user specified has permissions to create databases and tables, or that the target database exists.
    * **Security Note:** For production, avoid storing sensitive credentials directly in `appsettings.json`. Use User Secrets, Environment Variables, Azure Key Vault, or other secure configuration methods.

4.  **Build the Solution:**
    * Open a terminal or command prompt in the root `EnsekMeterReadingSolution` directory.
    * Run: `dotnet build`

5.  **Database Setup (EF Core Migrations):**
    * Ensure the `Ensek.MeterReadings.Web` project is set as the startup project (this is needed for EF Core tools to find the configuration).
    * **Option A (Automatic - Recommended for Dev/Demo):** If `ApplyMigrationsOnStartup` is set to `true` in `appsettings.Development.json` (the default), running the application (Step 6) should automatically create the database and apply migrations.
    * **Option B (Manual):**
        * Run the following command to apply migrations and create/update the database:
            ```bash
            dotnet ef database update --startup-project Ensek.MeterReadings.Web --project Ensek.MeterReadings.Data
            ```
        * This command reads the connection string from `appsettings.json`, connects to the database, applies any pending migrations defined in the `Ensek.MeterReadings.Data` project, and runs the data seeding logic.

6.  **Run the Application:**
    * Navigate to the Web project directory: `cd Ensek.MeterReadings.Web`
    * Run the application: `dotnet run`
    * Alternatively, run from the solution root: `dotnet run --project Ensek.MeterReadings.Web`
    * The application will typically be available at `https://localhost:<port>` and `http://localhost:<port>` (check the console output for the exact URLs).

## Usage

### MVC Web Interface

1.  Open your web browser and navigate to the application's base URL (e.g., `https://localhost:7123/`).
2.  You will see the "Upload Meter Readings" page.
3.  Click the "Choose File" or similar button and select the `Meter_Reading.csv` file (or another CSV with the same format).
4.  Click the "Upload File" button.
5.  The page will refresh, displaying the results below the form:
    * Counts for successful and failed readings.
    * A detailed list of errors if any readings failed validation or parsing.

### Web API Endpoint

1.  **Endpoint:** `POST /api/meter-reading-uploads`
2.  **Method:** `POST`
3.  **Body:** `multipart/form-data` containing the CSV file. The form field name for the file should be `file`.
4.  **Testing with Swagger (Development):**
    * Navigate to `/swagger` in your browser (e.g., `https://localhost:7123/swagger`).
    * Expand the `POST /api/meter-reading-uploads` section.
    * Click "Try it out".
    * Under the "file" parameter, click "Choose File" and select your `Meter_Reading.csv`.
    * Click "Execute".
    * The response body will contain a JSON object (`MeterReadingUploadResult`) with `successfulReadings`, `failedReadings`, and an `errors` array.
5.  **Testing with Tools (e.g., Postman, curl):**
    * Set the request method to `POST`.
    * Set the URL to `https://localhost:<port>/api/meter-reading-uploads`.
    * Set the body type to `form-data`.
    * Add a key named `file`, set its type to `File`, and select your `Meter_Reading.csv`.
    * Send the request. The response body will be the JSON result.

    *Example using `curl`:*
    ```bash
    curl -X POST "https://localhost:7123/api/meter-reading-uploads" -H "accept: application/json" -H "Content-Type: multipart/form-data" -F "file=@C:\path\to\your\Meter_Reading.csv"
    ```
    *(Replace the URL and file path accordingly)*

## Project Structure Details

* **`EnsekMeterReadingSolution/`** (Root Folder)
    * `.gitignore`: Standard Visual Studio gitignore file.
    * `EnsekMeterReadingSolution.sln`: Visual Studio Solution file.
    * `Meter_Reading.csv`: Example data file provided for upload testing.
    * **`Ensek.MeterReadings.Domain/`**: Class library for core entities, DTOs, ViewModels, and interfaces.
    * **`Ensek.MeterReadings.Data/`**: Class library for data access (EF Core DbContext, Repositories, Migrations, Seeding).
    * **`Ensek.MeterReadings.Services/`**: Class library for business logic (Parsing, Validation, Orchestration).
    * **`Ensek.MeterReadings.Web/`**: ASP.NET Core application (MVC Controllers/Views, API Controllers, `Program.cs`, `appsettings.json`, `wwwroot`, `DataSeed/Test_Accounts.csv`).

## Notes

* Ensure the SQL Server instance is running and accessible with the configured connection string.
* The `Test_Accounts.csv` file located in `Ensek.MeterReadings.Web/DataSeed/` is crucial for the initial database seeding. Ensure its "Copy to Output Directory" property is set to "Copy if newer" or "Copy always" in the Web project's properties.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // Required for Table attribute

namespace Ensek.MeterReadings.Domain.Models
{
    [Table("Accounts")] // Explicitly name the table
    public class Account
    {
        [Key]
        public int AccountId { get; set; }

        [Required]
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        public string? LastName { get; set; }

        // Navigation property
        public virtual ICollection<MeterReads> MeterReadings { get; set; } = new List<MeterReads>();
    }
}
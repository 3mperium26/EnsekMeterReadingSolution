using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ensek.MeterReadings.Domain.Models
{
    [Table("MeterReads")] // Explicitly name the table
    public class MeterReads
    {
        [Key]
        public long MeterReadId { get; set; } // Surrogate Key

        [Required]
        public int AccountId { get; set; } // Foreign Key

        [Required]
        public DateTime MeterReadDateTime { get; set; }

        [Required]
        [Range(0, 99999)] // Ensures 0 <= value <= 99999
        public int MeterReadValue { get; set; }

        // Navigation property
        [ForeignKey("AccountId")] // Explicitly define FK relationship
        public virtual Account? Account { get; set; }
    }
}
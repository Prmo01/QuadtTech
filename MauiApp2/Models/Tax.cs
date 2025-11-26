using System;
using System.ComponentModel.DataAnnotations;

namespace MauiApp2.Models
{
    public class Tax
    {
        public int tax_id { get; set; }
        
        [Required]
        public string tax_name { get; set; } = string.Empty;
        
        [Required]
        public string tax_type { get; set; } = string.Empty; // 'VATable', 'VAT-Exempt', 'Zero-Rated'
        
        public decimal tax_rate { get; set; } = 0.12m; // Default 12%
        
        public bool is_active { get; set; } = true;
        
        public DateTime created_date { get; set; } = DateTime.Now;
    }
}

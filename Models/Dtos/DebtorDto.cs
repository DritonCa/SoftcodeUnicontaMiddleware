namespace SoftcodeUnicontaMiddleware.Models.Dtos
{
    public class DebtorDto
    {
        // Identity
        public string Account { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }

        // Address
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string ZipCode { get; set; }
        public string City { get; set; }
        public string CountryCode { get; set; }

        // Contact
        public string ContactPerson { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Mobile { get; set; }

        // Financial
        public string PaymentTerms { get; set; }
        public int PaymentMethod { get; set; }
        public double CreditMax { get; set; }
        public double Balance { get; set; }
        public double Overdue { get; set; }
        public bool Blocked { get; set; }

        // ✅ Dynamic (FLAT, SAFE)
        public Dictionary<string, object> Extensions { get; set; }
    }
}

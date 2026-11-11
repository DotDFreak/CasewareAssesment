namespace LeaseImport.Core.Models
{
    public class ImportResult
    {
        public List<Lease> Leases { get; set; } = new List<Lease>();

        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
    }
}

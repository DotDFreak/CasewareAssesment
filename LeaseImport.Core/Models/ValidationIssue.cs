namespace LeaseImport.Core.Models
{
    public class ValidationIssue
    {
        public int LineNumber { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
    }
}

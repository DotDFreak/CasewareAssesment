namespace LeaseImport.Core.Models
{
    public class Lease
    {
        public string LeaseId { get; set; }
        public string AssetClass { get; set; }
        public string Description { get; set; }
        public string Currency { get; set; }
        public decimal Opening { get; set; }
        public decimal Additions { get; set; }
        public decimal Depreciation { get; set; }
        public decimal Disposals { get; set; }

    }
}

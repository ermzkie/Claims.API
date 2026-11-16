namespace Claims.API.Models
{
    public class AnnualBalanceResult
    {
        public string? SourceDesc { get; set; }
        public decimal? Release { get; set; }
        public decimal? Obligated { get; set; }
        public Int16? Year { get; set; }
    }
}
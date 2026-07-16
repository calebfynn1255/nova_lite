using SQLite;

namespace NovaLite.Setup;

[Table("BenchmarkResults")]
public class BenchmarkResult
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string ModelName { get; set; } = string.Empty;
    public DateTime RunDate { get; set; } = DateTime.UtcNow;

    public double LoadTimeMs { get; set; }
    public double FirstTokenLatencyMs { get; set; }
    public double AverageTokensPerSecond { get; set; }
    public long PeakRamBytes { get; set; }
    public double PeakCpuUsagePercent { get; set; }
}

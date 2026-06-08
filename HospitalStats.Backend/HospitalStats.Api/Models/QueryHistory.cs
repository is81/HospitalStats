using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalStats.Api.Models;

public class QueryHistory
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? QueryConfigId { get; set; }

    [MaxLength(200)]
    public string QueryConfigName { get; set; } = string.Empty;

    public string? FiltersJson { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public int RowCount { get; set; }

    public long ElapsedMs { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}

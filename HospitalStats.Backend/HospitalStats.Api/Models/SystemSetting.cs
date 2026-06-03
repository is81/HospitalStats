using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class SystemSetting
{
    [Key, MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;
}

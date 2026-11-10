using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IMCAPI.DB.Entities;

/// <summary>Immutable item snapshot and deletion/restoration ledger; deliberately has no relations.</summary>
public sealed class ItemArchive
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ArchiveId { get; set; }
    public int ItemId { get; set; }
    [Required, MaxLength(1000)]
    public string Label { get; set; } = "";
    [Required, MaxLength(50)]
    public string Icon { get; set; } = "";
    public int? ParentId { get; set; }
    public string? Attributes { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    [MaxLength(320)]
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(320)]
    public string? UpdatedBy { get; set; }
    public DateTime DeletedAtUtc { get; set; }
    [MaxLength(450)]
    public string? DeletedById { get; set; }
    [MaxLength(320)]
    public string? DeletedBy { get; set; }
    public string? AttachmentsJson { get; set; }
    public DateTime? RestoredAtUtc { get; set; }
    [MaxLength(450)]
    public string? RestoredById { get; set; }
}

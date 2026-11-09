using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection.Metadata;

namespace IMCAPI.DB.Entities
{
    public class Base
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public virtual int Id { get; set; }
        public virtual bool IsActive { get; set; }
        public virtual DateTime? UpdatedAt { get; set; }
        public virtual string? UpdatedBy { get; set; }

    }

    public class Item : Base
    {
        public string Label{ get; set; }
        public string Icon { get; set; }
        public int? ParentId { get; set; }
        public string? Attributes { get; set; }

        public Item? Parent { get; set; }
        public List<Item> Children { get; set; } = new();
        public List<Attachment> Attachments { get; set; } = new();

        public Item()
        {
            Icon = "";
            Label = "";
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }

    }
    public class Attachment : Base
    {
        public int ItemId { get; set; }
        public string? Type { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public string? FileBase64 { get; set; }
        public string? Attributes { get; set; }

        public Item? Item{ get; set; }

        public Attachment()
        {
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public class Constants: Base
    {
        public override int Id { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public string? Group { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }

        public int? ParentId { get; set; }
        public Constants? Parent { get; set; }
        public List<Constants> Children { get; set; } = new();

        public Constants()
        {
            Label = "";
            Icon = "";
            IsActive = true;
            UpdatedAt = DateTime.UtcNow;
        }

    }
}

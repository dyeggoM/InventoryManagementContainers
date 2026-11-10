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
        public virtual DateTime? CreatedAt { get; set; }
        public virtual string? CreatedBy { get; set; }
        public virtual DateTime? UpdatedAt { get; set; }
        public virtual string? UpdatedBy { get; set; }

    }

    public class Item : Base
    {
        /// <summary>Shown in the tree, so an empty one is refused by model validation.</summary>
        [Required(AllowEmptyStrings = false, ErrorMessage = "A record needs a name")]
        [MaxLength(1000)]
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
        /// <summary>Path of the original file on disk, relative to the content root.</summary>
        public string? FilePath { get; set; }

        /// <summary>Small compressed preview as a data URL; the original lives on disk.</summary>
        public string? ThumbnailBase64 { get; set; }

        /// <summary>The uploaded original as a data URL. Input only: it is written to disk and
        /// replaced by <see cref="FilePath"/> plus <see cref="ThumbnailBase64"/>.</summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? UploadBase64 { get; set; }
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
        /// <summary>
        /// How the value is edited, for constants that describe an item attribute: Text, Number,
        /// Money, Date or LongText. Null means Text, so rows written before this column exist keep
        /// working.
        /// </summary>
        public string? DataType { get; set; }
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
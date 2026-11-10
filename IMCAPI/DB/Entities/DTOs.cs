using System.Linq;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IMCAPI.DB.Entities
{

    public class ItemDTO
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public int? ParentId { get; set; }
        public string? Attributes { get; set; }
        /// <summary>How many files the record carries; the list only needs to know it has some.</summary>
        public int AttachmentCount { get; set; }

        public ItemDTO? Parent { get; set; }
        public List<ItemDTO> Children { get; set; } = new();
        public List<AttachmentDTO> Attachments { get; set; } = new();
        public ItemDTO()
        {
            Icon = "";
            Label = "";
        }
        public ItemDTO(int id, string label, string? attributes, int? parentId, List<Attachment> attachments)
        {
            // CTOR for panels
            Id = id;
            Label = label;
            Attributes = attributes;
            ParentId = parentId;
            Parent = null;
            Children = new();
            Attachments = attachments.Count() > 0 ? attachments.Select(x => new AttachmentDTO(x)).ToList() : new List<AttachmentDTO>();
            Icon = "";
        }
        public ItemDTO(Item data, bool includeChildren = true) : this(data, includeChildren, new HashSet<int>()) { }

        private ItemDTO(Item data, bool includeChildren, HashSet<int> visited)
        {
            // CTOR for tree
            Id = data.Id;
            var alreadyVisited = !visited.Add(data.Id);
            Label = data.Label;
            Attributes = data.Attributes;
            ParentId = data.ParentId;
            Parent = !alreadyVisited && data.Parent != null && !visited.Contains(data.Parent.Id)
                ? new ItemDTO(data.Parent, includeChildren: false, new HashSet<int> { data.Id })
                : null;
            Children = !alreadyVisited && includeChildren && data.Children != null && data.Children.Any()
                ? data.Children.Where(x => !visited.Contains(x.Id)).Select(x => new ItemDTO(x, includeChildren: true, new HashSet<int>(visited))).ToList()
                : new List<ItemDTO>();
            Attachments = data.Attachments.Count() > 0 ? data.Attachments.Select(x => new AttachmentDTO(x)).ToList() : new List<AttachmentDTO>();
            Icon = "";
        }
    }


    public class AttachmentDTO
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string? Type { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public string? ThumbnailBase64 { get; set; }
        public string? Attributes { get; set; }
        public AttachmentDTO()
        {
        }
        public AttachmentDTO(Attachment data)
        {
            Id = data.Id;
            ItemId = data.ItemId;
            Type = data.Type;
            FileName = data.FileName;
            FilePath = data.FilePath;
            ThumbnailBase64 = data.ThumbnailBase64;
            Attributes = data.Attributes;
        }
    }


    public class ConstantsDTO
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public string? Group { get; set; }
        public string? Description { get; set; } //TODO: Rename to Attributes
        public int SortOrder { get; set; }
        /// <summary>How an attribute value is edited (Text, Number, Money, Date, LongText).</summary>
        public string? DataType { get; set; }

        public int? ParentId { get; set; }
        public ConstantsDTO? Parent { get; set; }
        public List<ConstantsDTO> Children { get; set; } = new();

        public ConstantsDTO()
        {
            Icon = "";
            Label = "";
        }

        public ConstantsDTO(int id, string? group, string label, string? description, int sortOrder, int? parentId, ConstantsDTO? parent, List<ConstantsDTO> children, string? dataType = null)
        {
            // CTOR for panels
            Id = id;
            Group = group;
            Label = label;
            Description = description;
            SortOrder = sortOrder;
            ParentId = parentId;
            Parent= parent;
            Children = children;
            DataType = dataType;
            Icon = "";
        }

        public ConstantsDTO(Constants data, bool includeChildren = true) : this(data, includeChildren, new HashSet<int>()) { }

        private ConstantsDTO(Constants data, bool includeChildren, HashSet<int> visited)
        {
            // CTOR for tree
            Id = data.Id;
            var alreadyVisited = !visited.Add(data.Id);
            Group = data.Group;
            Label = data.Label;
            Description = data.Description;
            DataType = data.DataType;
            SortOrder = data.SortOrder;
            ParentId = data.ParentId;
            Parent = !alreadyVisited && data.Parent != null && !visited.Contains(data.Parent.Id)
                ? new ConstantsDTO(data.Parent, includeChildren: false, new HashSet<int> { data.Id })
                : null;
            Children = !alreadyVisited && includeChildren && data.Children != null && data.Children.Any()
                ? data.Children.Where(x => !visited.Contains(x.Id)).Select(x => new ConstantsDTO(x, includeChildren: true, new HashSet<int>(visited))).ToList()
                : new List<ConstantsDTO>();
            Icon = "";
        }
    }
}
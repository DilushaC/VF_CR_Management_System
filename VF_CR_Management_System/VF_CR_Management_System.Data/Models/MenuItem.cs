using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class MenuItem
    {
        public int Id { get; set; }

        public int? ParentMenuItemId { get; set; }
        public string ParentMenuTitle { get; set; } = string.Empty;

        public string MenuTitle { get; set; } = string.Empty;

        public int? PageId { get; set; }

        public string PageName { get; set; } = string.Empty;

        public string? IconClass { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; }

        public int? ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public List<int> PageIds { get; set; } = new List<int>();
        public string? PageUrl { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
    }
}

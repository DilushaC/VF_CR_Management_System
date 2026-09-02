using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class UserModel
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Password { get; set; }
        public string DisplayName { get; set; }
        public string DisplayDesignation { get; set; }
        public string Email { get; set; }
        public string DisplayDepartment { get; set; }
        public bool IsActive { get; set; }
        public List<int> ProductIds { get; set; } = new List<int>();
        public List<string> PageUrls { get; set; } = new List<string>();
        public List<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
    }
}

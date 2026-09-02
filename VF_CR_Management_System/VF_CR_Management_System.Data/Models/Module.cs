using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class Module
    {
        public int Id { get; set; }
        public string ModuleName { get; set; }
        public int DepartmentID { get; set; }
        public int VendorID { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class PendingApprovalViewModel
    {
        public int ApprovalID { get; set; }
        public int CRID { get; set; }
        public string? StepName { get; set; }
        public bool? IsApproved { get; set; }
    }
}

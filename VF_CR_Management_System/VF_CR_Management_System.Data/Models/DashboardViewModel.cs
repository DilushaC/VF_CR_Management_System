using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Models
{
    public class DashboardViewModel
    {
        public int TotalCRs { get; set; }
        public int PendingApprovals { get; set; }
        public int TotalHolds { get; set; }
        public int RejectedCRs { get; set; }
    }
}

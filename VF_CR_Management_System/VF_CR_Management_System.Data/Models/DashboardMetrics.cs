using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class DashboardMetrics
    {
        public int TotalCRs { get; set; }
        public int ApprovedCRs { get; set; }
        public int DeployedCRs { get; set; }
        public int RejectedCRs { get; set; }
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}

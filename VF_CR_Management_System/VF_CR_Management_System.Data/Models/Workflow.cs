using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class Workflow
    {
        public int WorkflowID { get; set; }
        public int DivisionID { get; set; }
        public string WorkflowName { get; set; }
        public string Description { get; set; }
        public bool Active { get; set; }

        // Populated via JOIN for display only - not sent back on save.
        public string DivisionName { get; set; }
    }
}

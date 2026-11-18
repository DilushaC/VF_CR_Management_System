using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class WorkflowStep
    {
        public int StepID { get; set; }
        public int WorkflowID { get; set; }
        public int StepOrder { get; set; }
        public string StepName { get; set; }
        public bool Active { get; set; }

        // Populated via JOIN for display only - not sent back on save.
        public string WorkflowName { get; set; }
    }
}

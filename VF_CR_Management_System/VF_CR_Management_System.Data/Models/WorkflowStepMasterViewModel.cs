using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class WorkflowStepMasterViewModel
    {
        public IEnumerable<WorkflowStep> Steps { get; set; }
        public IEnumerable<Workflow> Workflows { get; set; }
    }
}

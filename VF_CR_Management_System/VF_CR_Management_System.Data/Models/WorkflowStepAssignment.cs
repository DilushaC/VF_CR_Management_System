using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class WorkflowStepAssignment
    {
        public int StepID { get; set; }
        public int StepOrder { get; set; }
        public string StepName { get; set; }
        public bool ApprovalRequired { get; set; }
        public string AssignedTo { get; set; }        // username from Approval
        public string AssignedToName { get; set; }    // full name (resolved)
        public DateTime? AssignedDate { get; set; }
        public DateTime? TargetDate { get; set; }
        public string Decision { get; set; }
        public bool? IsApproved { get; set; }
    }
}

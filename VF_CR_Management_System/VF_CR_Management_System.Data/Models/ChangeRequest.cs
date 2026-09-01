using System;
using System.ComponentModel.DataAnnotations;

namespace VF_CR_Management_System.Data.Models
{
    public class ChangeRequest
    {
        public int CRID { get; set; }

        public string CRNumber { get; set; }

        public string UserName { get; set; }

        [Required(ErrorMessage = "Please provide a change summary and business justification.")]
        public string Summary { get; set; }

        public int ModuleID { get; set; }

        [Required(ErrorMessage = "Please select a change type.")]
        public int ChangeTypeID { get; set; }

        // Only used when ChangeTypeID corresponds to "Other" - holds the free-text description
        public string OtherChangeType { get; set; }

        [Required(ErrorMessage = "Please select a change priority.")]
        public int PriorityID { get; set; }

        public int WorkflowID { get; set; }

        public int StatusID { get; set; }

        public string BusinessImpact { get; set; }

        public string Reason { get; set; }

        public string ExpectedBenefit { get; set; }

        public string RollbackPlan { get; set; }

        public DateTime RequestedDate { get; set; }

        public DateTime? DueDate { get; set; }

        public DateTime? CompletedDate { get; set; }

        public bool Active { get; set; }

        public int VendorID { get; set; }

        public int EmpID { get; set; }
    }
}

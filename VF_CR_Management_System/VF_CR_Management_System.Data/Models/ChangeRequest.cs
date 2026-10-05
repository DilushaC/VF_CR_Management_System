using System;
using System.Collections.Generic;
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
        public string ChangeTitle { get; set; }
        public int DivisionID { get; set; }
        public int ModuleID { get; set; }
        [Required(ErrorMessage = "Please select a change type.")]
        public int ChangeTypeID { get; set; }
        public string OtherChangeType { get; set; }
        [Required(ErrorMessage = "Please select a change priority.")]
        public int PriorityID { get; set; }
        [Required(ErrorMessage = "Please select an approver.")]
        public string ApproverID { get; set; }
        public int WorkflowID { get; set; }
        public int StatusID { get; set; }
        public string FixedAssets { get; set; }
        public string ActivitiesTasks { get; set; }
        public string ProposalNumber { get; set; }
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
        public int ImpactID { get; set; }
        public string ChangeImpactName { get; set; }

        // Display-only properties, populated via joined lookup tables in list queries.
        public string ChangeType { get; set; }
        public string Priority { get; set; }
        public string Module { get; set; }
        public string Division { get; set; }
        public string Status { get; set; }
        public string RequestedBy { get; set; }
        public string Vendor { get; set; }
        public string RiskAssessment { get; set; }
        public string ChangeImpact { get; set; }

        // Existing (kept as they are)
        public string? ApproverUserName { get; set; }        
        public string? ImplementerUserName { get; set; }
        public string? ISOfficerUserName { get; set; }

        // NEW: one property per workflow step, populated from Approval by StepID
        public string? AssessmentUserName { get; set; }        // StepID 2 - Assessment
        public string? SecurityUserName { get; set; }          // StepID 3 - Security
        public string? TestingUserName { get; set; }           // StepID 4 - Testing
        public string? FinalApproverUserName { get; set; }     // StepID 5 - Final Approval
        public string? TesterUserName { get; set; }            // StepID 6 - TesterAssignment
        public string? TestingApproverUserName { get; set; }   // StepID 7 - TestingApproval

        // NEW: populated separately after the main query, not part of the SQL projection
        public List<Attachment> Attachments { get; set; } = new();
        public List<Testing> Tests { get; set; } = new();
        public List<WorkflowStepAssignment> StepAssignments { get; set; } = new();
    }
}
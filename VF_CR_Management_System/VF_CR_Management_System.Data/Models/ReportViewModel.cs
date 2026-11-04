using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VF_CR_Management_System.Data.Models
{
    public class ReportViewModel
    {
        public ChangeRequest ChangeRequest { get; set; } = new ChangeRequest();
        public Testing Testing { get; set; } = new Testing();
        public bool IsPdfExport { get; set; } = false;

        // Added: per-person identity details pulled from Users table
        public UserIdentity Requester { get; set; } = new UserIdentity();
        public UserIdentity Implementer { get; set; } = new UserIdentity();
        public UserIdentity DeptHeadApprover { get; set; } = new UserIdentity();
        public UserIdentity SecurityOfficer { get; set; } = new UserIdentity();
        public UserIdentity Tester { get; set; } = new UserIdentity();
    }

    public class UserIdentity
    {
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
    }
}

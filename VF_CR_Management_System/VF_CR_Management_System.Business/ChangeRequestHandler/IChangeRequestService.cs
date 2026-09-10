using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Data.Models;
namespace VF_CR_Management_System.Business.ChangeRequestHandler
{
    public interface IChangeRequestService
    {
        Task<int> CreateChangeRequestAsync(IFormCollection collection, string empId);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsDraftsAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsSubmissionsAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsRejectionsAsync(string empNo);
        Task<ChangeRequest> GetChangeRequestByIdAsync(int crId);
        Task<string> GetAssignedApproverUserNameAsync(int crId);
        Task<bool> UpdateChangeRequestAsync(int crId, IFormCollection collection, string userName, string empNo);
        Task<bool> ApproveChangeRequestAsync(int crId, int approverId, string approvedByEmpId);
        Task<bool> RejectChangeRequestAsync(int crId, string rejectReason, string rejectedByEmpId);
        Task<bool> SubmitChangeRequestAsync(int crId, string submittedByEmpId);
        Task<bool> DeleteChangeRequestAsync(int crId, string requestedByEmpId);
    }
}

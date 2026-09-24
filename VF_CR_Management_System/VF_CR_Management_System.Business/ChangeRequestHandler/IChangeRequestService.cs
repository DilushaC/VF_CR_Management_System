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
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAssessmentsAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsSecurityAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingAssignAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingQueueAsync(string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsTestingApprovalsAsync(string empNo);
        Task<ChangeRequest> GetChangeRequestByIdAsync(int crId);
        Task<string> GetAssignedApproverUserNameAsync(int crId);
        Task<bool> UpdateChangeRequestAsync(int crId, IFormCollection collection, string userName, string empNo);
        Task<bool> ApproveChangeRequestAsync(int crId, int approverId, string approvedByEmpId);
        Task<bool> AssignTesterAsync(int crId, int testerId, string approvedByEmpId);
        Task<bool> AssignFinalApproverAsync(int crId, int finalApproverId, string approvedByEmpId);
        Task<bool> RejectChangeRequestAsync(int crId, string rejectReason, string rejectedByEmpId);
        Task<bool> SubmitChangeRequestAsync(int crId, string submittedByEmpId);
        Task<bool> DeleteChangeRequestAsync(int crId, string requestedByEmpId);
        Task<bool> CreateAssessmentAsync(int crId, IFormCollection collection, string userName, string empNo);
        Task<bool> CreateAssessmentSecurityAsync(int crId, IFormCollection collection, string userName, string empId);
        Task<bool> CreateTestingAsync(int crId, IFormCollection collection, string userName, string empId);
        Task<IEnumerable<ChangeRequest>> GetChangeImpactsAsync();
        Task<IEnumerable<Attachment>> GetAttachmentsByCrIdAsync(int crId);
        Task<Attachment> GetAttachmentByIdAsync(int attachmentId);
        Task<bool> DeleteAttachmentAsync(int attachmentId, string deletedByEmpId);
    }
}
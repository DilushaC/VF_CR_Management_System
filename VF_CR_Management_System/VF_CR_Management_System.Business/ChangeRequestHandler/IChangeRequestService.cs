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
        Task<bool> CreateChangeRequestAsync(IFormCollection collection, string userName, string empNo);
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAsync(string empNo, string filter);
        Task<bool> ApproveChangeRequestAsync(int crId, int approverId, string approvedByEmpId);

        Task<bool> RejectChangeRequestAsync(int crId, string rejectReason, string rejectedByEmpId);

        Task<bool> DeleteChangeRequestAsync(int crId, string requestedByEmpId);
    }
}

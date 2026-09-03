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
        Task<IEnumerable<ChangeRequest>> GetAllChangeRequestsAsync();
    }
}

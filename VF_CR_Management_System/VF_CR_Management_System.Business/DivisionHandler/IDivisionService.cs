using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.DivisionHandler
{
    public interface IDivisionService
    {
        Task<List<Division>> GetAllDivisionsAsync();
    }
}

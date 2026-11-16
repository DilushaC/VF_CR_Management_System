using System.Collections.Generic;
using System.Threading.Tasks;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.MastersHandler
{
    public interface IMastersService
    {
        Task<IEnumerable<Vendor>> GetAllVendorsAsync();
        Task<Vendor> GetVendorByIdAsync(int vendorId);
        Task<int> AddVendorAsync(Vendor vendor);
        Task<bool> UpdateVendorAsync(Vendor vendor);
        Task<bool> DeleteVendorAsync(int vendorId);
    }
}

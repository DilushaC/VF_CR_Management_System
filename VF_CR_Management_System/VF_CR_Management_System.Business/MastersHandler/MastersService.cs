using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VF_CR_Management_System.Business.ConnectionHandler;
using VF_CR_Management_System.Data.Models;

namespace VF_CR_Management_System.Business.MastersHandler
{
    public class MastersService : IMastersService
    {
        private readonly _ConnectionService _connectionService;

        public MastersService(_ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public Task<IEnumerable<Vendor>> GetAllVendorsAsync()
        {
            const string sql = @"
                SELECT
                    VendorID,
                    VendorName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Vendor]
                ORDER BY VendorID";

            var result = _connectionService.Query<Vendor>(sql, new { })
                            ?? Enumerable.Empty<Vendor>();

            return Task.FromResult(result);
        }

        public Task<Vendor> GetVendorByIdAsync(int vendorId)
        {
            if (vendorId <= 0)
                throw new ArgumentException("Invalid Vendor.");

            const string sql = @"
                SELECT
                    VendorID,
                    VendorName,
                    Description,
                    Active
                FROM [CRManagementDB].[dbo].[Vendor]
                WHERE VendorID = @VendorID";

            var result = _connectionService.Query<Vendor>(sql, new { VendorID = vendorId });
            var vendor = result?.FirstOrDefault();

            return Task.FromResult(vendor);
        }

        public Task<int> AddVendorAsync(Vendor vendor)
        {
            if (vendor == null)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            const string sql = @"
                INSERT INTO [CRManagementDB].[dbo].[Vendor]
                    (VendorName, Description, Active)
                VALUES
                    (@VendorName, @Description, @Active);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());
            parameters.Add("@Active", vendor.Active);

            var scalarResult = _connectionService.ExecuteScalar(sql, parameters);
            var newVendorId = scalarResult != null ? Convert.ToInt32(scalarResult) : 0;

            return Task.FromResult(newVendorId);
        }

        public Task<bool> UpdateVendorAsync(Vendor vendor)
        {
            if (vendor == null || vendor.VendorID <= 0)
                throw new ArgumentException("Invalid Vendor.");

            if (string.IsNullOrWhiteSpace(vendor.VendorName))
                throw new ArgumentException("Vendor name is required.");

            const string sql = @"
                UPDATE [CRManagementDB].[dbo].[Vendor]
                SET VendorName  = @VendorName,
                    Description = @Description,
                    Active      = @Active
                WHERE VendorID  = @VendorID";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorName", vendor.VendorName.Trim());
            parameters.Add("@Description", (vendor.Description ?? string.Empty).Trim());
            parameters.Add("@Active", vendor.Active);
            parameters.Add("@VendorID", vendor.VendorID);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }

        public Task<bool> DeleteVendorAsync(int vendorId)
        {
            if (vendorId <= 0)
                throw new ArgumentException("Invalid Vendor.");

            const string sql = @"
                DELETE FROM [CRManagementDB].[dbo].[Vendor]
                WHERE VendorID = @VendorID";

            var parameters = new DynamicParameters();
            parameters.Add("@VendorID", vendorId);

            int rowsAffected = _connectionService.ExecuteWithPara(sql, parameters);

            return Task.FromResult(rowsAffected > 0);
        }
    }
}
